namespace PotPlayerNext.Services;

public enum ForwardKeyAction { None, Seek, RestoreRate }
/// <summary>One press is either a tap seek or a held speed change, never both.</summary>
public sealed class ForwardKeyGesture
{
    public bool Active { get; private set; }
    public bool Holding { get; private set; }
    private bool enabled;
    public bool Press(bool holdEnabled)
    {
        if (Active) return false;
        Active = true; Holding = false; enabled = holdEnabled;
        return true;
    }
    public bool HoldElapsed()
    {
        if (!Active || Holding || !enabled) return false;
        Holding = true;
        return true;
    }
    public ForwardKeyAction Release()
    {
        if (!Active) return ForwardKeyAction.None;
        var action = Holding ? ForwardKeyAction.RestoreRate : ForwardKeyAction.Seek;
        Active = Holding = false;
        return action;
    }
    public ForwardKeyAction Cancel()
    {
        var action = Holding ? ForwardKeyAction.RestoreRate : ForwardKeyAction.None;
        Active = Holding = false;
        return action;
    }
}
public static class PlaybackSeek
{
    public static TimeSpan Target(TimeSpan position, TimeSpan duration, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds)) return position;
        return TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds + deltaSeconds, 0, Math.Max(0, duration.TotalSeconds)));
    }
}
