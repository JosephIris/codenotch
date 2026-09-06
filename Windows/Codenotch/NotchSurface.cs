using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Codenotch;

internal sealed class NotchSurface : Canvas
{
    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(nameof(Reveal), typeof(double), typeof(NotchSurface), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((NotchSurface)d).LayoutCells()));
    public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }
    private readonly UsageService service;
    private readonly SettingsOrb orb;
    private readonly FrameworkElement empty;
    private readonly TextBlock demoLabel;
    private readonly Dictionary<string, ProviderCell> cells = [];
    private Geometry? shape;
    private double depth, length;
    private bool vertical;
    public IEnumerable<ProviderCell> Cells => cells.Values;
    public event Action? LayoutChanged;
    public event Action<ProviderCell>? CellEntered;
    public event Action? CellLeft;
    public event Action<ProviderCell>? CellClicked;
    public NotchSurface(UsageService service, Action open)
    {
        // Panel hit-testing requires a background even when OnRender paints the silhouette.
        // The HWND hook separately passes pointer events outside the shape through to the desktop.
        this.service = service; Background = Brushes.Transparent;
        orb = new SettingsOrb(open); Children.Add(orb);
        var panel = new StackPanel { Width = 58, Cursor = Cursors.Hand };
        panel.Children.Add(new TextBlock { Text = "+", Foreground = Theme.Text, FontSize = 31, TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Connect", Foreground = Theme.Muted, FontSize = 10, TextAlignment = TextAlignment.Center });
        panel.MouseLeftButtonUp += (_, e) => { open(); e.Handled = true; };
        empty = panel; Children.Add(empty);
        demoLabel = Theme.Label("DEMO", 8, Theme.Brush("#EABF72"), FontWeights.Bold); Children.Add(demoLabel);
        if (service.Settings.AlwaysShow) Reveal = 1;
        Update();
    }
    public void Expand(bool value) => BeginAnimation(RevealProperty, Theme.Animate(value ? 1 : 0, value ? 420 : 340));
    public void Rest() { service.Settings.AlwaysShow = false; Expand(false); }
    public void Update()
    {
        var visible = service.Connections.Values.Where(c => c.ShowInNotch).ToList();
        foreach (var id in cells.Keys.Except(visible.Select(c => c.Provider.Id)).ToList()) { Children.Remove(cells[id]); cells.Remove(id); }
        foreach (var connection in visible)
        {
            if (!cells.TryGetValue(connection.Provider.Id, out var cell))
            {
                cell = new ProviderCell(connection); cells[connection.Provider.Id] = cell; Children.Add(cell);
                cell.Entered += c => CellEntered?.Invoke(c); cell.Left += () => CellLeft?.Invoke(); cell.Clicked += () => CellClicked?.Invoke(cell);
            }
            cell.Update(connection);
        }
        vertical = service.Settings.Edge is "Left" or "Right"; depth = vertical ? 70 : 100;
        var count = Math.Max(1, cells.Count);
        length = 77.5 + 45 + count * (vertical ? 74 : 44) + (count - 1) * 31.4;
        Width = vertical ? depth : length + 34; Height = vertical ? length + 34 : depth;
        if (service.Settings.AlwaysShow) Expand(true);
        LayoutCells(); InvalidateVisual(); LayoutChanged?.Invoke();
    }
    private Point Place(double along, double across) => service.Settings.Edge switch
    {
        "Left" => new Point(across, along), "Right" => new Point(depth - across, along),
        "Top" => new Point(along, across), _ => new Point(along, depth - across)
    };
    private void LayoutCells()
    {
        if (orb == null) return;
        var reveal = Math.Clamp(Reveal, 0, 1); var i = 0;
        foreach (var provider in service.Providers)
        {
            if (!cells.TryGetValue(provider.Id, out var cell)) continue;
            var center = Place(38.75 + (vertical ? 26 : 22.5) + 22 + i++ * (vertical ? 105.4 : 75.4), 35);
            SetLeft(cell, center.X - 22); SetTop(cell, center.Y - 22);
            if (!vertical) SetTop(cell, service.Settings.Edge == "Top" ? 13 : depth - 87);
            cell.Opacity = Math.Clamp((reveal - .2) / .8, 0, 1); cell.IsHitTestVisible = reveal > .75;
            cell.RenderTransform = new TranslateTransform(service.Settings.Edge == "Right" ? (1 - reveal) * 18 : service.Settings.Edge == "Left" ? -(1 - reveal) * 18 : 0,
                service.Settings.Edge == "Bottom" ? (1 - reveal) * 18 : service.Settings.Edge == "Top" ? -(1 - reveal) * 18 : 0);
        }
        var orbCenter = Place(length, 38.74);
        orb.Edge = service.Settings.Edge; orb.InvalidateVisual();
        SetLeft(orb, orbCenter.X - 32); SetTop(orb, orbCenter.Y - 32); orb.Opacity = reveal; orb.IsHitTestVisible = reveal > .8;
        empty.Visibility = cells.Count == 0 ? Visibility.Visible : Visibility.Collapsed; empty.Opacity = reveal; empty.IsHitTestVisible = reveal > .75;
        var emptyCenter = Place(length / 2, depth / 2); SetLeft(empty, emptyCenter.X - 29); SetTop(empty, emptyCenter.Y - 32);
        demoLabel.Visibility = service.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        SetLeft(demoLabel, vertical ? 18 : length / 2 - 13); SetTop(demoLabel, vertical ? 32 : depth - 12);
        UpdateGeometry();
    }
    private void UpdateGeometry()
    {
        var t = Math.Clamp(Reveal, 0, 1); var d = 10 + (depth - 10) * t; var l = 79 + (length - 79) * t;
        shape = NotchGeometry.Create(d, l, service.Settings.Edge);
        var offset = service.Settings.Edge switch
        {
            "Right" => new Vector(depth - d, (length - l) / 2), "Left" => new Vector(0, (length - l) / 2),
            "Bottom" => new Vector((length - l) / 2, depth - d), _ => new Vector((length - l) / 2, 0)
        };
        var group = new TransformGroup(); group.Children.Add(shape.Transform); group.Children.Add(new TranslateTransform(offset.X, offset.Y)); shape.Transform = group;
        // Clip the visual children, not the input canvas: clipping the canvas itself
        // also clips its wake region and prevents a resting notch receiving mouse entry.
        foreach (var child in cells.Values.Cast<FrameworkElement>().Append(empty))
        {
            var slide = child.RenderTransform as TranslateTransform;
            var clip = new GeometryGroup { FillRule = FillRule.Nonzero };
            clip.Children.Add(shape);
            clip.Transform = new TranslateTransform(-GetLeft(child) - (slide?.X ?? 0), -GetTop(child) - (slide?.Y ?? 0));
            child.Clip = clip;
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (shape != null) dc.DrawGeometry(Brushes.Black, null, shape);
    }
    public bool InteractiveAt(Point point)
    {
        if (Reveal > .1) return shape?.FillContains(point) == true || new Rect(GetLeft(orb), GetTop(orb), 64, 64).Contains(point);
        var center = Place(length / 2, 10);
        return new Rect(center.X - (vertical ? 18 : 48), center.Y - (vertical ? 48 : 18), vertical ? 36 : 96, vertical ? 96 : 36).Contains(point);
    }
}
