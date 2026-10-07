param([string]$MsiPath = 'artifacts/installer/PotPlayerNext-0.2.6-win-x64.msi', [string]$ReportPath = 'docs/evidence/installer-background-026-static.json')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msi = [IO.Path]::GetFullPath((Join-Path $root $MsiPath))
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($msi,0))
function Rows([string]$table, [string[]]$columns) {
    $sql = 'SELECT ' + (($columns | ForEach-Object { '`' + $_ + '`' }) -join ',') + ' FROM `' + $table + '`'
    $view = $database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@($sql))
    try {
        [void]$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
        while ($record = $view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)) {
            try {
                $row = [ordered]@{}
                for ($i=0; $i -lt $columns.Count; $i++) { $row[$columns[$i]] = $record.GetType().InvokeMember('StringData','GetProperty',$null,$record,@($i+1)) }
                [pscustomobject]$row
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}
try {
    $properties = @(Rows 'Property' @('Property','Value'))
    $registry = @(Rows 'Registry' @('Registry','Root','Key','Name','Value','Component_'))
    $actions = @(Rows 'CustomAction' @('Action','Type','Source','Target'))
    $sequence = @(Rows 'InstallExecuteSequence' @('Action','Condition','Sequence'))
    $components = @(Rows 'Component' @('Component','Attributes','Condition'))
    $files = @(Rows 'File' @('File','FileName','Version'))
    foreach ($resource in @('SplashWindow.xbf','BrandLogo.xbf')) {
        if (!($files | Where-Object { ($_.FileName -split '\|')[-1] -eq $resource })) { throw "Missing startup resource in MSI: $resource" }
    }
    $startup = @($registry | Where-Object Name -eq 'PotPlayerNextPreview')
    if ($startup.Count -ne 1 -or $startup[0].Root -ne '1' -or $startup[0].Value -ne '"[#MainExe]" --background') { throw 'Incorrect per-user startup registration.' }
    if (($properties | Where-Object Property -eq 'PREVIEWENABLED').Value -ne '1') { throw 'Preview default is not enabled.' }
    $component = $components | Where-Object Component -eq 'PreviewAutoStart'
    if (!$component -or ([int]$component.Attributes -band 64) -eq 0 -or $component.Condition -notmatch 'PREVIEWENABLED') { throw 'Startup component must be conditional and transitive.' }
    $start = $actions | Where-Object Action -eq 'StartPreviewHost'
    $stop = $actions | Where-Object Action -eq 'StopPreviewHost'
    if ($start.Target -ne '--background' -or ([int]$start.Type -band 192) -ne 192) { throw 'Resident launch must not wait.' }
    if ($stop.Target -ne '--stop-background' -or ([int]$stop.Type -band 128) -ne 0) { throw 'Maintenance stop must be synchronous.' }
    $startSequence = $sequence | Where-Object Action -eq 'StartPreviewHost'
    $stopSequence = $sequence | Where-Object Action -eq 'StopPreviewHost'
    if ([int]$startSequence.Sequence -le [int]($sequence | Where-Object Action -eq 'InstallFinalize').Sequence) { throw 'Start must occur after installation commits.' }
    if ([int]$stopSequence.Sequence -ge [int]($sequence | Where-Object Action -eq 'InstallInitialize').Sequence -or $stopSequence.Condition -ne 'Installed') { throw 'Stop must occur only on installed-version maintenance before payload changes.' }
    if ($registry | Where-Object { $_.Key -match 'UserChoice' }) { throw 'Package must not write UserChoice.' }
    $normalize = $sequence | Where-Object { $_.Condition -match 'EXPLORERPREVIEWPREFERENCE' -and $_.Condition -match '#0' }
    if (!$normalize) { throw 'DWORD disabled preference must be normalized.' }
    $result = @{ passed=$true; timestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); msiSha256=(Get-FileHash $msi).Hash; scope='Read-only MSI database inspection, NOT install/upgrade/uninstall or Explorer acceptance.'; version=($properties | Where-Object Property -eq 'ProductVersion').Value; fileCount=$files.Count; startup=$startup; component=$component; actions=@($start,$stop); sequence=@($startSequence,$stopSequence); disabledPreference=$normalize; userChoiceWrites=0 }
    $result | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $root $ReportPath)
    Write-Output 'PASS: MSI offline background startup, maintenance sequencing, preference and no UserChoice writes.'
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
