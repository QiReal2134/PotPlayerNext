param([string]$OldMsi = 'artifacts/installer/PotPlayerNext-0.2.1-win-x64.msi', [string]$NewMsi = 'artifacts/installer/PotPlayerNext-0.2.2-win-x64.msi')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$old = [IO.Path]::GetFullPath((Join-Path $root $OldMsi)); $new = [IO.Path]::GetFullPath((Join-Path $root $NewMsi))
$evidence = Join-Path $root 'artifacts/upgrade-test'
$target = [IO.Path]::GetFullPath((Join-Path $evidence 'installed-app'))
if (!$target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Upgrade test target must stay in workspace.' }
if (Test-Path -LiteralPath $target) { throw 'Upgrade target already exists.' }
if ((Get-ItemProperty 'HKCU:/Software/RegisteredApplications' -Name PotPlayerNext -ErrorAction SilentlyContinue).PotPlayerNext) { throw 'Do not overwrite an existing user registration.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$installer = New-Object -ComObject WindowsInstaller.Installer
function ProductProperty([string]$path, [string]$property) {
    $database = $installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@($path,0))
    $view = $database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@('SELECT `Value` FROM `Property` WHERE `Property` = ''' + $property + ''''))
    [void]$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
    $record = $view.GetType().InvokeMember('Fetch','InvokeMethod',$null,$view,$null)
    $value = $record.GetType().InvokeMember('StringData','GetProperty',$null,$record,@(1))
    foreach ($object in @($record,$view,$database)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($object) }
    return $value
}
function ProductState([string]$code) { return $installer.GetType().InvokeMember('ProductState','GetProperty',$null,$installer,@($code)) }
function RunMsi([string]$arguments) {
    $process = Start-Process msiexec.exe -WindowStyle Hidden -Wait -PassThru -ArgumentList $arguments
    if ($process.ExitCode -notin @(0,3010)) { throw "MSI failed: $($process.ExitCode)" }
    return $process.ExitCode
}
function Defaults {
    $types = & "$PSScriptRoot/get-media-types.ps1"
    $values = foreach ($extension in @($types.image) + @($types.video)) {
        $key = Get-ItemProperty "HKCU:/Software/Microsoft/Windows/CurrentVersion/Explorer/FileExts/$extension/UserChoice" -ErrorAction SilentlyContinue
        [pscustomobject]@{extension=$extension; progId=$key.ProgId; hash=$key.Hash}
    }
    return $values | ConvertTo-Json -Compress
}
$oldCode = ProductProperty $old 'ProductCode'; $newCode = ProductProperty $new 'ProductCode'
$oldVersion = ProductProperty $old 'ProductVersion'; $newVersion = ProductProperty $new 'ProductVersion'
if ([version]$newVersion -le [version]$oldVersion) { throw 'Upgrade must increase version.' }
$before = Defaults
try {
    $install = RunMsi "/i `"$old`" /qn INSTALLFOLDER=`"$target`" REBOOT=ReallySuppress /l*v `"$evidence/old-install.log`""
    # No INSTALLFOLDER passed: upgrade must recover the previous custom directory itself.
    $upgrade = RunMsi "/i `"$new`" /qn REBOOT=ReallySuppress /l*v `"$evidence/upgrade.log`""
    if ((ProductState $oldCode) -ne -1) { throw 'Old product is still registered.' }
    if ((ProductState $newCode) -ne 5) { throw 'New product is not installed.' }
    if (!(Test-Path -LiteralPath (Join-Path $target 'PotPlayerNext.exe'))) { throw 'Upgrade lost custom directory.' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'PotPlayerNext.exe')).ProductVersion
    if (!$version.StartsWith($newVersion, [StringComparison]::Ordinal)) { throw "Executable version mismatch: $version" }
    foreach ($progId in @('PotPlayerNext.Image','PotPlayerNext.Video')) {
        if ((Get-Item "HKCU:/Software/Classes/$progId/shell/open/command").GetValue('') -ne ('"' + (Join-Path $target 'PotPlayerNext.exe') + '" -- "%1"')) { throw 'Upgrade damaged shell command.' }
    }
    if ((Defaults) -ne $before) { throw 'Upgrade changed user defaults.' }
    $dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    & $dotnet run --project tests/ShellLaunch/ShellLaunch.csproj -c Release -- (Join-Path $target 'PotPlayerNext.exe')
    if ($LASTEXITCODE) { throw 'Upgraded player launch failed.' }
    @{passed=$true; timestampUtc=[DateTimeOffset]::UtcNow; fromVersion=$oldVersion; toVersion=$newVersion; oldProductRemoved=$true; customDirectoryPreserved=$true; defaultsUnchanged=$true; installExitCode=$install; upgradeExitCode=$upgrade; newMsiSha256=(Get-FileHash $new).Hash} | ConvertTo-Json | Set-Content (Join-Path $evidence 'results.json')
    Write-Host "PASS: $oldVersion -> $newVersion, old product removed, directory/associations retained, image/video launch passed."
} finally {
    foreach ($code in @($newCode,$oldCode)) {
        if ((ProductState $code) -eq 5) { [void](RunMsi "/x $code /qn REBOOT=ReallySuppress /l*v `"$evidence/uninstall-$code.log`"") }
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    if (@(Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Upgrade cleanup left payload files.' }
    if ((Defaults) -ne $before) { throw 'Upgrade cleanup changed defaults.' }
}
