param([Parameter(Mandatory=$true)][int]$ProcessId, [int]$DurationSeconds = 60)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($DurationSeconds -lt 10 -or $DurationSeconds -gt 600) { throw 'Measurement duration must be 10-600 seconds.' }
$process = Get-Process -Id $ProcessId -ErrorAction Stop
if (![IO.Path]::GetFullPath($process.Path).StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or $process.ProcessName -ne 'PotPlayerNext') { throw 'Only a workspace PotPlayerNext test process may be measured.' }
$clock = [Diagnostics.Stopwatch]::StartNew()
$lastTime = 0.0; $lastCpu = $process.TotalProcessorTime.TotalSeconds
$samples = @(); $privateWorkingSet = @()
while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.HasExited) { throw 'Test process exited during measurement.' }
    $time = $clock.Elapsed.TotalSeconds; $cpu = $process.TotalProcessorTime.TotalSeconds
    $samples += [pscustomobject]@{
        seconds=[Math]::Round($time,3)
        normalizedCpuPercent=[Math]::Round(($cpu-$lastCpu)/($time-$lastTime)/[Environment]::ProcessorCount*100,4)
        workingSetBytes=$process.WorkingSet64
        privateBytes=$process.PrivateMemorySize64
        handles=$process.HandleCount
    }
    $lastTime=$time; $lastCpu=$cpu
    if ($privateWorkingSet.Count -eq 0 -or ($privateWorkingSet.Count -eq 1 -and $time -ge $DurationSeconds/2)) {
        $counter = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $ProcessId"
        $privateWorkingSet += [pscustomobject]@{seconds=[Math]::Round($clock.Elapsed.TotalSeconds,3); bytes=$counter.WorkingSetPrivate}
    }
}
$counter = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess = $ProcessId"
$privateWorkingSet += [pscustomobject]@{seconds=[Math]::Round($clock.Elapsed.TotalSeconds,3); bytes=$counter.WorkingSetPrivate}
$report = @{ timestampUtc=[DateTimeOffset]::UtcNow; scenario='Independent WinUI main window with 6 fixture thumbnails, video preview closed; warm process; other test instances may share DLL pages'; osVersion=[Environment]::OSVersion.Version.ToString(); logicalProcessors=[Environment]::ProcessorCount; scope='One local 60-second sample, not cold-start/4K/leak or cross-machine benchmark'; samples=$samples; privateWorkingSetSamples=$privateWorkingSet; meanNormalizedCpuPercent=($samples.normalizedCpuPercent | Measure-Object -Average).Average }
$output = Join-Path $root 'artifacts/performance'; New-Item -ItemType Directory -Path $output -Force | Out-Null
$path = Join-Path $output "idle-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')).json"
$report | ConvertTo-Json -Depth 5 | Set-Content $path
Write-Host "Report: $path"
Write-Host "Mean normalized CPU: $($report.meanNormalizedCpuPercent)%"
Write-Host "Private working set samples: $($privateWorkingSet.bytes -join ', ') bytes"
