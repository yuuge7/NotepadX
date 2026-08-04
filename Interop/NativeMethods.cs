using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NotepadX.Interop;

internal static class NativeMethods
{
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_NCHITTEST = 0x0084;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
                                            int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>
    /// Keeps a maximized borderless window inside the monitor work area so it does not
    /// spill under the taskbar or lose ~8px of content to the invisible resize border.
    /// </summary>
    public static void ApplyMaxSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        mmi.ptMinTrackSize.X = 400;
        mmi.ptMinTrackSize.Y = 260;
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    /// <summary>Dark window frame. Works on Windows 10 1809+ and Windows 11.</summary>
    public static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetDarkTitleBar(hwnd, dark);
    }

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));

            RepaintCaption(hwnd);
        }
        catch (DllNotFoundException) { /* pre-Vista shells only; ignore */ }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Makes DWM redraw the caption with the colour that was just set. A window that is
    /// already on screen keeps the old caption until its frame is recomputed, and
    /// SWP_FRAMECHANGED on its own is not enough — only a real size change is, so the
    /// window grows by a pixel and immediately shrinks back. Skipped while the window is
    /// hidden (the first paint picks the colour up anyway) and while it is maximized,
    /// where resizing would fight the maximized bounds.
    /// </summary>
    private static void RepaintCaption(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsZoomed(hwnd)) return;
        if (!GetWindowRect(hwnd, out var r)) return;

        const uint flags = SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, w, h + 1, flags);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, w, h, flags);
    }

    /// <summary>Mica backdrop. Silently ignored on Windows 10, which has no backdrop API.</summary>
    public static void TryEnableMica(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        int backdrop = 2; // DWMSBT_MAINWINDOW == Mica
        try { DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)); }
        catch { /* not fatal, we just keep the solid background */ }
    }

    /// <summary>Rounded corners. No-op before Windows 11.</summary>
    public static void TryRoundCorners(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        int pref = 2; // DWMWCP_ROUND
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); }
        catch { }
    }

    public static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
}
