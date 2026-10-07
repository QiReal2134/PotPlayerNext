param([string]$MsiPath = 'artifacts/installer/PotPlayerNext-0.2.4-win-x64.msi', [string]$TargetDirectory = 'artifacts/installer-test/installed-app', [switch]$KeepInstalled)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$msi = [IO.Path]::GetFullPath((Join-Path $root $MsiPath))
if (!(Test-Path -LiteralPath $msi)) { throw 'MSI not found.' }
$evidence = Join-Path $root 'artifacts/installer-test'
$target = [IO.Path]::GetFullPath((Join-Path $root $TargetDirectory))
if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test install target must stay inside workspace.' }
if (Test-Path -LiteralPath $target) { throw 'Test target already exists: do not overwrite an earlier installation.' }
if ((Get-ItemProperty 'HKCU:/Software/RegisteredApplications' -Name PotPlayerNext -ErrorAction SilentlyContinue).PotPlayerNext) { throw 'Existing PotPlayerNext registration found: do not overwrite a user installation.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($msi, 0))
$view = $database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode'''))
[void]$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
$record = $view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)
$productCode = $record.GetType().InvokeMember('StringData','GetProperty',$null,$record,@(1))
foreach ($object in @($record,$view,$database,$installer)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($object) }
$types = Get-Content installer/associations.json -Raw | ConvertFrom-Json
function UserChoices {
    $result = @{}
    foreach ($ext in @($types.image) + @($types.video)) {
        $value = Get-ItemProperty "HKCU:/Software/Microsoft/Windows/CurrentVersion/Explorer/FileExts/$ext/UserChoice" -ErrorAction SilentlyContinue
        $result[$ext] = @{ ProgId = $value.ProgId; Hash = $value.Hash }
    }
    return $result | ConvertTo-Json -Depth 4 -Compress
}
$before = UserChoices
$installed = $false
try {
    $installLog = Join-Path $evidence 'install.log'
    $process = Start-Process msiexec.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList "/i `"$msi`" /qn INSTALLFOLDER=`"$target`" REBOOT=ReallySuppress /l*v `"$installLog`""
    if ($process.ExitCode -notin @(0,3010)) { throw "MSI install failed: $($process.ExitCode). See $installLog" }
    $installed = $true
    foreach ($resource in @('PotPlayerNext.exe','PotPlayerNext.pri','MainWindow.xbf','PreviewWindow.xbf','potplayer_next_core.dll','LICENSE.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $target $resource))) { throw "Installed payload missing: $resource" }
    }
    $capabilities = (Get-ItemProperty 'HKCU:/Software/RegisteredApplications' -Name PotPlayerNext).PotPlayerNext
    if ($capabilities -ne 'Software\PotPlayerNext\Capabilities') { throw 'RegisteredApplications capability path mismatch.' }
    $commands = @{}
    foreach ($kind in @('image','video')) {
        $progId = if ($kind -eq 'image') { 'PotPlayerNext.Image' } else { 'PotPlayerNext.Video' }
        $command = (Get-Item "HKCU:/Software/Classes/$progId/shell/open/command").GetValue('')
        if ($command -ne ('"' + (Join-Path $target 'PotPlayerNext.exe') + '" -- "%1"')) { throw "Shell command quoting mismatch: $progId" }
        $commands[$kind] = $command
        foreach ($extension in $types.$kind) {
            if ((Get-Item "HKCU:/$capabilities/FileAssociations").GetValue($extension) -ne $progId) { throw "Missing capability: $extension" }
            if ((Get-Item "HKCU:/Software/Classes/$extension/OpenWithProgids").GetValue($progId) -ne '') { throw "Missing OpenWith entry: $extension" }
        }
    }
    if ((UserChoices) -ne $before) { throw 'Installer changed existing UserChoice defaults.' }
    $repairLog = Join-Path $evidence 'repair.log'
    $repair = Start-Process msiexec.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList "/fa $productCode /qn REBOOT=ReallySuppress /l*v `"$repairLog`""
    if ($repair.ExitCode -notin @(0,3010)) { throw "MSI repair failed: $($repair.ExitCode)" }
    if (!(Test-Path -LiteralPath (Join-Path $target 'PotPlayerNext.exe'))) { throw 'Repair lost the custom installation path.' }
    $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    & $dotnet run --project tests/ShellLaunch/ShellLaunch.csproj -c Release -- (Join-Path $target 'PotPlayerNext.exe')
    if ($LASTEXITCODE) { throw 'Installed app shell launch test failed.' }
    $report = @{ passed=$true; timestampUtc=[DateTimeOffset]::UtcNow; productCode=$productCode; msiSha256=(Get-FileHash $msi -Algorithm SHA256).Hash; installExitCode=$process.ExitCode; repairExitCode=$repair.ExitCode; associations=20; defaultChoicesUnchanged=$true; scope='MSI payload and HKCU registration, not visual/default-choice end-to-end'; installedFiles=(Get-ChildItem $target -Recurse -File).Count }
    $report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'install-results.json')
    Write-Host "PASS: MSI payload, 20 associations, quoted shell commands, defaults unchanged. Product $productCode"
} finally {
    if ($installed -and !$KeepInstalled) {
        $uninstallLog = Join-Path $evidence 'uninstall.log'
        $process = Start-Process msiexec.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList "/x $productCode /qn REBOOT=ReallySuppress /l*v `"$uninstallLog`""
        if ($process.ExitCode -notin @(0,3010)) { throw "Uninstall failed: $($process.ExitCode)" }
        if (Test-Path (Join-Path $target 'PotPlayerNext.exe')) { throw 'Uninstall left the executable.' }
        if (@(Get-ChildItem -LiteralPath $target -File -Recurse -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Uninstall left installed payload files.' }
        if ((Get-ItemProperty 'HKCU:/Software/RegisteredApplications' -Name PotPlayerNext -ErrorAction SilentlyContinue).PotPlayerNext) { throw 'Uninstall left default-app registration.' }
        foreach ($key in @('HKCU:/Software/Classes/PotPlayerNext.Image','HKCU:/Software/Classes/PotPlayerNext.Video','HKCU:/Software/PotPlayerNext/Capabilities','HKCU:/Software/Classes/Applications/PotPlayerNext.exe')) {
            if (Test-Path $key) { throw "Uninstall left registration: $key" }
        }
        foreach ($kind in @('image','video')) {
            $progId = if ($kind -eq 'image') { 'PotPlayerNext.Image' } else { 'PotPlayerNext.Video' }
            foreach ($extension in $types.$kind) {
                $key = Get-Item "HKCU:/Software/Classes/$extension/OpenWithProgids" -ErrorAction SilentlyContinue
                if ($null -ne $key -and $key.GetValueNames() -contains $progId) { throw "Uninstall left OpenWith: $extension" }
            }
        }
        if ((UserChoices) -ne $before) { throw 'Uninstall changed existing UserChoice defaults.' }
        @{passed=$true; timestampUtc=[DateTimeOffset]::UtcNow; uninstallExitCode=$process.ExitCode; defaultChoicesUnchanged=$true} | ConvertTo-Json | Set-Content (Join-Path $evidence 'uninstall-results.json')
        Write-Host 'PASS: MSI uninstall, registration cleanup, defaults unchanged.'
    }
}
