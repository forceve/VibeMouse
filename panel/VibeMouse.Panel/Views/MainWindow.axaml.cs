using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VibeMouse.Panel.Services;
using VibeMouse.Panel.ViewModels;

namespace VibeMouse.Panel.Views;

public partial class MainWindow : Window
{
    // ── Known events / commands (mirrors vibemouse/core/commands.py) ──────────

    private static readonly (string Event, string Label)[] KnownEvents =
    [
        ("mouse.side_front.press",  "Side Front Button"),
        ("mouse.side_rear.press",   "Side Rear Button"),
        ("hotkey.record_toggle",    "Record Hotkey"),
        ("hotkey.recording_submit", "Submit Hotkey"),
        ("gesture.up",              "Gesture ↑"),
        ("gesture.down",            "Gesture ↓"),
        ("gesture.left",            "Gesture ←"),
        ("gesture.right",           "Gesture →"),
    ];

    private static readonly string[] KnownCommands =
    [
        "—",
        "toggle_recording",
        "trigger_secondary_action",
        "submit_recording",
        "send_enter",
        "workspace_left",
        "workspace_right",
        "reload_config",
        "shutdown",
        "noop",
    ];

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _editMode;
    private readonly Dictionary<string, string> _bindings = new();

    // ── Services / ViewModel ─────────────────────────────────────────────────

    private readonly MainViewModel _vm;
    private readonly ConfigService _configService;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        var configPath = ConfigService.DefaultConfigPath();
        _configService = new ConfigService(configPath);

        var statusSvc    = new StatusService(ResolveStatusPath(_configService));
        var agentControl = new AgentControlService(statusSvc);

        _vm = new MainViewModel(_configService, statusSvc, agentControl);
        _vm.PropertyChanged += (_, e) => Dispatcher.UIThread.Post(() => SyncUi(e.PropertyName));

