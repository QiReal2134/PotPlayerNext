namespace PotPlayerNext.Services;

/// <summary>Tracks physical press/release pairs even when closing changes foreground HWND.</summary>
public sealed class PreviewKeyLatch
{
    private readonly HashSet<int> held = new();
    private readonly HashSet<int> suppressed = new();
    public bool BeginDown(int key) => held.Add(key);
    public bool IsSuppressed(int key) => suppressed.Contains(key);
    public void Suppress(int key) => suppressed.Add(key);
    public bool EndUp(int key)
    {
        held.Remove(key);
        return suppressed.Remove(key);
    }
}
