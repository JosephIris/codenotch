using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Codenotch;

internal sealed class Notch : Window
{
    private readonly UsageService service;
    private readonly NotchSurface surface;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer placement = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer detailsDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private IntegrationWindow? settingsWindow;
    private DetailWindow? details;
    private ProviderCell? hovered;
    private bool closed;

    public Notch(UsageService service, bool smoke, bool showSettings, bool verifyLive = false)
    {
        this.service = service;
        Title = service.IsDemo ? "Codenotch · DEMO PREVIEW" : "Codenotch";
        Icon = AppBrand.Image;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        UseLayoutRounding = true;
        surface = new NotchSurface(service, OpenSettings); Content = surface;
        surface.LayoutChanged += ResizeAndPosition;
        surface.CellEntered += cell => { hide.Stop(); hovered = cell; detailsDelay.Stop(); detailsDelay.Start(); };
        surface.CellLeft += () => { detailsDelay.Stop(); hide.Start(); };
        surface.CellClicked += cell => { if (cell.Connection.State == ConnectionState.Connected) ToolActions.OpenUsage(cell.Connection.Provider); else OpenSettings(); };
        MouseEnter += (_, _) => { hide.Stop(); surface.Expand(true); };
        MouseLeave += (_, _) => hide.Start();
        hide.Tick += (_, _) =>
        {
            if (IsMouseOver || details?.IsMouseOver == true || ContextMenu?.IsOpen == true) return;
            hide.Stop(); CloseDetails(); surface.Expand(service.Settings.AlwaysShow);
        };
        detailsDelay.Tick += (_, _) => { detailsDelay.Stop(); if (hovered != null) ShowDetails(hovered); };
        ContextMenu = Menu();
        tray = new Forms.NotifyIcon { Icon = AppBrand.TrayIcon(), Text = Title, Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        tray.ContextMenuStrip.Items.Add("Integrations & settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        tray.ContextMenuStrip.Items.Add("Refresh usage", null, async (_, _) => await service.RefreshAll());
        tray.ContextMenuStrip.Items.Add("Quit Codenotch", null, (_, _) => Dispatcher.Invoke(Close));
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        SourceInitialized += (_, _) => { NativeWindow.NonActivating(this); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Messages); };
        Loaded += async (_, _) =>
        {
            Update();
            if (!smoke && (!service.Settings.SetupSeen || showSettings)) OpenSettings();
            await service.RefreshAll();
            if (smoke) await Smoke();
            else if (verifyLive)
            {
                var folder = Path.Combine(Environment.CurrentDirectory, "Windows", "artifacts", "live-verification"); Directory.CreateDirectory(folder);
                var report = service.Connections.Values.Select(c => new { Provider = c.Provider.Name, State = c.State.ToString(), c.Message, c.Reading?.Source, c.Reading?.RecordedAt, Windows = c.Reading?.Windows });
                File.WriteAllText(Path.Combine(folder, "connections.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                OpenSettings(); await System.Threading.Tasks.Task.Delay(400); Capture(settingsWindow!, Path.Combine(folder, "Integrations.png"));
            }
        };
        service.Changed += Update;
        poll.Tick += async (_, _) => await service.RefreshAll(); placement.Tick += (_, _) => ResizeAndPosition();
        poll.Start(); placement.Start(); SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) =>
        {
            closed = true; service.Changed -= Update; SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            poll.Stop(); placement.Stop(); hide.Stop(); detailsDelay.Stop();
            CloseDetails(); settingsWindow?.Close(); tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose();
        };
    }
    private IntPtr Messages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0084)
        {
            var p = PointFromScreen(new Point((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff)));
            if (!surface.InteractiveAt(p)) { handled = true; return new IntPtr(-1); }
        }
        if (message is 0x02E0 or 0x001A) Dispatcher.BeginInvoke(ResizeAndPosition);
        return IntPtr.Zero;
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Update);
    private void Update()
    {
        if (closed) return;
        surface.Update(); ResizeAndPosition();
        if (!service.Settings.AlwaysShow && !IsMouseOver && details?.IsMouseOver != true) surface.Expand(false);
        if (hovered != null && details != null && service.Connections.TryGetValue(hovered.Connection.Provider.Id, out var connection)) details.Update(connection);
    }
    private void ResizeAndPosition()
    {
        if (closed || !IsLoaded) return;
        Width = surface.Width; Height = surface.Height;
        var screen = NativeWindow.Screen(service.Settings); var area = screen.WorkingArea; var scale = NativeWindow.Scale(screen);
        var width = Width * scale; var height = Height * scale;
        var x = service.Settings.Edge switch { "Left" => area.Left, "Right" => area.Right - width, _ => area.Left + (area.Width - width) / 2 };
        var y = service.Settings.Edge switch { "Top" => area.Top, "Bottom" => area.Bottom - height, _ => area.Top + (area.Height - height) / 2 };
        NativeWindow.Place(this, x, y, scale);
    }
    private void ShowDetails(ProviderCell cell)
    {
        if (details == null)
        {
            details = new DetailWindow(service.IsDemo, () => { CloseDetails(); OpenSettings(); });
            details.MouseEnter += (_, _) => hide.Stop(); details.MouseLeave += (_, _) => hide.Start();
        }
        details.Update(cell.Connection); details.ShowAt(cell, service.Settings);
    }
    private void CloseDetails() { if (closed) details?.Close(); else details?.Dismiss(); details = null; hovered = null; }
    public void OpenSettings()
    {
        if (closed) return;
        CloseDetails();
        if (settingsWindow == null) { settingsWindow = new IntegrationWindow(service, Update); settingsWindow.Closed += (_, _) => settingsWindow = null; }
        settingsWindow.Show(); settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate();
        service.Settings.SetupSeen = true; service.Save();
    }
    private ContextMenu Menu()
    {
        var menu = new ContextMenu { Background = Theme.Surface, Foreground = Theme.Text, BorderBrush = Theme.Line, Padding = new Thickness(6) };
        var settings = new MenuItem { Header = "Integrations & settings" }; settings.Click += (_, _) => OpenSettings(); menu.Items.Add(settings);
        var refresh = new MenuItem { Header = "Refresh usage" }; refresh.Click += async (_, _) => await service.RefreshAll(); menu.Items.Add(refresh);
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "Quit Codenotch" }; quit.Click += (_, _) => Close(); menu.Items.Add(quit);
        return menu;
    }
    private async System.Threading.Tasks.Task Smoke()
    {
        var folder = Path.Combine(Environment.CurrentDirectory, "Windows", "artifacts", "smoke-v2"); Directory.CreateDirectory(folder);
        foreach (var edge in new[] { "Left", "Right", "Top", "Bottom" })
        {
            service.Settings.Edge = edge; surface.Expand(true); Update();
            await System.Threading.Tasks.Task.Delay(650); UpdateLayout(); Capture(this, Path.Combine(folder, edge + ".png"));
        }
        service.Settings.Edge = "Right"; Update(); OpenSettings(); await System.Threading.Tasks.Task.Delay(250);
        Capture(settingsWindow!, Path.Combine(folder, "Integrations.png"));
        ShowDetails(surface.Cells.First()); await System.Threading.Tasks.Task.Delay(600); Capture(details!, Path.Combine(folder, "Usage.png"));
        CaptureScene(Path.Combine(folder, "Scene.png"));
        CloseDetails(); surface.Rest(); await System.Threading.Tasks.Task.Delay(450);
        Capture(this, Path.Combine(folder, "Rest.png")); surface.Expand(true);
        await System.Threading.Tasks.Task.Delay(100); Capture(this, Path.Combine(folder, "Unfold-100ms.png"));
        await System.Threading.Tasks.Task.Delay(130); Capture(this, Path.Combine(folder, "Unfold-230ms.png"));
        await System.Threading.Tasks.Task.Delay(260); Capture(this, Path.Combine(folder, "Unfold-490ms.png")); Close();
    }
    private void CaptureScene(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Theme.Brush("#A8DCD9").Color, Theme.Brush("#316A70").Color, 55), null, new Rect(0, 0, 640, 530));
            var notch = new RenderTargetBitmap((int)Width * 2, (int)Height * 2, 192, 192, PixelFormats.Pbgra32); notch.Render(this);
            dc.DrawImage(notch, new Rect(640 - Width, (530 - Height) / 2, Width, Height));
            if (details != null)
            {
                var card = new RenderTargetBitmap((int)details.Width * 2, (int)details.Height * 2, 192, 192, PixelFormats.Pbgra32); card.Render(details);
                dc.DrawImage(card, new Rect(640 - Width - details.Width - 8, 50, details.Width, details.Height));
            }
        }
        var bitmap = new RenderTargetBitmap(1280, 1060, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    internal static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
