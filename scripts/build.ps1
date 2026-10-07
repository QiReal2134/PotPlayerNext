param([switch]$Publish, [switch]$TestOnly, [string]$OutputDirectory = 'artifacts/app', [string]$Version = '')
$ErrorActionPreference = 'Stop'
if (!$Version) { $Version = & "$PSScriptRoot/get-version.ps1" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric fields.' }
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$cargo = Get-Command cargo -ErrorAction SilentlyContinue
if ($cargo) { $cargoPath = $cargo.Source } else { $cargoPath = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe' }
if (!(Test-Path $cargoPath)) { throw 'Install Rust (MSVC toolchain) from https://rustup.rs/' }
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $dotnetPath = $dotnet.Source } else { $dotnetPath = Join-Path $root '.tools\dotnet\dotnet.exe' }
if (!(Test-Path $dotnetPath)) { throw 'Install the .NET 8 SDK.' }

# Import the MSVC environment without assuming a system PATH change or a developer shell.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsPath) {
        & (Join-Path $vsPath 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
    }
}
if (!(Get-Command link.exe -ErrorAction SilentlyContinue)) { throw 'MSVC linker missing. Install Visual Studio Build Tools: Desktop development with C++, including Windows SDK.' }

& $cargoPath fmt --all -- --check
if ($LASTEXITCODE) { throw 'Rust formatting failed.' }
& $cargoPath test --locked
if ($LASTEXITCODE) { throw 'Rust tests failed.' }
& $cargoPath clippy --locked --all-targets -- -D warnings
if ($LASTEXITCODE) { throw 'Rust lint failed.' }
if ($TestOnly) { exit 0 }
& "$PSScriptRoot/build-icon.ps1"
& $dotnetPath restore src/PotPlayerNext/PotPlayerNext.csproj -p:Platform=x64 -p:RestoreLockedMode=true
if ($LASTEXITCODE) { throw 'WinUI restore failed.' }
& "$PSScriptRoot/collect-notices.ps1"
& $cargoPath build --release --locked
if ($LASTEXITCODE) { throw 'Native core build failed.' }
& $dotnetPath run --project tests/NativeSmoke/NativeSmoke.csproj -c Release
if ($LASTEXITCODE) { throw 'Rust / C# ABI integration tests failed.' }
& $dotnetPath run --project tests/CoreLogic/CoreLogic.csproj -c Release
if ($LASTEXITCODE) { throw 'Cache / decode-budget tests failed.' }
if ($Publish) {
    $publishPath = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    & $dotnetPath publish src/PotPlayerNext/PotPlayerNext.csproj -c Release -p:Platform=x64 "-p:Version=$Version" -o $publishPath
} else {
    & $dotnetPath build src/PotPlayerNext/PotPlayerNext.csproj -c Release -p:Platform=x64 "-p:Version=$Version"
}
if ($LASTEXITCODE) { throw 'WinUI build failed.' }
if ($Publish) {
    foreach ($resource in @('PotPlayerNext.pri', 'App.xbf', 'MainWindow.xbf', 'PreviewWindow.xbf', 'SplashWindow.xbf', 'Controls/BrandLogo.xbf', 'potplayer_next_core.dll')) {
        $file = Join-Path $publishPath $resource
        if (!(Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Publish missing mandatory resource: $resource" }
    }
}
