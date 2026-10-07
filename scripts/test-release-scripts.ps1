# Publisher tests replace gh with an in-process fake. No GitHub request or release mutation.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root ('artifacts/release-script-test/' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $directory -Force)
$oldSha = $env:GITHUB_SHA
$env:GITHUB_SHA = '0123456789abcdef0123456789abcdef01234567'
$state = [pscustomobject]@{ Mode='new'; Calls=[Collections.Generic.List[object]]::new(); Checks=0 }
function gh {
    $state.Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'api') {
        $sha = if ($state.Mode -eq 'wrong-tag') { 'ffffffffffffffffffffffffffffffffffffffff' } else { $env:GITHUB_SHA }
        @{object=@{type='commit';sha=$sha}} | ConvertTo-Json -Compress
    } elseif ($args[0] -eq 'release' -and $args[1] -eq 'view') {
        if ($state.Mode -in @('draft','published')) { @{isDraft=($state.Mode -eq 'draft')} | ConvertTo-Json -Compress }
        else { $global:LASTEXITCODE = 1 }
    } elseif ($state.Mode -eq 'upload-failure' -and $args[1] -eq 'create') {
        $global:LASTEXITCODE = 1
    }
}
function Check([string]$name, [scriptblock]$body) {
    $state.Calls.Clear(); & $body; $state.Checks++; Write-Output "PASS: $name"
}
function ExpectFailure([scriptblock]$body) {
    $failed = $false; try { & $body } catch { $failed = $true }
    if (!$failed) { throw 'Expected publisher failure.' }
}
function Mutations {
    foreach ($call in $state.Calls) {
        if ($call[0] -eq 'release' -and $call[1] -in @('create','upload','edit')) { Write-Output -NoEnumerate $call }
    }
}
function Publish { & "$PSScriptRoot/publish-release.ps1" -Version '0.2.6' -Repository 'fixture/PotPlayerNext' -AssetDirectory $directory }
try {
    $names = @('PotPlayerNext-0.2.6-win-x64.msi','PotPlayerNext-0.2.6-win-x64-portable.zip','PotPlayerNext-0.2.6-source.zip')
    $lines = @()
    foreach ($name in $names) {
        $path = Join-Path $directory $name
        [IO.File]::WriteAllText($path, "Test fixture: $name")
        $line = "$((Get-FileHash $path).Hash.ToLowerInvariant())  $name"
        [IO.File]::WriteAllText("$path.sha256", "$line`n"); $lines += $line
    }
    [IO.File]::WriteAllText((Join-Path $directory 'SHA256SUMS.txt'), ($lines -join "`n") + "`n")
    @{version='0.2.6';commit=$env:GITHUB_SHA} | ConvertTo-Json | Set-Content (Join-Path $directory 'release-manifest.json')
    Check 'New release is created as draft, then published' {
        $state.Mode='new'; Publish
        $mutations = @(Mutations)
        if ($mutations.Count -ne 2 -or $mutations[0][1] -ne 'create' -or $mutations[0] -notcontains '--draft' -or $mutations[0] -notcontains '--verify-tag' -or $mutations[1][1] -ne 'edit') { throw 'Invalid publication sequence.' }
    }
    Check 'Failed upload never publishes' {
        $state.Mode='upload-failure'; ExpectFailure { Publish }
        if ($state.Calls | Where-Object { $_[1] -eq 'edit' }) { throw 'Partial release was published.' }
    }
    Check 'Existing draft can be completed' {
        $state.Mode='draft'; Publish
        $mutations = @(Mutations)
        if ($mutations.Count -ne 2 -or $mutations[0][1] -ne 'upload' -or $mutations[1][1] -ne 'edit') { throw 'Draft repair failed.' }
    }
    Check 'Published release is not overwritten' {
        $state.Mode='published'; ExpectFailure { Publish }
        if (@(Mutations).Count) { throw 'Existing published release was changed.' }
    }
    Check 'Wrong tag commit blocks publication' {
        $state.Mode='wrong-tag'; ExpectFailure { Publish }
        if (@(Mutations).Count) { throw 'Wrong commit was published.' }
    }
    Check 'Checksum mismatch blocks publication' {
        $state.Mode='new'; Add-Content (Join-Path $directory $names[0]) 'tampered'
        ExpectFailure { Publish }
        if (@(Mutations).Count) { throw 'Tampered assets were published.' }
    }
    @{passed=$true;checks=$state.Checks;scope='Publisher logic with fake in-process gh; no real GitHub API or publication.'} | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/release-script-test/results.json')
} finally {
    $env:GITHUB_SHA = $oldSha
    $resolved = [IO.Path]::GetFullPath($directory)
    $allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/release-script-test')) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup scope failed.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
