using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace StickyPad.Utils;

/// Clamps a saved note rectangle back onto a visible monitor in case the user disconnected the display
/// it was last on. Falls back to the primary monitor when nothing else fits.
public static class MonitorHelper
{
    private const int MonitorDefaultToNearest = 0x00000002;
    private const int MinVisible = 80;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public static Rect EnsureOnScreen(double x, double y, double width, double height)
    {
        var rect = new RECT
        {
            left = (int)x,
            top = (int)y,
            right = (int)(x + width),
            bottom = (int)(y + height),
        };

        var hMonitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            return new Rect(0, 0, Math.Max(width, MinVisible), Math.Max(height, MinVisible));
        }

        var work = info.rcWork;
        var newWidth = Math.Min(width, work.right - work.left);
        var newHeight = Math.Min(height, work.bottom - work.top);

        var newX = Math.Max(work.left, Math.Min(x, work.right - MinVisible));
        var newY = Math.Max(work.top, Math.Min(y, work.bottom - MinVisible));

        if (newX + newWidth > work.right) newX = work.right - newWidth;
        if (newY + newHeight > work.bottom) newY = work.bottom - newHeight;

        return new Rect(newX, newY, newWidth, newHeight);
    }
}
