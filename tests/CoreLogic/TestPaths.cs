internal static class TestPaths
{
    // Separate runs must never reset another run's settings fixtures mid-update.
    public static string Root { get; } = CreateRoot();
    private static string CreateRoot()
    {
        var path = Path.GetFullPath(Path.Combine("artifacts/core-logic", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(path);
        return path;
    }
}
