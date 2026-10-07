namespace PotPlayerNext.Services;

public sealed record LaunchRequest(string? Path = null, string? Error = null, bool Preview = false, bool Background = false, bool StopBackground = false)
{
    // Windows has already split/decoded quoted command-line arguments. Never split a path again.
    public static LaunchRequest Parse(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        if (args.Length == 0) return new();
        if (args.Length == 1 && args[0] == "--background") return new(Background: true);
        if (args.Length == 1 && args[0] == "--stop-background") return new(StopBackground: true);
        var preview = args[0] == "--preview";
        if (preview) args = args.Skip(1).ToArray();
        if (args.Length == 0) return new(Error: "请指定预览文件。");
        if (args[0] == "--") args = args.Skip(1).ToArray();
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0])) return new(Error: "请指定一个图片、视频文件或文件夹路径。");
        try { return new(System.IO.Path.GetFullPath(args[0]), Preview: preview); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return new(Error: $"文件路径无效：{error.Message}"); }
    }
}
