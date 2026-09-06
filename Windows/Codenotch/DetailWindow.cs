using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Codenotch;

internal sealed class DetailWindow : Window
{
    private readonly Canvas canvas = new();
    private readonly Border card;
    private readonly Path tail = new() { Fill = Brushes.Black, IsHitTestVisible = false };
    private readonly bool demo;
    private readonly Action settings;
    private const double CardWidth = 286;
    private ProviderCell? anchor;
    private Settings? placementSettings;
    private Size desiredSize = new(CardWidth + 40, 100);
    private bool positioning;
    public DetailWindow(bool demo, Action settings)
    {
        this.demo = demo; this.settings = settings;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Content = canvas; Width = CardWidth + 40;
        card = new Border { Width = CardWidth, Background = Brushes.Black, CornerRadius = new CornerRadius(18), Padding = new Thickness(16),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 5, Opacity = .35 } };
        Canvas.SetLeft(card, 20); Canvas.SetTop(card, 20); canvas.Children.Add(card); canvas.Children.Add(tail);
        SourceInitialized += (_, _) =>
        {
            NativeWindow.NonActivating(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        };
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Leave DPI handling to WPF, then place the card using its updated rendering scale.
        if (message == 0x02E0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Reposition));
        return IntPtr.Zero;
    }
    public void Update(Connection connection)
    {
        Title = "Codenotch · " + connection.Provider.Name + " usage";
        var panel = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Glyphs.View(connection.Provider.Kind, 18));
        var title = Theme.Label(connection.Provider.Name.Replace(" Code", "") + " Usage", 14, Theme.Text, FontWeights.SemiBold); title.Margin = new Thickness(9, 0, 0, 0); header.Children.Add(title); panel.Children.Add(header);
        if (demo) { var sample = Theme.Label("DEMO · SAMPLE DATA", 9, Theme.Brush("#E3C997"), FontWeights.SemiBold); sample.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(sample); }
        if (connection.State != ConnectionState.Connected)
        {
            var status = Theme.Label(connection.StatusLabel + " · " + connection.Message, 11, Theme.StatusColor(connection.State)); status.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(status);
        }
        if (connection.Reading != null)
        {
            foreach (var window in connection.Reading.Windows)
            {
                var block = new StackPanel { Margin = new Thickness(0, 17, 0, 0) };
                var labels = new DockPanel();
                var reset = Theme.Label(Theme.Reset(window.ResetsAt), 10, Theme.Muted); reset.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(reset, Dock.Right); labels.Children.Add(reset);
                labels.Children.Add(Theme.Label(window.Label, 11)); block.Children.Add(labels);
                var track = new Border { Background = Theme.Brush("#2D2D2D"), Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 9, 0, 0) };
                var fill = new Border { Background = Theme.UsageColor(window.Percent), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                fill.BeginAnimation(WidthProperty, Theme.Animate(Math.Clamp(window.Percent / 100, 0, 1) * (CardWidth - 32), 520)); track.Child = fill; block.Children.Add(track);
                var used = Theme.Label($"{window.Percent:0.#}% Used", 11); used.Margin = new Thickness(0, 9, 0, 0); block.Children.Add(used); panel.Children.Add(block);
            }
            var source = Theme.Label((demo ? "Preview" : connection.Reading.Source) + " · " + Theme.Age(connection.Reading.RecordedAt), 9, Theme.Muted); source.Margin = new Thickness(0, 18, 0, 0); panel.Children.Add(source);
        }
        if (connection.State is not ConnectionState.Connected)
        {
            var button = Theme.Button("Manage connection →", settings); button.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(button);
        }
        card.Child = panel;
        card.InvalidateMeasure();
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        desiredSize = new Size(CardWidth + 40, card.DesiredSize.Height + 40);
        Width = desiredSize.Width; Height = desiredSize.Height;
        if (IsVisible) Reposition();
    }
    public void ShowAt(ProviderCell cell, Settings settings)
    {
        anchor = cell; placementSettings = settings;
        if (!IsVisible) Show();
        Reposition();
        canvas.Opacity = 0; canvas.BeginAnimation(OpacityProperty, Theme.Animate(1, 180));
        var offset = new TranslateTransform(settings.Edge == "Right" ? 8 : settings.Edge == "Left" ? -8 : 0, settings.Edge == "Top" ? -8 : settings.Edge == "Bottom" ? 8 : 0);
        canvas.RenderTransform = offset; offset.BeginAnimation(TranslateTransform.XProperty, Theme.Animate(0, 240)); offset.BeginAnimation(TranslateTransform.YProperty, Theme.Animate(0, 240));
    }
    internal void Reposition()
    {
        if (positioning || !IsVisible || anchor?.IsLoaded != true || placementSettings == null) return;
        positioning = true;
        try
        {
            var screen = NativeWindow.Screen(placementSettings); var area = screen.WorkingArea;
            var scale = NativeWindow.Prepare(this, screen);
            var point = anchor.PointToScreen(new Point(22, 22));
            var width = desiredSize.Width * scale; var height = desiredSize.Height * scale;
            var x = placementSettings.Edge switch { "Left" => point.X + 50 * scale, "Right" => point.X - width - 50 * scale, _ => point.X - width / 2 };
            var y = placementSettings.Edge switch { "Top" => point.Y + 80 * scale, "Bottom" => point.Y - height - 55 * scale, _ => point.Y - height / 2 };
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)); y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
            NativeWindow.Place(this, x, y, scale, desiredSize);
            DrawTail(placementSettings.Edge, Math.Clamp((point.X - x) / scale, 44, desiredSize.Width - 44), Math.Clamp((point.Y - y) / scale, 44, desiredSize.Height - 44));
        }
        finally { positioning = false; }
    }
    private void DrawTail(string edge, double targetX, double targetY)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            if (edge == "Right") { path.BeginFigure(new Point(desiredSize.Width - 21, targetY - 12), true, true); path.LineTo(new Point(desiredSize.Width - 2, targetY), true, false); path.LineTo(new Point(desiredSize.Width - 21, targetY + 12), true, false); }
            else if (edge == "Left") { path.BeginFigure(new Point(21, targetY - 12), true, true); path.LineTo(new Point(2, targetY), true, false); path.LineTo(new Point(21, targetY + 12), true, false); }
            else if (edge == "Top") { path.BeginFigure(new Point(targetX - 12, 21), true, true); path.LineTo(new Point(targetX, 2), true, false); path.LineTo(new Point(targetX + 12, 21), true, false); }
            else { path.BeginFigure(new Point(targetX - 12, desiredSize.Height - 21), true, true); path.LineTo(new Point(targetX, desiredSize.Height - 2), true, false); path.LineTo(new Point(targetX + 12, desiredSize.Height - 21), true, false); }
        }
        tail.Data = geometry;
    }
    public void Dismiss()
    {
        BeginAnimation(OpacityProperty, Theme.Animate(0, 120));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) }; timer.Tick += (_, _) => { timer.Stop(); Close(); }; timer.Start();
    }
    internal void VerifyLayout()
    {
        var expected = CardWidth + 40;
        if (ActualWidth + 1 < expected || card.ActualWidth + 40 > ActualWidth + 1 || card.ActualHeight + 40 > ActualHeight + 1)
            throw new InvalidOperationException($"Popup clipped: window {ActualWidth:0.##}x{ActualHeight:0.##}, card {card.ActualWidth:0.##}x{card.ActualHeight:0.##}, DPI {VisualTreeHelper.GetDpi(this).PixelsPerInchX:0}. Expected width {expected}.");
    }
    internal string LayoutInfo => $"{ActualWidth:0.##}x{ActualHeight:0.##} DIP, {VisualTreeHelper.GetDpi(this).PixelsPerInchX:0} DPI";
}
