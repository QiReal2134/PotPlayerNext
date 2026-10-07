using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

// Independent OS-encoder fixtures. Does not initialize WinUI, launch a preview, or install codecs.
const int width = 320, height = 180;
var output = Path.GetFullPath("artifacts/format-fixtures-030");
Directory.CreateDirectory(output);
var folder = await StorageFolder.GetFolderFromPathAsync(output);
var run = Guid.NewGuid().ToString("N");
var fixtures = new List<FixtureResult>();
var encoders = BitmapEncoder.GetEncoderInformationEnumerator().ToArray();
var decoders = BitmapDecoder.GetDecoderInformationEnumerator().ToArray();
using var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
softwareBitmap.CopyFromBuffer(CreatePixels(width, height).AsBuffer());
var required = new[]
{
    new ImagePlan("PNG", ".png", BitmapEncoder.PngEncoderId),
    new ImagePlan("JPEG", ".jpg", BitmapEncoder.JpegEncoderId),
    new ImagePlan("GIF", ".gif", BitmapEncoder.GifEncoderId),
    new ImagePlan("TIFF", ".tiff", BitmapEncoder.TiffEncoderId),
    new ImagePlan("BMP", ".bmp", BitmapEncoder.BmpEncoderId),
    new ImagePlan("JPEG XR", ".jxr", BitmapEncoder.JpegXREncoderId)
};

foreach (var plan in required) await GenerateImageAsync(plan, isRequired: true);
await GenerateSvgAsync();
var webp = encoders.FirstOrDefault(codec => codec.FileExtensions.Any(extension =>
    extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)) ||
    codec.MimeTypes.Any(mime => mime.Equals("image/webp", StringComparison.OrdinalIgnoreCase)));
if (webp is not null) await GenerateImageAsync(new ImagePlan("WebP", ".webp", webp.CodecId), isRequired: false);
else fixtures.Add(new("WebP", ".webp", false, "optionalUnsupported", null,
    Reason: "No installed BitmapEncoder advertises .webp or image/webp. A decoder alone cannot generate a WebP fixture."));

await GenerateAsfAsync();
var passed = fixtures.Where(result => result.Required).All(result => result.Status == "created");
var report = new
{
    schemaVersion = 1,
    scope = "Local fixture generation and Windows decoder validation only; WinUI rendering is tested separately.",
    timestampUtc = DateTimeOffset.UtcNow,
    run,
    output,
    passed,
    installedEncoders = encoders.Select(codec => new { codec.CodecId, codec.FriendlyName, codec.FileExtensions, codec.MimeTypes }),
    installedDecoders = decoders.Select(codec => new { codec.CodecId, codec.FriendlyName, codec.FileExtensions, codec.MimeTypes }),
    fixtures,
    limits = new { imageWidth = width, imageHeight = height, asfSeconds = 2, operationTimeoutSeconds = 45 },
    exclusions = new[] { "No renamed containers", "No external downloads", "No WinUI preview launches", "No codec or system configuration changes" }
};
var manifest = Path.Combine(output, "manifest.json");
await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}));
Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {fixtures.Count(result => result.Status == "created")} actual fixtures. Manifest: {manifest}");
return passed ? 0 : 1;

async Task GenerateImageAsync(ImagePlan plan, bool isRequired)
{
    StorageFile? file = null;
    var clock = Stopwatch.StartNew();
    try
    {
        var advertised = encoders.FirstOrDefault(codec => codec.CodecId == plan.EncoderId)
            ?? throw new NotSupportedException($"Installed BitmapEncoder list does not advertise {plan.Format} ({plan.EncoderId}).");
        file = await folder.CreateFileAsync($"fixture-{run}{plan.Extension}", CreationCollisionOption.FailIfExists);
        using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(advertised.CodecId, stream);
            encoder.IsThumbnailGenerated = false;
            encoder.SetSoftwareBitmap(softwareBitmap);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await encoder.FlushAsync().AsTask(deadline.Token);
        }
        RequireSignature(plan.Format, file.Path);
        using var input = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(input);
        if (decoder.PixelWidth != width || decoder.PixelHeight != height)
            throw new InvalidDataException($"Decoded size {decoder.PixelWidth}x{decoder.PixelHeight}, expected {width}x{height}.");
        // Header recognition alone is not enough: force actual pixel decoding of each encoded fixture.
        using var decoded = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        if (decoded.PixelWidth != width || decoded.PixelHeight != height)
            throw new InvalidDataException("SoftwareBitmap pixel dimensions do not match the fixture.");
        fixtures.Add(new(plan.Format, plan.Extension, isRequired, "created", file.Path,
            Bytes: new FileInfo(file.Path).Length, Width: (uint)decoded.PixelWidth, Height: (uint)decoded.PixelHeight,
            Encoder: advertised.FriendlyName, EncoderId: advertised.CodecId, Decoder: decoder.DecoderInformation.FriendlyName,
            SignatureVerified: true, Sha256: Hash(file.Path), Milliseconds: clock.ElapsedMilliseconds));
        Console.WriteLine($"CREATED: {plan.Format} {file.Path}");
    }
    catch (Exception error)
    {
        fixtures.Add(new(plan.Format, plan.Extension, isRequired, isRequired ? "failed" : "optionalUnsupported", file?.Path,
            Reason: error.Message, HResult: $"0x{error.HResult:X8}", Milliseconds: clock.ElapsedMilliseconds));
        Console.WriteLine($"{(isRequired ? "FAILED" : "OPTIONAL UNSUPPORTED")}: {plan.Format}: {error.Message}");
    }
}

