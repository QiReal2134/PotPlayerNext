param([string]$Executable = 'artifacts/qa-open-preview-024/PotPlayerNext.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$exe = [IO.Path]::GetFullPath((Join-Path $root $Executable))
if (!$exe.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Only a workspace test executable is allowed.' }
$scope = [guid]::NewGuid().ToString('N')
$directory = Join-Path $root "artifacts/background-test/$scope"
[void](New-Item -ItemType Directory -Path $directory -Force)
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string]$name, [string]$command = '--background') {
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.Arguments = $command
    $info.EnvironmentVariables['PPN_TEST_BACKGROUND_SCOPE'] = $scope
    $info.EnvironmentVariables['PPN_TEST_EVENTS'] = Join-Path $directory "$name.jsonl"
    $p = [Diagnostics.Process]::Start($info)
    $children.Add($p)
    return $p
}
function WaitEvidence($process, [string]$file, [string]$event) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 20) {
        if (Test-Path -LiteralPath $file) {
            foreach ($line in Get-Content -LiteralPath $file) {
                $value = $line | ConvertFrom-Json
                if ($value.name -eq $event) { return $value }
            }
        }
        if ($process.HasExited) { throw "Background exited before $event (code $($process.ExitCode))." }
        Start-Sleep -Milliseconds 100
    }
    throw "No $event evidence within 20 seconds."
}
try {
    $first = Launch 'first'
    $ready = WaitEvidence $first (Join-Path $directory 'first.jsonl') 'background-ready'
    if ($ready.data.visible -or $ready.data.shownInSwitchers) { throw 'Background host must be hidden and absent from switchers.' }
    $duplicate = Launch 'duplicate'
    if (!$duplicate.WaitForExit(20000) -or $duplicate.ExitCode -ne 0) { throw 'Duplicate background did not exit successfully.' }
    if (!(Get-Content (Join-Path $directory 'duplicate.jsonl') | Where-Object { ($_ | ConvertFrom-Json).name -eq 'background-duplicate' })) { throw 'No duplicate suppression evidence.' }
    if ($first.HasExited) { throw 'Original host exited after duplicate launch.' }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $stopper = Launch 'stop' '--stop-background'
    if (!$stopper.WaitForExit(15000) -or $stopper.ExitCode -ne 0) { throw 'MSI stop helper did not finish successfully.' }
    if (!$first.WaitForExit(15000) -or $first.ExitCode -ne 0) { throw 'Background shutdown failed.' }
    $restart = Launch 'restart'
    [void](WaitEvidence $restart (Join-Path $directory 'restart.jsonl') 'background-ready')
    $signal = [Threading.EventWaitHandle]::OpenExisting("Local\PotPlayerNext.Preview.Stop.$sid.Test.$scope")
    [void]$signal.Set(); $signal.Dispose()
    if (!$restart.WaitForExit(15000) -or $restart.ExitCode -ne 0) { throw 'Restart shutdown failed.' }
    $allEvents = @(Get-ChildItem -LiteralPath $directory -Filter '*.jsonl' | ForEach-Object { Get-Content -LiteralPath $_.FullName | ForEach-Object { $_ | ConvertFrom-Json } })
    if ($allEvents | Where-Object name -in @('splash-started','library-window-created','ui-unhandled-error','operation-error')) { throw 'Background path created visible startup UI or reported an error.' }
    $result = @{ passed=$true; timestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); executableSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'PotPlayerNext.dll')).Hash; scope='Isolated real WinUI background host: hidden/singleton/stop/restart. No injected keys, Explorer interaction, Run registry writes or visual acceptance.'; evidence=@('hidden host alive','switchers disabled','duplicate exits, original alive','MSI stop helper graceful stop','restart after mutex release','no splash or library window') }
    $result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'artifacts/background-test/results.json')
    Write-Output 'PASS: hidden background host, singleton, shutdown and restart.'
} finally {
    foreach ($p in $children) { if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }; $p.Dispose() }
}
