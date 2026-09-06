using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Codenotch;

internal static class NativeWindow
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    public static void NonActivating(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80);
    }
    public static Forms.Screen Screen(Settings settings) => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Screen) ?? Forms.Screen.PrimaryScreen!;
    public static double Prepare(Window window, Forms.Screen screen)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var area = screen.WorkingArea;
        var monitor = MonitorFromPoint(new NativePoint { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, 2);
        if (MonitorFromWindow(hwnd, 2) != monitor)
        {
            // Moving first lets WPF process WM_DPICHANGED. Never size a new monitor's
            // window using the scale of the monitor where its HWND was created.
            SetWindowPos(hwnd, IntPtr.Zero, area.Left + area.Width / 2, area.Top + area.Height / 2, 0, 0, 0x0015);
        }
        return VisualTreeHelper.GetDpi(window).DpiScaleX;
    }
    public static void Place(Window window, double x, double y, double scale, Size logicalSize)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        window.Width = logicalSize.Width; window.Height = logicalSize.Height;
        var left = (int)Math.Round(x); var top = (int)Math.Round(y);
        var width = (int)Math.Ceiling(logicalSize.Width * scale); var height = (int)Math.Ceiling(logicalSize.Height * scale);
        if (GetWindowRect(hwnd, out var rect) && rect.Left == left && rect.Top == top && rect.Right - rect.Left == width && rect.Bottom - rect.Top == height) return;
        // Preserve the order of topmost windows; periodic placement must not raise
        // the notch above its own tooltip or the user's other floating windows.
        SetWindowPos(hwnd, IntPtr.Zero, left, top, width, height, 0x0014);
    }
}
