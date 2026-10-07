using System.Diagnostics;
using System.Text.Json;
using PotPlayerNext.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;

// Independently tests the OS APIs used by the app, NOT the WinUI view or Rust ABI.
var output = Path.GetFullPath("artifacts/media-smoke");
Directory.CreateDirectory(output);
var evidence = new List<object>();
var run = Guid.NewGuid().ToString("N");
var fixtureSeconds = args.Contains("--long-fixture") ? 60 : 3;
var bmpPath = Path.Combine(output, $"你好 image-{run}.bmp");
WriteBitmap(bmpPath, 320, 180);
try
{
    var image = await StorageFile.GetFileFromPathAsync(bmpPath);
    using (var stream = await image.OpenReadAsync())
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        Require(decoder.PixelWidth == 320 && decoder.PixelHeight == 180, "BMP decoder dimensions");
        var pixels = await decoder.GetPixelDataAsync();
        Require(pixels.DetachPixelData().Length >= 320 * 180 * 3, "BMP decoded pixel data");
        evidence.Add(new { test = "Windows image decoder", passed = true, width = 320, height = 180 });
    }
    using (var thumbnail = await image.GetThumbnailAsync(ThumbnailMode.PicturesView, 192, ThumbnailOptions.ResizeThumbnail))
    {
        Require(thumbnail is not null && thumbnail.Size > 0, "Windows shell image thumbnail");
        evidence.Add(new { test = "Windows shell image thumbnail", passed = true, bytes = thumbnail!.Size });
    }

    // Produce a local H.264 MP4 fixture without downloads or a bundled encoder.
    var composition = new MediaComposition();
    composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(image, TimeSpan.FromSeconds(fixtureSeconds)));
    var directory = await StorageFolder.GetFolderFromPathAsync(output);
    var video = await directory.CreateFileAsync($"你好 video-{run}.mp4", CreationCollisionOption.FailIfExists);
    var watch = Stopwatch.StartNew();
    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
    {
        var rendered = await composition.RenderToFileAsync(video, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga)).AsTask(timeout.Token);
        Require(rendered == TranscodeFailureReason.None, $"MP4 fixture encoding: {rendered}");
    }
    composition.Clips.Clear();
    evidence.Add(new { test = "Local H.264 fixture", passed = true, milliseconds = watch.ElapsedMilliseconds, file = video.Name });

    using (var thumbnail = await video.GetThumbnailAsync(ThumbnailMode.VideosView, 192, ThumbnailOptions.ResizeThumbnail))
    {
        Require(thumbnail is not null && thumbnail.Size > 0, "Windows shell video thumbnail");
        evidence.Add(new { test = "Windows shell video thumbnail", passed = true, bytes = thumbnail!.Size });
    }

    using var source = MediaSource.CreateFromStorageFile(video);
    using var player = new MediaPlayer { AutoPlay = false, Volume = 0 };
    var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    player.MediaOpened += (_, _) => opened.TrySetResult();
    player.MediaFailed += (_, error) => opened.TrySetException(new InvalidOperationException(error.ErrorMessage));
    player.Source = source;
    await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
    var session = player.PlaybackSession;
    Require(session.NaturalDuration.TotalSeconds >= 2.5, "MP4 duration");
    Require(session.NaturalVideoWidth > 0 && session.NaturalVideoHeight > 0, "MP4 dimensions");
    player.Play();
    await WaitForAsync(() => session.PlaybackState == MediaPlaybackState.Playing, TimeSpan.FromSeconds(5));
    session.PlaybackRate = 2;
    Require(Math.Abs(session.PlaybackRate - 2) < 0.001, "2x playback rate accepted by native media engine");
    await Task.Delay(250);
    Require(session.Position > TimeSpan.Zero, "Video playback clock advances");
    player.Pause();
    await WaitForAsync(() => session.PlaybackState == MediaPlaybackState.Paused, TimeSpan.FromSeconds(5));
    session.PlaybackRate = 1;
    Require(Math.Abs(session.PlaybackRate - 1) < 0.001, "Original playback rate restored");
    session.Position = PlaybackSeek.Target(TimeSpan.FromSeconds(0.5), session.NaturalDuration, 1.5);
    await WaitForAsync(() => Math.Abs(session.Position.TotalSeconds - 2) < 0.25, TimeSpan.FromSeconds(5));
    session.Position = PlaybackSeek.Target(TimeSpan.FromSeconds(2), session.NaturalDuration, -1.5);
    await WaitForAsync(() => Math.Abs(session.Position.TotalSeconds - 0.5) < 0.25, TimeSpan.FromSeconds(5));
    player.Volume = 0.25;
    Require(Math.Abs(player.Volume - 0.25) < 0.001, "Volume property");
    evidence.Add(new { test = "MediaPlayer open/play/pause/1.5s forward+back/2x+restore/volume", passed = true, width = session.NaturalVideoWidth, height = session.NaturalVideoHeight, durationSeconds = session.NaturalDuration.TotalSeconds });
    player.Source = null;

    await WriteReportAsync(true);
    Console.WriteLine("PASS: OS image/video smoke tests. WinUI rendering and Rust ABI remain unverified.");
    return 0;
}
catch (Exception error)
{
    evidence.Add(new { test = "Failure", passed = false, error = error.ToString() });
    await WriteReportAsync(false);
    Console.Error.WriteLine(error);
    return 1;
}

async Task WriteReportAsync(bool passed)
{
    var report = new { scope = "Windows decoder, shell thumbnail, and MediaPlayer APIs only", passed, timestampUtc = DateTimeOffset.UtcNow, evidence };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(output, "results.json"), json);
    await File.WriteAllTextAsync(Path.Combine(output, $"results-{run}.json"), json);
    Console.WriteLine(json);
}

static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException($"FAILED: {name}"); }
static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
{
    var watch = Stopwatch.StartNew();
    while (!predicate())
    {
        if (watch.Elapsed > timeout) throw new TimeoutException("Media state did not change before the deadline.");
        await Task.Delay(50);
    }
}

static void WriteBitmap(string path, int width, int height)
{
    var stride = (width * 3 + 3) & ~3;
    using var writer = new BinaryWriter(File.Create(path));
    writer.Write((ushort)0x4D42); writer.Write(54 + stride * height);
    writer.Write(0); writer.Write(54); writer.Write(40);
    writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)24);
    writer.Write(0); writer.Write(stride * height); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
    for (var y = 0; y < height; ++y)
    {
        for (var x = 0; x < width; ++x) { writer.Write((byte)180); writer.Write((byte)(y * 255 / height)); writer.Write((byte)(x * 255 / width)); }
        for (var padding = width * 3; padding < stride; ++padding) writer.Write((byte)0);
    }
}
