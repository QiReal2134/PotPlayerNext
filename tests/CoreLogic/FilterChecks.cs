using PotPlayerNext.Services;

internal static class FilterChecks
{
    public static int Run()
    {
        MediaItem[] items = [new("a.jpg", "你好 A.JPG", "image", 7), new("b.mp4", "你好 B.mp4", "video", 11)];
        Require(ReferenceEquals(items, MediaFilter.Apply(items, null, "  ")));
        Require(MediaFilter.Apply(items, "video", "你好").Single() == items[1]);
        Require(MediaFilter.Apply(items, null, " a.jPg ").Single() == items[0]);
        Require(MediaFilter.Apply(items, "image", "b.mp4").Count == 0);
        Console.WriteLine("PASS: 4 filter checks, unfiltered catalog reuses original collection.");
        return 4;
    }
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Filter regression."); }
}
