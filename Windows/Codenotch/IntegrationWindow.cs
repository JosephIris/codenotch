using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace Codenotch;

internal sealed class IntegrationWindow : Window
{
    private readonly UsageService service;
    private readonly Action appearanceChanged;
    private readonly StackPanel body = new();
    private readonly Dictionary<string, Border> rows = [];
    private readonly Dictionary<string, Button> waitingButtons = [];
    private readonly System.Windows.Threading.DispatcherTimer countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock footer = Theme.Label("", 11, Theme.Muted);
    private readonly TextBlock notice = Theme.Label("", 12, Theme.Brush("#E3C997"));
    private readonly Border noticeBox;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal };
    private string tab = "Integrations";
    private bool returningFromSignIn;

    public IntegrationWindow(UsageService service, Action appearanceChanged)
    {
        this.service = service; this.appearanceChanged = appearanceChanged;
        Title = service.IsDemo ? "Codenotch · Demo preview" : "Codenotch · Integrations";
        Width = 640; Height = 740; MinHeight = 540; MinWidth = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; Foreground = Theme.Text; FontFamily = new FontFamily("Segoe UI");
        var shell = new Border { Background = Theme.Brush("#101012"), BorderBrush = Theme.Brush("#343438"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16) };
        Content = shell;
        var root = new Grid(); shell.Child = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(28, 22, 28, 0) }; root.Children.Add(header);
        var titlebar = new DockPanel { Margin = new Thickness(0, 0, 0, 26), Background = Brushes.Transparent };
        titlebar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1 && e.OriginalSource is not Button) DragMove(); };
        var close = Theme.Button("✕", Close); close.Padding = new Thickness(9, 4, 9, 4); close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0); close.Foreground = Theme.Muted; DockPanel.SetDock(close, Dock.Right); titlebar.Children.Add(close);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Border { Width = 18, Height = 18, BorderBrush = Theme.Text, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 10, 0) };
        brand.Children.Add(mark); brand.Children.Add(Theme.Label("codenotch", 14, Theme.Text, FontWeights.SemiBold));
        if (service.IsDemo) brand.Children.Add(new Border { Background = Theme.Brush("#382D1A"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(10, 0, 0, 0), Child = Theme.Label("DEMO PREVIEW", 9, Theme.Brush("#E3C997"), FontWeights.SemiBold) });
        titlebar.Children.Add(brand); header.Children.Add(titlebar);
        header.Children.Add(Theme.Label("Your assistants, at a glance.", 26, Theme.Text, FontWeights.SemiBold));
        header.Children.Add(new TextBlock { Text = "Connect your tools. Keep an eye on your limits.", FontSize = 13, Foreground = Theme.Muted, Margin = new Thickness(0, 8, 0, 22) });
        header.Children.Add(tabs); BuildTabs();

        var content = new StackPanel { Margin = new Thickness(28, 20, 28, 20) };
        noticeBox = new Border { Background = Theme.Brush("#242015"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14), Child = notice, Visibility = Visibility.Collapsed };
        content.Children.Add(noticeBox); content.Children.Add(body);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var bottom = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(28, 16, 28, 16) };
        var bottomPanel = new DockPanel(); bottom.Child = bottomPanel;
        var version = Theme.Label("v0.2 · Windows", 10, Theme.Muted); DockPanel.SetDock(version, Dock.Right); bottomPanel.Children.Add(version); bottomPanel.Children.Add(footer);
        Grid.SetRow(bottom, 2); root.Children.Add(bottom);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        service.Changed += Update;
        Closed += (_, _) => { service.Changed -= Update; countdown.Stop(); };
        countdown.Tick += (_, _) => UpdateCountdowns(); countdown.Start();
        Activated += async (_, _) => { if (returningFromSignIn) { returningFromSignIn = false; await service.RefreshAll(); } };
        Loaded += (_, _) => { Opacity = 0; BeginAnimation(OpacityProperty, Theme.Animate(1, 180)); };
        BuildBody(); Update();
    }
    private void BuildTabs()
    {
        tabs.Children.Clear();
        foreach (var name in new[] { "Integrations", "Appearance" })
        {
            var button = Theme.Button(name, () => { tab = name; BuildTabs(); BuildBody(); body.Opacity = 0; body.BeginAnimation(OpacityProperty, Theme.Animate(1, 180)); });
            button.Margin = new Thickness(0, 0, 8, 0); button.Padding = new Thickness(14, 8, 14, 8);
            button.Background = tab == name ? Theme.Brush("#2A2A2E") : Brushes.Transparent;
            button.Foreground = tab == name ? Theme.Text : Theme.Muted; button.BorderThickness = new Thickness(0); tabs.Children.Add(button);
        }
    }
    private void BuildBody()
    {
        body.Children.Clear(); rows.Clear(); waitingButtons.Clear();
        if (tab == "Appearance") { Appearance(); return; }
        foreach (var provider in service.Providers)
        {
            var row = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10) };
            rows[provider.Id] = row; body.Children.Add(row);
        }
        var note = Theme.Label(service.IsDemo ? "This is a visual preview. Every percentage here is sample data. Close the preview to use your real connections." : "Codenotch uses the accounts already signed in to your coding tools. Connect opens the tool's own sign-in; no passwords or API keys are entered here.", 11, Theme.Muted);
        note.Margin = new Thickness(2, 8, 2, 0); body.Children.Add(note);
        Update();
    }
    private void Update()
    {
        foreach (var pair in rows) DrawRow(pair.Value, service.Connections[pair.Key]);
        var count = service.Connections.Values.Count(c => c.State == ConnectionState.Connected);
        footer.Text = service.IsDemo ? "Preview mode · sample data only" : $"{count} connected · refreshes every 5 minutes";
        if (service.SaveError != null) ShowNotice(service.SaveError);
    }
    private void DrawRow(Border border, Connection connection)
    {
        var provider = connection.Provider;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); border.Child = grid;
        var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Brushes.Black, Child = Glyphs.View(provider.Kind, 20), VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(icon);
        var info = new StackPanel { Margin = new Thickness(12, 0, 12, 0) }; Grid.SetColumn(info, 1); grid.Children.Add(info);
        var heading = new WrapPanel(); heading.Children.Add(Theme.Label(provider.Name, 14, Theme.Text, FontWeights.SemiBold));
        var badge = Theme.Label(connection.StatusLabel, 10, Theme.StatusColor(connection.State)); badge.Margin = new Thickness(10, 3, 0, 0); heading.Children.Add(badge); info.Children.Add(heading);
        var summary = connection.HasReading ? $"{connection.PercentLabel} used · {connection.Reading!.Windows[0].Label}" : ToolActions.Description(provider);
        info.Children.Add(new TextBlock { Text = summary, Foreground = Theme.Muted, FontSize = 11, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (connection.State == ConnectionState.Connected)
        {
            var provenance = service.IsDemo ? "Sample data" : (connection.Reading!.Account is { Length: > 0 } account ? account + " · " : "") + (connection.Reading.Source.Contains("live") ? "Live" : "Verified") + " · " + Theme.Age(connection.Reading.RecordedAt);
            info.Children.Add(new TextBlock { Text = provenance, Foreground = Theme.Brush("#686870"), FontSize = 10, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        }
        else if (connection.State is ConnectionState.RateLimited or ConnectionState.Error or ConnectionState.Stale)
            info.Children.Add(new TextBlock { Text = connection.Message, Foreground = Theme.Brush("#C1AE8D"), FontSize = 10, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 105 }; Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        var label = connection.State switch { ConnectionState.Connected => "View usage ↗", ConnectionState.Checking => "Checking…", ConnectionState.RateLimited => "Waiting", ConnectionState.Error or ConnectionState.Stale => "Check again", _ => "Connect" };
        var action = Theme.Button(label, async () =>
        {
            if (service.IsDemo) { ShowNotice("Preview mode cannot connect to accounts. Close this preview and launch Codenotch normally."); return; }
            try
            {
                if (connection.State == ConnectionState.Connected) { ToolActions.OpenUsage(provider); return; }
                if (connection.State == ConnectionState.Disabled) service.Enable(provider.Id, true);
                await service.Refresh(provider.Id);
                if (service.Connections[provider.Id].State == ConnectionState.NeedsSignIn) { returningFromSignIn = true; ShowNotice(ToolActions.Connect(provider)); }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowNotice("Could not open the tool. Open it yourself, sign in, and choose Check connection."); }
        }, connection.State is ConnectionState.NeedsSignIn or ConnectionState.Disabled);
        action.IsEnabled = connection.State is not ConnectionState.Checking and not ConnectionState.RateLimited;
        if (connection.State == ConnectionState.RateLimited) waitingButtons[provider.Id] = action;
        else waitingButtons.Remove(provider.Id);
        action.Padding = new Thickness(11, 8, 11, 8); actions.Children.Add(action);
        if (connection.State != ConnectionState.Disabled)
        {
            var secondary = Theme.Button(connection.State == ConnectionState.NeedsSignIn ? "Check connection" : "Disconnect", async () =>
            {
                if (connection.State == ConnectionState.NeedsSignIn) await service.Refresh(provider.Id);
                else service.Enable(provider.Id, false);
            });
            secondary.Background = Brushes.Transparent; secondary.BorderThickness = new Thickness(0); secondary.FontSize = 10; secondary.Foreground = Theme.Muted; secondary.Padding = new Thickness(2, 6, 2, 0); actions.Children.Add(secondary);
        }
    }
    private void ShowNotice(string text) { notice.Text = text; noticeBox.Visibility = Visibility.Visible; }
    private void UpdateCountdowns()
    {
        foreach (var pair in waitingButtons)
        {
            var remaining = (service.Connections[pair.Key].RetryAt ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow;
            pair.Value.IsEnabled = remaining <= TimeSpan.Zero;
            pair.Value.Content = remaining <= TimeSpan.Zero ? "Check again" : remaining.TotalMinutes >= 1 ? $"Retry in {Math.Ceiling(remaining.TotalMinutes)}m" : $"Retry in {Math.Ceiling(remaining.TotalSeconds)}s";
        }
    }
    private void Appearance()
    {
        body.Children.Add(Theme.Label("Make it feel at home.", 18, Theme.Text, FontWeights.SemiBold));
        var description = Theme.Label("A quiet handle when you're focused. Your usage when you need it.", 12, Theme.Muted); description.Margin = new Thickness(0, 8, 0, 22); body.Children.Add(description);
        Section("Visibility", ["On hover", "Always visible"], service.Settings.AlwaysShow ? "Always visible" : "On hover", value => { service.Settings.AlwaysShow = value == "Always visible"; service.Save(); appearanceChanged(); });
        Section("Screen edge", ["Left", "Right", "Top", "Bottom"], service.Settings.Edge, value => { service.Settings.Edge = value; service.Save(); appearanceChanged(); });
        var screens = Forms.Screen.AllScreens;
        Section("Display", screens.Select((s, i) => $"Display {i + 1}" + (s.Primary ? " · main" : "")).ToArray(),
            screens.Select((s, i) => new { s, label = $"Display {i + 1}" + (s.Primary ? " · main" : "") }).FirstOrDefault(p => p.s.DeviceName == service.Settings.Screen || service.Settings.Screen == null && p.s.Primary)?.label ?? "Display 1 · main",
            value => { var index = Array.FindIndex(screens, s => $"Display {Array.IndexOf(screens, s) + 1}" + (s.Primary ? " · main" : "") == value); if (index >= 0) service.Settings.Screen = screens[index].DeviceName; service.Save(); appearanceChanged(); });
        var motion = Theme.Label(Theme.Motion ? "Animations follow Windows accessibility settings. Turn off animation effects in Windows to reduce motion." : "Reduced motion is enabled in Windows. Transitions are immediate.", 11, Theme.Muted); motion.Margin = new Thickness(0, 6, 0, 24); body.Children.Add(motion);
        body.Children.Add(Theme.Label("Original design by vinzdg · Windows port by JosephIris", 11, Theme.Muted));
    }
    private void Section(string title, string[] options, string selected, Action<string> choose)
    {
        var card = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
        var panel = new StackPanel(); card.Child = panel; panel.Children.Add(Theme.Label(title, 12, Theme.Text, FontWeights.SemiBold));
        var choices = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(choices);
        foreach (var option in options)
        {
            var button = Theme.Button(option, () => { choose(option); BuildBody(); }, option == selected); button.Margin = new Thickness(0, 0, 7, 4); choices.Children.Add(button);
        }
        body.Children.Add(card);
    }
}
