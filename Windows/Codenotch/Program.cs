using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Codenotch;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\Codenotch.Windows", out var first);
        if (!first) return;
        var demo = args.Contains("--demo") || Environment.GetEnvironmentVariable("CODENOTCH_DEMO") == "1";
        var smoke = args.Contains("--smoke-test");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new Notch(demo || smoke, smoke));
    }
}

internal sealed class Notch : Window
{
    private readonly Settings settings;
    private readonly Archive archive;
    private readonly Providers? providers;
    private readonly List<Provider> descriptors;
    private readonly Dictionary<string, Reading> readings = [];
    private readonly Forms.NotifyIcon tray;
    private readonly StackPanel stack = new();
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer placement = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer collapse = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly bool demo;
    private bool expanded;
    private bool refreshing;

    public Notch(bool demo, bool smoke)
    {
        this.demo = demo;
        settings = demo ? new Settings { AlwaysShow = true } : State.Load<Settings>("settings.json");
        archive = demo ? new Archive() : State.Load<Archive>("usage.json");
        if (!new[] { "Left", "Right", "Top", "Bottom" }.Contains(settings.Edge)) settings.Edge = "Right";
        settings.Disabled ??= [];
        archive.Readings ??= [];
        archive.RetryAfter ??= [];
        archive.Failures ??= [];
        if (demo)
        {
            descriptors = [new("claude", "Claude", "#D99B7C", _ => throw new NotSupportedException()), new("codex", "Codex", "#84DCC6", _ => throw new NotSupportedException()), new("cursor", "Cursor", "#C1BEF5", _ => throw new NotSupportedException()), new("glm", "GLM", "#80B5FF", _ => throw new NotSupportedException())];
            for (var i = 0; i < descriptors.Count; i++)
            {
                var p = descriptors[i];
                readings[p.Id] = new(p.Id, p.Name, "Demo · sample data", "Demo", DateTimeOffset.UtcNow, [new("session", "Current session", 23 + i * 19, DateTimeOffset.UtcNow.AddHours(2)), new("weekly", "Weekly", 14 + i * 12, DateTimeOffset.UtcNow.AddDays(3))]);
            }
        }
        else
        {
            providers = new Providers();
            descriptors = providers.All;
            foreach (var p in descriptors.Where(p => !settings.Disabled.Contains(p.Id)))
                readings[p.Id] = archive.Readings.TryGetValue(p.Id, out var saved) ? saved with { Status = "Stale · checking for updates" } : new(p.Id, p.Name, "Checking usage…", "", null, []);
        }
        Title = demo ? "Codenotch · Demo" : "Codenotch";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        Content = new Border { Background = Brush("#111316"), BorderBrush = Brush("#303338"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Child = stack, Padding = new Thickness(5) };
        MouseEnter += (_, _) => { collapse.Stop(); expanded = true; Render(); };
        MouseLeave += (_, _) => collapse.Start();
        collapse.Tick += (_, _) => { collapse.Stop(); if (!IsMouseOver && ContextMenu?.IsOpen != true) { expanded = false; Render(); } };
        ContextMenu = BuildMenu();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = Title, Visible = true };
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Show notch / settings", null, (_, _) => Dispatcher.Invoke(OpenMenu));
        tray.ContextMenuStrip.Items.Add("Refresh now", null, async (_, _) => await Refresh());
        tray.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Close));
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenMenu);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, style | 0x08000000 | 0x00000080); // NOACTIVATE and TOOLWINDOW.
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
        Loaded += async (_, _) =>
        {
            Render();
            await Refresh();
            if (smoke)
            {
                var output = System.IO.Path.Combine(Environment.CurrentDirectory, "Windows", "artifacts", "smoke");
                System.IO.Directory.CreateDirectory(output);
                foreach (var edge in new[] { "Left", "Right", "Top", "Bottom" })
                {
                    settings.Edge = edge; Render(); UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(this);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(System.IO.Path.Combine(output, edge + ".png"));
                    encoder.Save(file);
                }
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => { timer.Stop(); Close(); };
                timer.Start();
            }
        };
        poll.Tick += async (_, _) => await Refresh();
        placement.Tick += (_, _) => Position();
        poll.Start(); placement.Start();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) =>
        {
            lifetime.Cancel(); poll.Stop(); placement.Stop(); collapse.Stop();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            tray.Visible = false; tray.Dispose(); providers?.Dispose();
        };
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { ContextMenu = BuildMenu(); Position(); });
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is 0x02E0 or 0x001A) Dispatcher.BeginInvoke(Position); // DPI and working-area changes.
        return IntPtr.Zero;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }

    private void Position()
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Screen) ?? Forms.Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        var monitor = MonitorFromPoint(new NativePoint { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, 2);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        var scale = dpi > 0 ? dpi / 96.0 : 1;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var x = settings.Edge switch { "Left" => area.Left, "Right" => area.Right - width, _ => area.Left + (area.Width - width) / 2 };
        var y = settings.Edge switch { "Top" => area.Top, "Bottom" => area.Bottom - height, _ => area.Top + (area.Height - height) / 2 };
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), x, y, width, height, 0x0010);
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void Render()
    {
        stack.Children.Clear();
        var vertical = settings.Edge is "Left" or "Right";
        stack.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        if (!expanded && !settings.AlwaysShow)
        {
            stack.Children.Add(new Border { Width = vertical ? 4 : 34, Height = vertical ? 34 : 4, Background = Brush("#84DCC6"), CornerRadius = new CornerRadius(2), Margin = new Thickness(3) });
        }
        else
        {
            foreach (var provider in descriptors.Where(p => !settings.Disabled.Contains(p.Id)))
            {
                readings.TryGetValue(provider.Id, out var reading);
                var ring = new UsageRing(reading?.Windows.FirstOrDefault()?.Percent, Brush(provider.Color), reading?.Status.StartsWith("Stale") == true) { ToolTip = Details(reading, provider.Name), Margin = new Thickness(4) };
                AutomationProperties.SetName(ring, $"{provider.Name}: {reading?.Windows.FirstOrDefault()?.Percent.ToString("0") ?? "unknown"} percent used. {reading?.Status}");
                ToolTipService.SetInitialShowDelay(ring, 100);
                ToolTipService.SetShowDuration(ring, 60000);
                ToolTipService.SetPlacement(ring, settings.Edge switch { "Left" => System.Windows.Controls.Primitives.PlacementMode.Right, "Top" => System.Windows.Controls.Primitives.PlacementMode.Bottom, "Bottom" => System.Windows.Controls.Primitives.PlacementMode.Top, _ => System.Windows.Controls.Primitives.PlacementMode.Left });
                stack.Children.Add(ring);
            }
            var button = new Button { Content = "⚙", Width = 44, Height = 32, Background = Brushes.Transparent, Foreground = Brush("#B6BBC4"), BorderThickness = new Thickness(0), ToolTip = "Codenotch settings" };
            AutomationProperties.SetName(button, "Codenotch settings");
            button.Click += (_, _) => OpenMenu();
            stack.Children.Add(button);
        }
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = stack.DesiredSize.Width + 12;
        Height = stack.DesiredSize.Height + 12;
        Position();
    }

    private static object Details(Reading? reading, string name)
    {
        var panel = new StackPanel { Margin = new Thickness(10), MaxWidth = 310 };
        panel.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = reading?.Status ?? "Checking usage…", Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (reading != null)
        {
            foreach (var window in reading.Windows)
            {
                panel.Children.Add(new TextBlock { Text = $"{window.Label}    {window.Percent:0.#}% used", Margin = new Thickness(0, 4, 0, 0) });
                panel.Children.Add(new TextBlock { Text = window.ResetsAt is { } reset ? $"Resets {reset.ToLocalTime():ddd HH:mm}" : "Reset time not reported", Opacity = .65, FontSize = 11 });
            }
            panel.Children.Add(new TextBlock { Text = reading.Source, FontSize = 10, Opacity = .65, Margin = new Thickness(0, 10, 0, 0) });
            if (reading.RecordedAt is { } stamp) panel.Children.Add(new TextBlock { Text = $"Reading from {stamp.ToLocalTime():g}", FontSize = 10, Opacity = .65 });
        }
        return panel;
    }

    private void OpenMenu()
    {
        expanded = true; Render();
        ContextMenu = BuildMenu();
        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = demo ? "Codenotch · DEMO" : "Codenotch for Windows", IsEnabled = false });
        var refresh = new MenuItem { Header = "Refresh now" };
        refresh.Click += async (_, _) => await Refresh(); menu.Items.Add(refresh);
        var always = new MenuItem { Header = "Always show rings", IsCheckable = true, IsChecked = settings.AlwaysShow };
        always.Click += (_, _) => { settings.AlwaysShow = always.IsChecked; SaveSettings(); Render(); }; menu.Items.Add(always);
        var edges = new MenuItem { Header = "Screen edge" };
        foreach (var edge in new[] { "Left", "Right", "Top", "Bottom" })
        {
            var item = new MenuItem { Header = edge, IsCheckable = true, IsChecked = settings.Edge == edge };
            item.Click += (_, _) => { settings.Edge = edge; SaveSettings(); ContextMenu = BuildMenu(); Render(); }; edges.Items.Add(item);
        }
        menu.Items.Add(edges);
        var displays = new MenuItem { Header = "Display" };
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var item = new MenuItem { Header = screen.DeviceName + (screen.Primary ? " (primary)" : ""), IsCheckable = true, IsChecked = settings.Screen == screen.DeviceName || settings.Screen == null && screen.Primary };
            item.Click += (_, _) => { settings.Screen = screen.DeviceName; SaveSettings(); ContextMenu = BuildMenu(); Position(); }; displays.Items.Add(item);
        }
        menu.Items.Add(displays);
        var connections = new MenuItem { Header = "Providers" };
        foreach (var provider in descriptors)
        {
            var item = new MenuItem { Header = provider.Name, IsCheckable = true, IsChecked = !settings.Disabled.Contains(provider.Id), ToolTip = "Turning off stops credential reads and clears saved usage. It does not sign you out." };
            item.Click += async (_, _) =>
            {
                if (item.IsChecked) settings.Disabled.Remove(provider.Id);
                else { settings.Disabled.Add(provider.Id); readings.Remove(provider.Id); archive.Readings.Remove(provider.Id); }
                SaveSettings(); SaveArchive(); Render(); await Refresh();
            };
            connections.Items.Add(item);
        }
        connections.Items.Add(new MenuItem { Header = "Antigravity · Windows support pending", IsEnabled = false });
        menu.Items.Add(connections);
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "Quit" }; quit.Click += (_, _) => Close(); menu.Items.Add(quit);
        return menu;
    }

    private void SaveSettings()
    {
        if (demo) return;
        try { State.Save("settings.json", settings); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { tray.ShowBalloonTip(3000, "Codenotch", "Could not save settings to LocalAppData.", Forms.ToolTipIcon.Warning); }
    }
    private void SaveArchive()
    {
        if (demo) return;
        try { State.Save("usage.json", archive); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { tray.ShowBalloonTip(3000, "Codenotch", "Could not save usage and retry deadlines to LocalAppData.", Forms.ToolTipIcon.Warning); }
    }

    private async Task Refresh()
    {
        if (demo || refreshing || lifetime.IsCancellationRequested) return;
        refreshing = true;
        try
        {
            foreach (var provider in descriptors)
            {
                if (settings.Disabled.Contains(provider.Id) || lifetime.IsCancellationRequested) continue;
                if (archive.RetryAfter.TryGetValue(provider.Id, out var deadline) && deadline > DateTimeOffset.UtcNow)
                {
                    Failure(provider, $"Rate limited · retry after {deadline.ToLocalTime():HH:mm}"); continue;
                }
                try
                {
                    var reading = await Task.Run(() => provider.Fetch(lifetime.Token), lifetime.Token);
                    if (settings.Disabled.Contains(provider.Id) || lifetime.IsCancellationRequested) continue;
                    readings[provider.Id] = reading;
                    archive.Readings[provider.Id] = reading;
                    archive.RetryAfter.Remove(provider.Id); archive.Failures.Remove(provider.Id);
                }
                catch (Exception error)
                {
                    if (lifetime.IsCancellationRequested) return;
                    if (settings.Disabled.Contains(provider.Id)) continue;
                    var message = error switch
                    {
                        ProviderFailure failure => failure.Message,
                        UnauthorizedAccessException => "Access denied to the owning tool's local data",
                        JsonException => "Usage or credential format not recognized",
                        OperationCanceledException => "Usage request timed out",
                        HttpRequestException => "Usage service is unreachable",
                        _ => "Could not read usage; retry later"
                    };
                    if (error is ProviderFailure { RetrySeconds: { } seconds })
                    {
                        var attempts = archive.Failures.GetValueOrDefault(provider.Id);
                        archive.Failures[provider.Id] = Math.Min(attempts + 1, 5);
                        archive.RetryAfter[provider.Id] = DateTimeOffset.UtcNow.AddSeconds(Math.Max(Math.Min(900, 60 * Math.Pow(2, attempts)), Math.Min(seconds, 86400)));
                    }
                    Failure(provider, message);
                }
            }
            SaveArchive(); Render();
        }
        finally { refreshing = false; }
    }

    private void Failure(Provider provider, string message)
    {
        readings[provider.Id] = readings.TryGetValue(provider.Id, out var previous) && previous.Windows.Count > 0
            ? previous with { Status = "Stale · " + message }
            : new(provider.Id, provider.Name, message, "", null, []);
    }
}

internal sealed class UsageRing(double? percent, Brush accent, bool stale) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize) => new(44, 44);
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 44, 44));
        dc.PushOpacity(stale ? .5 : 1);
        var center = new Point(22, 22);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(49, 53, 59)), 3), center, 18, 18);
        var fraction = Math.Clamp((percent ?? 0) / 100, 0, 1);
        var pen = new Pen(accent, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 1) dc.DrawEllipse(null, pen, center, 18, 18);
        else if (fraction > 0)
        {
            var angle = fraction * Math.PI * 2;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(22, 4), false, false);
                context.ArcTo(new Point(22 + 18 * Math.Sin(angle), 22 - 18 * Math.Cos(angle)), new Size(18, 18), 0, fraction > .5, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, pen, geometry);
        }
        var text = new FormattedText(percent is { } n ? $"{n:0}" : "–", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(22 - text.Width / 2, 22 - text.Height / 2));
        dc.Pop();
    }
}
