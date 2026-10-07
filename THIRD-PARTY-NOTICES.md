# PotPlayerNext notices

Original application source is licensed under GPL-3.0-only; the full text is included in LICENSE.txt.
The accompanying source archive contains the application, native core, installer authoring and build scripts.
PotPlayer and ImageGlass binaries or source are not included.

Runtime dependencies retain their own licenses:

Full package license/notice documents are copied from the restored NuGet packages and locked Rust dependencies into the accompanying `licenses` directory by `scripts/collect-notices.ps1`.

- Microsoft .NET 8: MIT, https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
  and third-party notices: https://github.com/dotnet/runtime/blob/release/8.0/THIRD-PARTY-NOTICES.TXT
- Microsoft Windows App SDK / WinUI: https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE
  and https://github.com/microsoft/microsoft-ui-xaml/blob/main/LICENSE
  NuGet package license/redist terms also apply to their distributed Windows runtime files.
- Rust serde / serde_json: MIT OR Apache-2.0,
  https://github.com/serde-rs/serde and https://github.com/serde-rs/json

Windows system codecs and APIs are supplied by the operating system, not bundled codecs from PotPlayer.
WiX is a build-time installer authoring dependency; it is not a runtime dependency of the player.
