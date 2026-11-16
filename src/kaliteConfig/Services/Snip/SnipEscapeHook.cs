using System;
using System.Runtime.InteropServices;

namespace kaliteConfig.Services.Snip;

internal static class SnipEscapeHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _hook;
    private static HookProc? _proc;
    private static Action? _onEscape;
    private static int _owners;

    public static void Install(Action onEscape)
    {
        _onEscape = onEscape;
        _owners++;
        if (_hook != IntPtr.Zero) return;
        try
        {
            _proc = Callback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }
        catch
        {
            _hook = IntPtr.Zero;
        }
    }

    public static void Uninstall()
    {
        _owners--;
        if (_owners > 0) return;
        _owners = 0;
        _onEscape = null;
        if (_hook == IntPtr.Zero) return;
        try { UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN) && Marshal.ReadInt32(lParam) == VK_ESCAPE)
            {
                _onEscape?.Invoke();
            }
        }
        catch { }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
