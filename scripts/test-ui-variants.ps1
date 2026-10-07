param([string]$Executable='artifacts/release-030/PotPlayerNext.exe', [string]$Folder='artifacts/format-fixtures-030')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=[IO.Path]::GetFullPath((Join-Path $root $Executable)); $folderPath=[IO.Path]::GetFullPath((Join-Path $root $Folder))
foreach ($path in @($exe,$folderPath)) { if (!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only workspace test paths are allowed.' } }
$settings=Join-Path $env:LOCALAPPDATA 'PotPlayerNext/settings.json'
$before=if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { $null }
$directory=Join-Path $root ('artifacts/style-launch/'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory)
$runs=@()
foreach ($style in @('default','compact','cinema')) {
    $eventsPath=Join-Path $directory ($style+'.jsonl')
    $info=[Diagnostics.ProcessStartInfo]::new($exe); $info.UseShellExecute=$false
    $info.ArgumentList.Add('--'); $info.ArgumentList.Add($folderPath)
    $info.Environment['PPN_TEST_EVENTS']=$eventsPath; $info.Environment['PPN_TEST_UI_STYLE']=$style
    $process=[Diagnostics.Process]::Start($info)
    try {
        $watch=[Diagnostics.Stopwatch]::StartNew(); $ready=$false
        while ($watch.Elapsed.TotalSeconds -lt 30) {
            if ($process.HasExited) { throw "Style $style exited: $($process.ExitCode)" }
            $events=@(); if (Test-Path -LiteralPath $eventsPath) { foreach ($line in Get-Content -LiteralPath $eventsPath) { try { $events+=($line | ConvertFrom-Json) } catch { } } }
            if ($events | Where-Object name -in @('operation-error','ui-unhandled-error','preview-error')) { throw "Style $style reported UI error." }
            $applied=$events | Where-Object { $_.name -eq 'library-ui-style' -and $_.data.effective -eq $style -and $_.data.experimental -eq ($style -ne 'default') }
            $catalog=$events | Where-Object { $_.name -eq 'library-catalog-ready' -and $_.data.count -gt 0 }
            $thumbnails=@($events | Where-Object { $_.name -eq 'thumbnail-bound' -and $_.data.available })
            if ($events | Where-Object { $_.name -eq 'thumbnail-bound' -and ($_.data.estimatedBytes -gt 12MB -or $_.data.entries -gt 128) }) { throw 'Thumbnail cache exceeds contract.' }
            if ($applied -and $catalog -and $thumbnails.Count -ge 3) { $ready=$true; break }
            Start-Sleep -Milliseconds 100
        }
        if (!$ready) { throw "No actual template + catalog + thumbnails for $style : $eventsPath" }
        Start-Sleep -Milliseconds 500
        if ($process.HasExited) { throw 'Style process exited after loading.' }
        $process.Refresh()
        $runs+=@{style=$style; passed=$true; thumbnails=$thumbnails.Count; events=$eventsPath; privateBytes=$process.PrivateMemorySize64; workingSetBytes=$process.WorkingSet64}
        Write-Output "PASS: $style native template, catalog and $($thumbnails.Count) decoded thumbnails."
    } finally { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
$after=if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { $null }
if ($before -ne $after) { throw 'User settings changed during read-only style test.' }
@{passed=$true; timestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); dllSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'PotPlayerNext.dll')).Hash; scope='In-memory beta/default startup + visible template thumbnail binding; no input, registry/defaults/settings mutation or visual acceptance; memory snapshots not benchmarks'; settingsUnchanged=$true; runs=$runs} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'results.json')
Write-Output "Style report: $directory/results.json"
