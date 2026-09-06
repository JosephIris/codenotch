using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Codenotch;

internal static class Theme
{
    public static SolidColorBrush Brush(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    public static readonly Brush Text = Brush("#F4F4F5"), Muted = Brush("#8D8D96"), Line = Brush("#29292D"), Surface = Brush("#171719"), Green = Brush("#00FF88"), Amber = Brush("#F2FF00"), Orange = Brush("#FF3F00");
    public static Brush UsageColor(double percent) => percent < 50 ? Green : percent < 70 ? Amber : Orange;
    public static Brush StatusColor(ConnectionState state) => state switch { ConnectionState.Connected => Green, ConnectionState.RateLimited or ConnectionState.Stale => Brush("#EABF72"), ConnectionState.Error => Brush("#FF8B7C"), _ => Muted };
    public static bool Motion => SystemParameters.ClientAreaAnimation;
    public static DoubleAnimation Animate(double to, double milliseconds = 220) => new(to, TimeSpan.FromMilliseconds(Motion ? milliseconds : 0)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
    public static TextBlock Label(string text, double size = 12, Brush? color = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = color ?? Text, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(15, 9, 15, 9), FontSize = 12, FontWeight = FontWeights.Medium,
            Foreground = primary ? Brush("#131315") : Text, Background = primary ? Text : Brush("#28282C"), BorderBrush = primary ? Text : Brush("#37373C"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
        button.Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border x:Name="surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.8" /></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.55" /></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.4" /></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
        button.Click += (_, _) => action();
        return button;
    }

    public static string Age(DateTimeOffset? stamp)
    {
        if (stamp == null) return "No reading yet";
        var age = DateTimeOffset.UtcNow - stamp.Value;
        return age.TotalSeconds < 60 ? "just now" : age.TotalMinutes < 60 ? $"{Math.Max(1, (int)age.TotalMinutes)}m ago" : age.TotalHours < 24 ? $"{(int)age.TotalHours}h ago" : $"{(int)age.TotalDays}d ago";
    }
    public static string Reset(DateTimeOffset? stamp)
    {
        if (stamp == null) return "Reset not reported";
        var remaining = stamp.Value - DateTimeOffset.UtcNow;
        return remaining.TotalSeconds <= 0 ? "Reset passed · refresh" : remaining.TotalMinutes < 60 ? $"Resets in {Math.Ceiling(remaining.TotalMinutes)} min" : $"Resets {stamp.Value.ToLocalTime():ddd h:mm tt}";
    }
}

internal static class Glyphs
{
    private static readonly Dictionary<string, Geometry> outlines = Load();
    private static Dictionary<string, Geometry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Codenotch.Assets.glyphs.json")!;
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        return data.ToDictionary(p => p.Key, p => { var g = Geometry.Parse(p.Value); g.Freeze(); return g; });
    }
    public static void Draw(DrawingContext dc, string id, Rect bounds, Brush brush)
    {
        var key = id.StartsWith("claude") ? "claude" : id == "codex" ? "openai" : id;
        if (!outlines.TryGetValue(key, out var geometry)) return;
        dc.PushTransform(new TranslateTransform(bounds.Left, bounds.Top));
        dc.PushTransform(new ScaleTransform(bounds.Width, bounds.Height));
        dc.DrawGeometry(brush, null, geometry);
        dc.Pop(); dc.Pop();
    }
    public static FrameworkElement View(string id, double size = 22) => new GlyphView(id) { Width = size, Height = size };
    private sealed class GlyphView(string id) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => Draw(dc, id, new Rect(0, 0, ActualWidth, ActualHeight), Theme.Text);
    }
}

internal sealed class ProviderCell : FrameworkElement
{
    public static readonly DependencyProperty AmountProperty = DependencyProperty.Register(nameof(Amount), typeof(double), typeof(ProviderCell), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(ProviderCell), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RotationProperty = DependencyProperty.Register(nameof(Rotation), typeof(double), typeof(ProviderCell), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Amount { get => (double)GetValue(AmountProperty); set => SetValue(AmountProperty, value); }
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    public double Rotation { get => (double)GetValue(RotationProperty); set => SetValue(RotationProperty, value); }
    private bool spinning;
    public Connection Connection { get; private set; }
    public event Action<ProviderCell>? Entered;
    public event Action? Left;
    public event Action? Clicked;
    protected override AutomationPeer OnCreateAutomationPeer() => new CellPeer(this);
    private sealed class CellPeer(ProviderCell owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => "ProviderCell";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);
        public void Invoke() => owner.Dispatcher.BeginInvoke(() => owner.Clicked?.Invoke());
    }
    public ProviderCell(Connection connection)
    {
        Connection = connection; Width = 44; Height = 74; Focusable = true; Cursor = Cursors.Hand;
        MouseEnter += (_, _) => { BeginAnimation(HoverProperty, Theme.Animate(1, 180)); Entered?.Invoke(this); };
        MouseLeave += (_, _) => { BeginAnimation(HoverProperty, Theme.Animate(0, 280)); Left?.Invoke(); };
        MouseLeftButtonUp += (_, e) => { Clicked?.Invoke(); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Clicked?.Invoke(); e.Handled = true; } };
        Update(connection);
    }
    public void Update(Connection connection)
    {
        var previous = Connection.Reading?.Windows.FirstOrDefault()?.Percent;
        Connection = connection;
        if (connection.State == ConnectionState.Checking && !spinning && Theme.Motion)
        {
            spinning = true;
            BeginAnimation(RotationProperty, new DoubleAnimation(0, Math.PI * 2, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else if (connection.State != ConnectionState.Checking && spinning) { spinning = false; BeginAnimation(RotationProperty, null); }
        var amount = connection.Reading?.Windows.FirstOrDefault()?.Percent ?? 0;
        if (previous != amount || Amount != amount) BeginAnimation(AmountProperty, Theme.Animate(amount, 600));
        System.Windows.Automation.AutomationProperties.SetName(this, $"{connection.Provider.Name}, {connection.PercentLabel} used, {connection.StatusLabel}");
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height));
        var stale = Connection.State is not ConnectionState.Connected;
        var opacity = Connection.State == ConnectionState.Checking ? .65 : stale ? .45 : 1;
        dc.PushOpacity(opacity);
        dc.PushTransform(new ScaleTransform(1 + Hover * .065, 1 + Hover * .065, 22, 22));
        dc.DrawEllipse(null, new Pen(Theme.Brush("#303030"), 5.83), new Point(22, 22), 19.08, 19.08);
        if (Connection.HasReading) DrawArc(dc, new Point(22, 22), 19.08, Amount / 100, new Pen(Theme.UsageColor(Amount), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Glyphs.Draw(dc, Connection.Provider.Kind, new Rect(13.35, 13.35, 17.3, 17.3), Theme.Text);
        if (Connection.State == ConnectionState.Checking) DrawArc(dc, new Point(22, 22), 13.5, .23, new Pen(Theme.Text, 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Rotation);
        dc.Pop(); dc.Pop();
        var label = new FormattedText(Connection.PercentLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 14.2, stale ? Theme.Muted : Theme.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(22 - label.Width / 2, 54));
        if (Connection.State == ConnectionState.RateLimited || Connection.State == ConnectionState.Error)
        {
            dc.DrawEllipse(Theme.Brush("#EABF72"), new Pen(Brushes.Black, 2), new Point(39, 5), 3.5, 3.5);
        }
    }
    public static void DrawArc(DrawingContext dc, Point center, double radius, double fraction, Pen pen, double rotation = 0)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction <= 0) return;
        if (fraction >= 1) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
        var start = rotation - Math.PI / 2;
        var end = start + fraction * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X + radius * Math.Cos(start), center.Y + radius * Math.Sin(start)), false, false);
            g.ArcTo(new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}

internal sealed class SettingsOrb : FrameworkElement
{
    private readonly Action click;
    protected override AutomationPeer OnCreateAutomationPeer() => new OrbPeer(this);
    private sealed class OrbPeer(SettingsOrb owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => "SettingsOrb";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);
        public void Invoke() => owner.Dispatcher.BeginInvoke(owner.click);
    }
    public string Edge { get; set; } = "Right";
    public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(nameof(Hover), typeof(double), typeof(SettingsOrb), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Hover { get => (double)GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    public SettingsOrb(Action click)
    {
        this.click = click;
        Width = 64; Height = 64; Cursor = Cursors.Hand;
        MouseEnter += (_, _) => BeginAnimation(HoverProperty, Theme.Animate(1, 230));
        MouseLeave += (_, _) => BeginAnimation(HoverProperty, Theme.Animate(0, 320));
        MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        ToolTip = "Integrations & settings";
        System.Windows.Automation.AutomationProperties.SetName(this, "Open integrations and settings");
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new TranslateTransform(9, 9));
        dc.DrawEllipse(Brushes.Transparent, null, new Point(23, 23), 30, 30);
        dc.PushOpacity(1 - Hover);
        var rotation = Edge switch { "Left" or "Top" => -Math.PI / 2, "Bottom" => Math.PI, _ => 0 };
        ProviderCell.DrawArc(dc, new Point(23, 23), 28.58 * (1 - Hover * .14), .25, new Pen(Brushes.Black, 6.77) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, rotation);
        dc.Pop();
        dc.PushOpacity(Hover);
        dc.DrawEllipse(Brushes.Black, null, new Point(23, 23), 21, 21);
        dc.PushTransform(new RotateTransform(-35 * (1 - Hover), 23, 23));
        var pen = new Pen(Theme.Text, 1.5);
        dc.DrawEllipse(null, pen, new Point(23, 23), 6.4, 6.4);
        dc.DrawEllipse(null, pen, new Point(23, 23), 2, 2);
        for (var i = 0; i < 8; i++) { var a = i * Math.PI / 4; dc.DrawLine(pen, new Point(23 + 6 * Math.Cos(a), 23 + 6 * Math.Sin(a)), new Point(23 + 9 * Math.Cos(a), 23 + 9 * Math.Sin(a))); }
        dc.Pop(); dc.Pop();
        dc.Pop();
    }
}

internal static class NotchGeometry
{
    public static Geometry Create(double depth, double length, string edge)
    {
        var corner = Math.Min(29.63, depth / 2);
        var curl = Math.Min(38.74, Math.Min(length / 2, depth - corner));
        corner = Math.Min(corner, (length - 2 * curl) / 2);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(depth, 0), true, true);
            g.ArcTo(new Point(depth - curl, curl), new Size(curl, curl), 0, false, SweepDirection.Clockwise, true, false);
            g.LineTo(new Point(corner, curl), true, false);
            g.ArcTo(new Point(0, curl + corner), new Size(corner, corner), 0, false, SweepDirection.Counterclockwise, true, false);
            g.LineTo(new Point(0, length - curl - corner), true, false);
            g.ArcTo(new Point(corner, length - curl), new Size(corner, corner), 0, false, SweepDirection.Counterclockwise, true, false);
            g.LineTo(new Point(depth - curl, length - curl), true, false);
            g.ArcTo(new Point(depth, length), new Size(curl, curl), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Transform = new MatrixTransform(edge switch
        {
            "Left" => new Matrix(-1, 0, 0, 1, depth, 0), "Top" => new Matrix(0, -1, 1, 0, 0, depth),
            "Bottom" => new Matrix(0, 1, 1, 0, 0, 0), _ => Matrix.Identity
        });
        return geometry;
    }
}
