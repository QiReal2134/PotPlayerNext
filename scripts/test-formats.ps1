param([string]$Executable='artifacts/release-030/PotPlayerNext.exe', [string]$Manifest='artifacts/format-fixtures-030/manifest.json')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=[IO.Path]::GetFullPath((Join-Path $root $Executable))
if (!$exe.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only a workspace executable is allowed.' }
$fixtures=Get-Content -LiteralPath (Join-Path $root $Manifest) -Raw | ConvertFrom-Json
if (!$fixtures.passed) { throw 'Fixture generation was not successful.' }
$directory=Join-Path $root ('artifacts/format-launch/'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory)
$runs=@()
foreach ($fixture in $fixtures.fixtures | Where-Object status -eq 'created') {
    $path=[IO.Path]::GetFullPath($fixture.path)
    if (!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only a workspace fixture is allowed.' }
    if ((Get-FileHash -LiteralPath $path).Hash -ne $fixture.sha256) { throw 'Fixture digest differs.' }
    $eventsPath=Join-Path $directory ($fixture.extension.TrimStart('.')+'.jsonl')
    $info=[Diagnostics.ProcessStartInfo]::new($exe); $info.UseShellExecute=$false
    $info.ArgumentList.Add('--preview'); $info.ArgumentList.Add('--'); $info.ArgumentList.Add($path)
    $info.Environment['PPN_TEST_EVENTS']=$eventsPath
    $process=[Diagnostics.Process]::Start($info)
    try {
        $watch=[Diagnostics.Stopwatch]::StartNew(); $loaded=$false
        $token=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($path)))
        while ($watch.Elapsed.TotalSeconds -lt 25) {
            if ($process.HasExited) { throw "Format $($fixture.format) exited: $($process.ExitCode)" }
            $events=@(); if (Test-Path -LiteralPath $eventsPath) {
                foreach ($line in Get-Content -LiteralPath $eventsPath) { try { $events+=($line | ConvertFrom-Json) } catch { } }
            }
            if ($events | Where-Object name -in @('preview-error','operation-error','ui-unhandled-error','library-window-created','splash-started')) { throw "Format $($fixture.format) reported an error or incorrect startup path." }
            $activated=$events | Where-Object { $_.name -eq 'preview-activated' -and $_.data.fileToken -eq $token }
            $decoded=$events | Where-Object { $_.name -in @('image-decoded','video-opened') -and $_.data.width -eq $fixture.width -and $_.data.height -eq $fixture.height }
            $focus=$events | Where-Object { $_.name -eq 'preview-keyboard-focus' -and $_.data.focused }
            if ($activated -and $decoded -and $focus) { $loaded=$true; break }
            Start-Sleep -Milliseconds 100
        }
        if (!$loaded) { throw "No actual decode/focus for $($fixture.format): $eventsPath" }
        Start-Sleep -Milliseconds 300
        if ($process.HasExited) { throw 'Process exited after decoding.' }
        $runs+=@{format=$fixture.format; extension=$fixture.extension; passed=$true; width=$fixture.width; height=$fixture.height; events=$eventsPath}
        Write-Output "PASS: actual WinUI $($fixture.format) preview, dimensions and keyboard focus."
    } finally { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
@{passed=$true; timestampUtc=[DateTimeOffset]::UtcNow.ToString('o'); dllSha256=(Get-FileHash (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'PotPlayerNext.dll')).Hash; scope='Real workspace WinUI decode/focus; no keyboard injection/Explorer interaction/visual acceptance'; runs=$runs; optionalUnsupported=@($fixtures.fixtures | Where-Object status -ne 'created')} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'results.json')
Write-Output "Format report: $directory/results.json"
