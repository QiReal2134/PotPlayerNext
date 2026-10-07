namespace PotPlayerNext.Services;

/// <summary>UI-thread state for one Explorer preview, including a pending probe.</summary>
public sealed class PreviewSessionState
{
    private long version;
    private IntPtr owner, window;
    private bool opening;
    public long BeginOpen(IntPtr sourceWindow) { owner = sourceWindow; opening = true; return ++version; }
    public bool IsCurrent(long request) => request == version;
    public void ForgetWindow() => window = IntPtr.Zero;
    public void Opened(long request, IntPtr previewWindow)
    {
        if (!IsCurrent(request)) return;
        window = previewWindow; opening = false;
    }
    public void EndOpen(long request)
    {
        if (!IsCurrent(request)) return;
        opening = false;
        if (window == IntPtr.Zero) owner = IntPtr.Zero;
    }
    public long? CaptureDismiss(IntPtr foreground) => foreground != IntPtr.Zero &&
        (opening || window != IntPtr.Zero) && (foreground == owner || foreground == window) ? version : null;
    public bool Dismiss(long request)
    {
        if (!IsCurrent(request)) return false;
        Reset(); return true;
    }
    public void Reset() { ++version; owner = window = IntPtr.Zero; opening = false; }
}
