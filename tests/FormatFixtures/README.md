# Format fixtures

Run from the repository root with the existing .NET SDK:

```powershell
& ./.tools/dotnet/dotnet.exe run --project tests/FormatFixtures/FormatFixtures.csproj -c Release
```

The independent console app writes uniquely named 320×180 files and
`artifacts/format-fixtures-030/manifest.json`. PNG, JPEG, GIF, TIFF, BMP, JPEG XR and
static SVG are required; required failures return exit code 1. It enumerates installed
Windows bitmap encoders, checks encoded container signatures, and forces actual pixel
decoding with `BitmapDecoder` for raster files. SVG is generated as local static vector
source; its WinUI rendering must be validated separately.

WebP is generated only if an installed encoder advertises it. WMV in a genuine ASF
container is attempted with `MediaComposition` and `CreateWmv`, with a 45-second
deadline; its ASF signature, dimensions and duration are verified. Optional formats
record `optionalUnsupported` with a concrete reason when unavailable. No renamed
containers, external assets, codec installs, WinUI initialization or preview launches
are used. Re-running preserves prior uniquely named fixtures and replaces only the
manifest index.
