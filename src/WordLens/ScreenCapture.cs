using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WordLens;
public sealed record Capture(string Image, int X, int Y, int Left, int Top, Native.Point Cursor);
public static class ScreenCapture
{
    public static Capture AroundCursor()
    {
        Native.GetCursorPos(out var cursor);
        return AroundPoint(cursor);
    }
    public static Capture AroundPoint(Native.Point cursor)
    {
        var monitor = Native.MonitorFromPoint(cursor, 2);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) throw new IOException("无法读取显示器位置。");
        double scale = 1;
        if (Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0) scale = dpi / 96.0;
        int width = Math.Min((int)(720 * scale), info.Monitor.Right-info.Monitor.Left);
        int height = Math.Min((int)(180 * scale), info.Monitor.Bottom-info.Monitor.Top);
        width = Math.Min(width, 2400); height = Math.Min(height, 1600);
        int left = Math.Clamp(cursor.X-width/2, info.Monitor.Left, info.Monitor.Right-width);
        int top = Math.Clamp(cursor.Y-height/2, info.Monitor.Top, info.Monitor.Bottom-height);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(left, top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        return new Capture(Convert.ToBase64String(stream.ToArray()), cursor.X-left, cursor.Y-top, left, top, cursor);
    }
    public static void Place(Window window, Native.Point cursor)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var monitor = Native.MonitorFromPoint(cursor, 2);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.GetMonitorInfo(monitor, ref info);
        Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _);
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        int w = (int)Math.Ceiling(window.Width*scale), h = (int)Math.Ceiling(window.Height*scale);
        int x = Math.Clamp(cursor.X+20, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right-w));
        int y = cursor.Y+28;
        if (y+h > info.Work.Bottom) y = cursor.Y-h-16;
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom-h));
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, w, h, 0x0010);
    }
}
public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; public Point(int x,int y) { X=x; Y=y; } }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
