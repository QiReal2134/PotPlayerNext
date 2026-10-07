using System.Runtime.InteropServices;
using System.Text;

namespace PotPlayerNext.Services;

/// <summary>Opt-in Explorer bridge. No logging, no key suppression, no periodic polling.</summary>
public sealed class ExplorerSpacePreview : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int Space = 0x20;
    private readonly HookProcedure callback;
    private readonly Action<Action> dispatch;
    private readonly Action<string> preview;
    private IntPtr hook;
    private bool held;
    private bool disposed;

    public ExplorerSpacePreview(Action<Action> dispatch, Action<string> preview)
    {
        this.dispatch = dispatch; this.preview = preview;
        callback = OnKeyboard;
        hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == Space)
        {
            var keyMessage = message.ToInt64();
            if (keyMessage is 0x101 or 0x105) held = false;
            else if (keyMessage == 0x100 && !held)
            {
                held = true;
                var window = GetForegroundWindow();
                // Restrict to Explorer. Never capture modified shortcuts; editor check is deferred to dispatch.
                if (ClassName(window) is "CabinetWClass" or "ExploreWClass" && !HasModifier())
                    dispatch(() => ReadSelection(window));
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private void ReadSelection(IntPtr expectedWindow)
    {
        if (disposed || GetForegroundWindow() != expectedWindow || IsEditing(expectedWindow)) return;
        object? shell = null, windows = null, window = null, document = null, selection = null, item = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return;
            shell = Activator.CreateInstance(type);
            windows = ((dynamic)shell!).Windows();
            for (var index = 0; index < (int)((dynamic)windows).Count; index++)
            {
                window = ((dynamic)windows).Item(index);
                if (new IntPtr((long)((dynamic)window).HWND) != expectedWindow) { Release(window); window = null; continue; }
                document = ((dynamic)window).Document;
                if (document is null) { Release(window); window = null; continue; }
                selection = ((dynamic)document).SelectedItems();
                if (selection is null || (int)((dynamic)selection).Count != 1)
                {
                    Release(selection); selection = null;
                    Release(document); document = null;
                    Release(window); window = null;
                    continue; // Check other tabs in Windows 11 Explorer with the same HWND
                }
                item = ((dynamic)selection).Item(0);
                string path = ((dynamic)item).Path;
                if (File.Exists(path)) preview(path);
                return;
            }
        }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine($"Explorer preview: {error.Message}"); }
        finally { Release(item); Release(selection); Release(document); Release(window); Release(windows); Release(shell); }
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static bool HasModifier() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    private static string ClassName(IntPtr window) { var text = new StringBuilder(256); GetClassName(window, text, text.Capacity); return text.ToString(); }
    private static bool IsEditing(IntPtr window)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var thread = GetWindowThreadProcessId(window, out _);
        if (!GetGUIThreadInfo(thread, ref info)) return true;
        var name = ClassName(info.Focus);
        // Fail closed for known editor/XAML hosts; the Shell file pane is typically DirectUIHWND.
        return info.Caret != IntPtr.Zero || name.Contains("Edit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rich", StringComparison.OrdinalIgnoreCase) || name.Contains("Input", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() { disposed = true; if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
}
