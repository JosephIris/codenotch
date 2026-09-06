using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Codenotch;

internal static class NativeWindow
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    public static void NonActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
    }
    public static Forms.Screen Screen(Settings settings) => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Screen) ?? Forms.Screen.PrimaryScreen!;
    public static double Scale(Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        GetDpiForMonitor(MonitorFromPoint(new NativePoint { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, 2), 0, out var dpi, out _);
        return dpi == 0 ? 1 : dpi / 96d;
    }
    public static void Place(Window window, double x, double y, double scale)
    {
        SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), (int)Math.Round(x), (int)Math.Round(y), (int)Math.Ceiling(window.Width * scale), (int)Math.Ceiling(window.Height * scale), 0x0010);
    }
}