async Task GenerateSvgAsync()
{
    string? path = null;
    try
    {
        path = Path.Combine(output, $"fixture-{run}.svg");
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="320" height="180" viewBox="0 0 320 180">
              <defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#10364f"/><stop offset="1" stop-color="#41558b"/></linearGradient></defs>
              <rect width="320" height="180" rx="12" fill="url(#bg)"/>
              <rect x="20" y="20" width="125" height="140" rx="8" fill="#1fbbc0"/>
              <circle cx="82" cy="72" r="30" fill="#f8cd66"/>
              <path d="M28 148 L62 112 L86 135 L113 100 L138 148 Z" fill="#10364f"/>
              <path d="M174 135 L199 92 L224 117 L249 52 L289 85" fill="none" stroke="#f8cd66" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>
              <rect x="174" y="148" width="116" height="8" rx="4" fill="#1fbbc0"/>
            </svg>
            """;
        await File.WriteAllTextAsync(path, svg, new UTF8Encoding(false));
        fixtures.Add(new("SVG", ".svg", true, "created", path, Bytes: new FileInfo(path).Length,
            Width: width, Height: height, Encoder: "Local secure-static SVG source", Sha256: Hash(path)));
        Console.WriteLine($"CREATED: SVG {path}");
    }
    catch (Exception error)
    {
        fixtures.Add(new("SVG", ".svg", true, "failed", path, Reason: error.Message, HResult: $"0x{error.HResult:X8}"));
    }
}

async Task GenerateAsfAsync()
{
    StorageFile? file = null;
    var clock = Stopwatch.StartNew();
    var composition = new MediaComposition();
    try
    {
        var png = fixtures.FirstOrDefault(result => result.Format == "PNG" && result.Status == "created")
            ?? throw new InvalidOperationException("No successfully encoded PNG is available for the WMV composition.");
        var image = await StorageFile.GetFileFromPathAsync(png.Path!);
        composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(image, TimeSpan.FromSeconds(2)));
        file = await folder.CreateFileAsync($"fixture-{run}.asf", CreationCollisionOption.FailIfExists);
        var profile = MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Vga);
        profile.Video.Width = width;
        profile.Video.Height = height;
        profile.Video.Bitrate = 400_000;
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var rendered = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile).AsTask(deadline.Token);
        if (rendered != TranscodeFailureReason.None) throw new NotSupportedException($"Windows WMV/ASF rendering result: {rendered}.");
        RequireSignature("ASF", file.Path);
        var properties = await file.Properties.GetVideoPropertiesAsync();
        if (properties.Width != width || properties.Height != height || properties.Duration.TotalSeconds < 1.5)
            throw new InvalidDataException($"ASF video metadata is invalid: {properties.Width}x{properties.Height}, {properties.Duration.TotalSeconds:F3}s.");
        fixtures.Add(new("ASF/WMV", ".asf", false, "created", file.Path,
            Bytes: new FileInfo(file.Path).Length, Width: properties.Width, Height: properties.Height,
            Encoder: $"MediaComposition / CreateWmv ({profile.Container.Subtype}, {profile.Video.Subtype})",
            SignatureVerified: true, Sha256: Hash(file.Path), Milliseconds: clock.ElapsedMilliseconds,
            DurationSeconds: properties.Duration.TotalSeconds));
        Console.WriteLine($"CREATED: ASF/WMV {file.Path}");
    }
    catch (Exception error)
    {
        fixtures.Add(new("ASF/WMV", ".asf", false, "optionalUnsupported", file?.Path,
            Reason: error.Message, HResult: $"0x{error.HResult:X8}", Milliseconds: clock.ElapsedMilliseconds));
        Console.WriteLine($"OPTIONAL UNSUPPORTED: ASF/WMV: {error.Message}");
    }
    finally { composition.Clips.Clear(); }
}

static byte[] CreatePixels(int width, int height)
{
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var offset = (y * width + x) * 4;
        var panel = x > width / 2 && y > height / 4 && y < height * 3 / 4;
        pixels[offset] = panel ? (byte)50 : (byte)(40 + x * 180 / width);
        pixels[offset + 1] = panel ? (byte)200 : (byte)(40 + y * 180 / height);
        pixels[offset + 2] = panel ? (byte)240 : (byte)40;
        pixels[offset + 3] = 255;
    }
    return pixels;
}

static void RequireSignature(string format, string path)
{
    using var input = File.OpenRead(path);
    Span<byte> header = stackalloc byte[16];
    var read = input.Read(header);
    var valid = format switch
    {
        "PNG" => read >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "JPEG" => read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
        "GIF" => read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
        "TIFF" => read >= 4 && (header[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0x2a, 0 }) || header[..4].SequenceEqual(new byte[] { 0x4d, 0x4d, 0, 0x2a })),
        "BMP" => read >= 2 && header[..2].SequenceEqual("BM"u8),
        "JPEG XR" => read >= 4 && header[..4].SequenceEqual(new byte[] { 0x49, 0x49, 0xbc, 1 }),
        "WebP" => read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
        "ASF" => read == 16 && header.SequenceEqual(new Guid("75B22630-668E-11CF-A6D9-00AA0062CE6C").ToByteArray()),
        _ => false
    };
    if (!valid) throw new InvalidDataException($"{format} fixture has the wrong container signature.");
}

static string Hash(string path)
{
    using var input = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(input));
}

sealed record ImagePlan(string Format, string Extension, Guid EncoderId);
sealed record FixtureResult(string Format, string Extension, bool Required, string Status, string? Path,
    long? Bytes = null, uint? Width = null, uint? Height = null, string? Encoder = null, Guid? EncoderId = null,
    string? Decoder = null, bool SignatureVerified = false, string? Sha256 = null, string? Reason = null,
    string? HResult = null, long? Milliseconds = null, double? DurationSeconds = null);