        InitializeControls();
        LoadBindings();
        BuildBindingsList();
        SyncUi(null);
    }

    // ── Config controls ───────────────────────────────────────────────────────

    private void InitializeControls()
    {
        ModelCombo.ItemsSource  = _vm.ModelOptions;
        ModelCombo.SelectedItem = _vm.Model;
        ModelCombo.SelectionChanged += (_, _) =>
            _vm.Model = ModelCombo.SelectedItem as string ?? _vm.Model;

        LogLevelCombo.ItemsSource  = _vm.LogLevelOptions;
        LogLevelCombo.SelectedItem = _vm.LogLevel;
        LogLevelCombo.SelectionChanged += (_, _) =>
            _vm.LogLevel = LogLevelCombo.SelectedItem as string ?? _vm.LogLevel;

        LanguageBox.Text         = _vm.Language;
        LanguageBox.TextChanged += (_, _) => _vm.Language = LanguageBox.Text ?? string.Empty;

        HotkeyBox.Text = _vm.Hotkey;
    }

    private void SyncUi(string? propertyName)
    {
        if (propertyName is null or "AgentState")
        {
            AgentStateText.Text       = _vm.AgentState;
            AgentStateText.Foreground = _vm.AgentState is "idle" or "recording" or "processing"
                ? SolidColorBrush.Parse("#4ade80")
                : SolidColorBrush.Parse("#888888");
        }

        if (propertyName is null or "ListenerMode")
            ListenerModeText.Text = _vm.ListenerMode;

        if (propertyName is null or "LastTranscript")
        {
            var t = _vm.LastTranscript;
            LastTranscriptText.Text = string.IsNullOrWhiteSpace(t) ? "—" : t;
        }

        if (propertyName is null or "IpcAvailable")
        {
            ReloadButton.IsEnabled = _vm.IpcAvailable;
            DoctorButton.IsEnabled = true;
            IpcHintText.IsVisible  = !_vm.IpcAvailable;
        }
    }

    // ── Bindings ──────────────────────────────────────────────────────────────

    private void LoadBindings()
    {
        _bindings.Clear();
        try
        {
            var doc = _configService.Load();
            if (doc["bindings"] is JsonObject b)
                foreach (var (k, v) in b)
                    _bindings[k] = v?.GetValue<string>() ?? string.Empty;
        }
        catch { }
    }

    private void SaveBindings()
    {
        try
        {
            var doc = _configService.Load();
            var kb  = new JsonObject();
            foreach (var (k, v) in _bindings)
                if (!string.IsNullOrEmpty(v) && v != "—")
                    kb[k] = v;
            doc["bindings"] = kb;
            _configService.Save(doc);
        }
        catch { }
    }

    private void BuildBindingsList()
    {
        BindingsList.Children.Clear();

        foreach (var (evt, label) in KnownEvents)
        {
            var current = _bindings.TryGetValue(evt, out var v) && !string.IsNullOrEmpty(v) ? v : "—";

            var row = new Border
            {
                Padding      = new Avalonia.Thickness(8, 5),
                CornerRadius = new Avalonia.CornerRadius(8),
                Background   = Brushes.Transparent,
            };
            row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(Color.Parse("#0AFFFFFF"));
            row.PointerExited  += (_, _) => row.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var labelTb = new TextBlock
            {
                Text              = label,
                Foreground        = new SolidColorBrush(Color.Parse("#A0A0A0")),
                FontSize          = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(labelTb, 0);

            if (_editMode)
            {
                var capturedEvt = evt;
                var combo = new ComboBox
                {
                    ItemsSource  = KnownCommands,
                    SelectedItem = KnownCommands.Contains(current) ? current : "—",
                    Width        = 200,
                    Background   = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
                    BorderBrush  = new SolidColorBrush(Color.Parse("#26FFFFFF")),
                    Foreground   = new SolidColorBrush(Color.Parse("#E0E0E0")),
                    FontSize     = 12,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    var selected = combo.SelectedItem as string ?? "—";
                    if (selected == "—")
                        _bindings.Remove(capturedEvt);
                    else
                        _bindings[capturedEvt] = selected;
                };
                Grid.SetColumn(combo, 1);
                grid.Children.Add(labelTb);
                grid.Children.Add(combo);
            }
            else
            {
                var chip = new Border
                {
                    Background      = new SolidColorBrush(Color.Parse("#0AFFFFFF")),
                    BorderBrush     = new SolidColorBrush(Color.Parse("#12FFFFFF")),
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius    = new Avalonia.CornerRadius(6),
                    Padding         = new Avalonia.Thickness(11, 4),
                    MinWidth        = 140,
                };
                chip.Child = new TextBlock
                {
                    Text                = current,
                    Foreground          = current == "—"
                        ? new SolidColorBrush(Color.Parse("#2e2e35"))
                        : new SolidColorBrush(Color.Parse("#4a4a50")),
                    FontSize            = 12,
                    FontFamily          = new FontFamily("Consolas,Courier New,monospace"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                Grid.SetColumn(chip, 1);
                grid.Children.Add(labelTb);
                grid.Children.Add(chip);
            }

            row.Child = grid;
            BindingsList.Children.Add(row);
        }
    }

    // ── Listener edit toggle ──────────────────────────────────────────────────

    private void ListenerEditSwitch_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _editMode = ListenerEditSwitch.IsChecked == true;

        if (_editMode)
        {
            ListenerEditStatusText.Text       = "Edit mode — changes saved with config";
            ListenerEditStatusText.Foreground = new SolidColorBrush(Color.Parse("#4ade80"));
        }
        else
        {
            ListenerEditStatusText.Text       = "IPC commands only";
            ListenerEditStatusText.Foreground = new SolidColorBrush(Color.Parse("#FF555555"));
        }

        BuildBindingsList();
    }

    // ── Drag ──────────────────────────────────────────────────────────────────

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // ── Config save ───────────────────────────────────────────────────────────

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        _vm.SaveConfig();   // model / language / log level
        SaveBindings();     // event → command bindings

        SaveButton.Content = "Saved ✓";
        DispatcherTimer.RunOnce(() => SaveButton.Content = "Save Config",
            TimeSpan.FromMilliseconds(1200));
    }

    // ── Agent control ─────────────────────────────────────────────────────────

    private async void ReloadButton_Click(object? sender, RoutedEventArgs e)
    {
        try { await _vm.ReloadConfigAsync(); }
        catch { }
    }

    private async void DoctorButton_Click(object? sender, RoutedEventArgs e)
    {
        try { await _vm.RunDoctorAsync(); }
        catch { }
    }

    private void OpenLogDirButton_Click(object? sender, RoutedEventArgs e)
        => _vm.OpenLogDir();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    private static string ResolveStatusPath(ConfigService configSvc)
    {
        try
        {
            var doc = configSvc.Load();
            if (doc["runtime"]?["status_file"]?.GetValue<string>() is { Length: > 0 } p)
                return p;
        }
        catch { }

        return AppPaths.DefaultStatusFilePath();
    }
}
