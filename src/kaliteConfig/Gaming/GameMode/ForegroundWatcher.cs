using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using kaliteConfig.Native;

namespace kaliteConfig.Gaming.GameMode;

public sealed class ForegroundWatcher : IDisposable
{
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private IntPtr _window;
    private volatile bool _running;
    private NativeMethods.GameMode.WinEventProc? _callback;
    private IntPtr _lastForeground;
    private long _lastPollTimestamp;

    public event Action<IntPtr>? ForegroundChanged;
    public event Action<string>? Diagnostic;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "GameModeForeground" };
        _thread.SetApartmentState(ApartmentState.STA);
        // kaliteConfig runs itself at Idle priority. A normal-priority watcher
        // can be starved for long stretches while a game saturates the CPU,
        // which is what made Gaming Mode feel like it needed the app window
        // open. Highest buys this thread as much time as the process class
        // allows, and the polling loop below removes the dependence on a
        // promptly delivered WinEvent.
        try { _thread.Priority = ThreadPriority.Highest; } catch { }
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_threadId != 0)
            NativeMethods.GameMode.PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        try { _thread?.Join(2000); } catch { }
        _thread = null;
    }

    private void Loop()
    {
        _threadId = NativeMethods.GameMode.GetCurrentThreadId();
        _window = NativeMethods.GameMode.CreateWindowEx(0, "STATIC", "kaliteConfigGameMode", 0,
            0, 0, 0, 0, NativeMethods.GameMode.HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.GameMode.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        _callback = OnWinEvent;
        _hook = NativeMethods.GameMode.SetWinEventHook(
            NativeMethods.GameMode.EventSystemForeground,
            NativeMethods.GameMode.EventSystemForeground,
            IntPtr.Zero, _callback, 0, 0,
            NativeMethods.GameMode.WineventOutofcontext);

        try
        {
            Diagnostic?.Invoke(_hook == IntPtr.Zero
                ? "Foreground hook was not installed"
                : "Foreground hook installed (window " + _window + ")");
        }
        catch { }

        while (_running)
        {
            bool quit = false;
            while (NativeMethods.GameMode.PeekMessage(out var message, IntPtr.Zero, 0, 0, 1))
            {
                if (message.Message == 0x0012) // WM_QUIT
                {
                    quit = true;
                    break;
                }
                NativeMethods.GameMode.TranslateMessage(ref message);
                NativeMethods.GameMode.DispatchMessage(ref message);
            }
            if (quit) break;

            PollForeground();

            // Wake as soon as a hook event arrives, otherwise check again in a
            // second. This keeps working when WinEvent delivery is delayed or
            // dropped, including while the app is hidden in the tray.
            NativeMethods.GameMode.MsgWaitForMultipleObjectsEx(
                0, IntPtr.Zero, 1000, NativeMethods.GameMode.QsAllInput, NativeMethods.GameMode.MwmoInputAvailable);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.GameMode.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        if (_window != IntPtr.Zero)
        {
            NativeMethods.GameMode.DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        try { Diagnostic?.Invoke("Foreground message loop exited"); } catch { }
        _threadId = 0;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { Diagnostic?.Invoke("event " + eventType + " hwnd=" + hwnd); } catch { }
        if (eventType != NativeMethods.GameMode.EventSystemForeground || hwnd == IntPtr.Zero) return;
        _lastForeground = hwnd;
        _lastPollTimestamp = Environment.TickCount64;
        try { ForegroundChanged?.Invoke(hwnd); } catch { }
    }

    private void PollForeground()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GameMode.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            long now = Environment.TickCount64;
            bool changed = hwnd != _lastForeground;
            // Re-evaluate every few seconds even when the window is unchanged:
            // this catches a game that was already focused when the app was
            // hidden or when Gaming Mode was switched on from the tray.
            if (!changed && now - _lastPollTimestamp < 5000) return;

            _lastForeground = hwnd;
            _lastPollTimestamp = now;
            try { ForegroundChanged?.Invoke(hwnd); } catch { }
        }
        catch { }
    }

    public static bool TryResolveForeground(IntPtr hwnd, out int pid, out string processName, out string? imagePath)
    {
        pid = 0;
        processName = string.Empty;
        imagePath = null;
        try
        {
            if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GameMode.GetShellWindow()) return false;

            var builder = new StringBuilder(256);
            NativeMethods.GameMode.GetClassName(hwnd, builder, builder.Capacity);
            string windowClass = builder.ToString();
            foreach (var ignored in BackgroundCatalog.IgnoredForegroundClasses)
                if (windowClass.Equals(ignored, StringComparison.OrdinalIgnoreCase)) return false;

            NativeMethods.GameMode.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == 0) return false;

            builder.Clear();
            NativeMethods.GameMode.GetWindowText(hwnd, builder, builder.Capacity);
            string title = builder.ToString();
            if (string.IsNullOrWhiteSpace(title)) return false;

            IntPtr target = hwnd;
            if (windowClass.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr childHolder = IntPtr.Zero;
                NativeMethods.GameMode.EnumChildWindows(hwnd, (child, _) =>
                {
                    NativeMethods.GameMode.GetWindowThreadProcessId(child, out uint childPid);
                    if (childPid != 0 && childPid != owner)
                    {
                        childHolder = child;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (childHolder != IntPtr.Zero) target = childHolder;
            }

            NativeMethods.GameMode.GetWindowThreadProcessId(target, out uint resolved);
            if (resolved == 0) return false;
            pid = (int)resolved;

            using var process = System.Diagnostics.Process.GetProcessById(pid);
            processName = process.ProcessName + ".exe";
            using var handle = NativeMethods.Handles.OpenProcess(
                NativeMethods.ProcessAccess.QueryLimitedInformation, false, resolved);
            if (handle.IsInvalid) return true;
            var path = new StringBuilder(1024);
            int size = path.Capacity;
            if (NativeMethods.GameMode.QueryFullProcessImageName(handle.DangerousGetHandle(), 0, path, ref size))
                imagePath = path.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}
