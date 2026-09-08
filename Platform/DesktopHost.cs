using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TerminalWidget.Platform;
// Explorer의 비공개 창 계층은 Windows 버전에 따라 달라질 수 있으므로 성공 여부를 확인한다.
public sealed class DesktopHost
{
    private readonly Window window;
    private IntPtr Handle => new WindowInteropHelper(window).Handle;
    public bool Attached => GetParent(Handle) != IntPtr.Zero && IsWindow(GetParent(Handle));
    public DesktopHost(Window window) { this.window = window; }
    public void Reveal()
    {
        // A hidden launcher can pass SW_HIDE through STARTUPINFO. Override it only
        // after WPF has completed its initial Show, without stealing input focus.
        ShowWindow(Handle, 4);
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0185);
    }
    public bool Attach()
    {
        IntPtr shell = FindWindow("Progman", null);
        if (shell == IntPtr.Zero) return false;
        IntPtr host = IntPtr.Zero;
        EnumWindows((top, _) => { var view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null); if (view != IntPtr.Zero) { host = view; return false; } return true; }, IntPtr.Zero);
        if (host == IntPtr.Zero) return false;
        GetWindowRect(Handle, out var rect);
        long oldStyle = GetWindowLongPtr(Handle, -16).ToInt64();
        SetWindowLongPtr(Handle, -16, new IntPtr((oldStyle & ~0x80000000L) | 0x40000000L));
        SetParent(Handle, host);
        if (GetParent(Handle) != host) { SetWindowLongPtr(Handle, -16, new IntPtr(oldStyle)); return false; }
        var p = new PointNative { X = rect.Left, Y = rect.Top }; ScreenToClient(host, ref p);
        // Keep the widget above desktop siblings, without raising it above other apps.
        SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, 0x0011 | 0x0020 | 0x0040);
        return true;
    }
    public void Detach()
    {
        GetWindowRect(Handle, out var r); SetParent(Handle, IntPtr.Zero);
        long style = GetWindowLongPtr(Handle, -16).ToInt64();
        SetWindowLongPtr(Handle, -16, new IntPtr((style & ~0x40000000L) | 0x80000000L));
        SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, 0, 0, 0x0015 | 0x0020);
    }
    public void MoveBy(int dx, int dy)
    {
        GetWindowRect(Handle, out var r); var p = new PointNative { X = r.Left + dx, Y = r.Top + dy };
        var parent = GetParent(Handle); if (parent != IntPtr.Zero) ScreenToClient(parent, ref p);
        SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, 0x0015);
    }
    public (double X, double Y) Position() { GetWindowRect(Handle, out var r); var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window); return (r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY); }
    public void Clamp()
    {
        GetWindowRect(Handle, out var r);
        var bounds = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, Math.Max(1,r.Right-r.Left), Math.Max(1,r.Bottom-r.Top))).WorkingArea;
        int x = Math.Clamp(r.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right-(r.Right-r.Left)));
        int y = Math.Clamp(r.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom-(r.Bottom-r.Top)));
        MoveBy(x-r.Left, y-r.Top);
    }
    private delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h, ref PointNative point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
}
