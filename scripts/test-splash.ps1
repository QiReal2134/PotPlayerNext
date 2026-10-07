param([string]$Executable = 'artifacts/release-026/PotPlayerNext.exe', [int]$Repetitions = 3)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath((Join-Path $root $Executable))
if (!$exe.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Only a workspace test executable is allowed.' }
if ($Repetitions -lt 1 -or $Repetitions -gt 10) { throw 'Use 1-10 bounded repetitions.' }
$directory = Join-Path $root ('artifacts/splash-test/' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory -Force)
$runs = @()
for ($i = 0; $i -lt $Repetitions; $i++) {
    $eventsPath = Join-Path $directory "$i.jsonl"
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.UseShellExecute = $false
    $info.Environment['PPN_TEST_EVENTS'] = $eventsPath
    $process = [Diagnostics.Process]::Start($info)
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $complete = $false
        while ($watch.Elapsed.TotalSeconds -lt 15) {
            if ($process.HasExited) { throw "Startup exited before main window: $($process.ExitCode)" }
            if (Test-Path -LiteralPath $eventsPath) {
                $events = @(Get-Content -LiteralPath $eventsPath | ForEach-Object { try { $_ | ConvertFrom-Json } catch {} })
                if ($events | Where-Object name -in @('splash-error','ui-unhandled-error','operation-error')) { throw "Application reported an error: $eventsPath" }
                if ($events | Where-Object name -eq 'library-window-created') {
                    $policy = @($events | Where-Object name -eq 'library-startup-policy')
                    if ($policy.Count -ne 1) { throw 'No startup motion policy evidence.' }
                    $names = @($events.name)
                    if ($policy[0].data.animationsEnabled) {
                        $start = @($events | Where-Object name -eq 'splash-started')
                        $end = @($events | Where-Object name -eq 'splash-completed')
                        if ($start.Count -ne 1 -or $end.Count -ne 1) { throw 'Missing splash lifecycle.' }
                        if ($names.IndexOf('splash-started') -ge $names.IndexOf('splash-completed') -or $names.IndexOf('splash-completed') -ge $names.IndexOf('library-window-created')) { throw 'Incorrect splash/main ordering.' }
                        $elapsed = ([DateTimeOffset]$end[0].timestampUtc - [DateTimeOffset]$start[0].timestampUtc).TotalMilliseconds
                        if ($elapsed -lt 700 -or $elapsed -gt 5000 -or $start[0].data.shownInSwitchers) { throw "Incorrect splash duration/window contract: $elapsed" }
                        $runs += @{ animation='completed'; elapsedMs=$elapsed; eventsPath=$eventsPath }
                    } else {
                        if ($names -contains 'splash-started') { throw 'System motion preference was ignored.' }
                        $runs += @{ animation='skipped: system animations disabled'; eventsPath=$eventsPath }
                    }
                    $complete = $true; break
                }
            }
            Start-Sleep -Milliseconds 100
        }
        if (!$complete) { throw "No main window after bounded startup wait: $eventsPath" }
        Start-Sleep -Milliseconds 750
        if ($process.HasExited) { throw 'Application exited after opening main window.' }
        if (Get-Content -LiteralPath $eventsPath | Where-Object { ($_ | ConvertFrom-Json).name -in @('splash-error','ui-unhandled-error','operation-error') }) { throw 'Post-startup error.' }
    } finally {
        # Only this harness's own child process; no user window or installed background host.
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
$report = @{ passed=$true; timestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); executableSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'PotPlayerNext.dll')).Hash; scope='Native WinUI splash lifecycle/order/duration, main remains alive. No UI input or screenshot; not visual animation acceptance.'; runs=$runs }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'artifacts/splash-test/results.json')
Write-Output "PASS: $Repetitions native splash/main startup runs."
