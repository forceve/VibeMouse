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
    private const string NoBinding = "None";

    private static readonly (string Event, string Label)[] KnownEvents =
    [
        ("mouse.side_front.press", "Side Front Button"),
        ("mouse.side_rear.press", "Side Rear Button"),
        ("hotkey.record_toggle", "Record Hotkey"),
        ("hotkey.recording_submit", "Submit Hotkey"),
        ("gesture.up", "Gesture Up"),
        ("gesture.down", "Gesture Down"),
        ("gesture.left", "Gesture Left"),
        ("gesture.right", "Gesture Right"),
    ];

    private static readonly string[] KnownCommands =
    [
        NoBinding,
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

    private readonly Dictionary<string, string> _bindings = new();
    private readonly MainViewModel _vm;
    private readonly ConfigService _configService;

    private bool _editMode;

    public MainWindow()
    {
        InitializeComponent();

        var configPath = ConfigService.DefaultConfigPath();
        _configService = new ConfigService(configPath);

        var statusPath = ResolveStatusPath(_configService);
        var statusService = new StatusService(statusPath);
        var logDirectory = AppPaths.ResolveLogDirectory(_configService.ConfigPath, statusPath);
        var agentControl = new AgentControlService(statusService, logDirectory);

        _vm = new MainViewModel(_configService, statusService, agentControl);
        _vm.PropertyChanged += (_, e) => Dispatcher.UIThread.Post(() => SyncUi(e.PropertyName));

        InitializeControls();
        LoadBindings();
        BuildBindingsList();
        SyncUi(null);
    }

    private void InitializeControls()
    {
        ModelCombo.ItemsSource = _vm.ModelOptions;
        ModelCombo.SelectedItem = _vm.Model;
        ModelCombo.SelectionChanged += (_, _) =>
            _vm.Model = ModelCombo.SelectedItem as string ?? _vm.Model;

        LogLevelCombo.ItemsSource = _vm.LogLevelOptions;
        LogLevelCombo.SelectedItem = _vm.LogLevel;
        LogLevelCombo.SelectionChanged += (_, _) =>
            _vm.LogLevel = LogLevelCombo.SelectedItem as string ?? _vm.LogLevel;

        LanguageBox.Text = _vm.Language;
        LanguageBox.TextChanged += (_, _) => _vm.Language = LanguageBox.Text ?? string.Empty;

        HotkeyBox.Text = _vm.Hotkey;
    }

    private void SyncUi(string? propertyName)
    {
        if (propertyName is null or nameof(MainViewModel.AgentState))
        {
            AgentStateText.Text = _vm.AgentState;
            AgentStateText.Foreground = _vm.AgentState is "idle" or "recording" or "processing"
                ? SolidColorBrush.Parse("#4ade80")
                : SolidColorBrush.Parse("#888888");
        }

        if (propertyName is null or nameof(MainViewModel.ListenerState))
        {
            ListenerStateText.Text = _vm.ListenerState;
            ListenerStateText.Foreground = _vm.ListenerState switch
            {
                "running" => SolidColorBrush.Parse("#4ade80"),
                "disabled" => SolidColorBrush.Parse("#f59e0b"),
                _ => SolidColorBrush.Parse("#888888"),
            };
        }

        if (propertyName is null or nameof(MainViewModel.ListenerMode))
        {
            ListenerModeText.Text = _vm.ListenerMode;
        }

        if (propertyName is null or nameof(MainViewModel.LastTranscript))
        {
            var transcript = _vm.LastTranscript;
            LastTranscriptText.Text = string.IsNullOrWhiteSpace(transcript) ? "-" : transcript;
        }

        if (propertyName is null or nameof(MainViewModel.IpcAvailable))
        {
            ReloadButton.IsEnabled = _vm.IpcAvailable;
            DoctorButton.IsEnabled = true;
            IpcHintText.IsVisible = !_vm.IpcAvailable;
        }
    }

    private void LoadBindings()
    {
        _bindings.Clear();
        try
        {
            var doc = _configService.Load();
            if (doc["bindings"] is JsonObject bindings)
            {
                foreach (var (key, value) in bindings)
                {
                    _bindings[key] = value?.GetValue<string>() ?? string.Empty;
                }
            }
        }
        catch
        {
        }
    }

    private void SaveBindings()
    {
        try
        {
            var doc = _configService.Load();
            var bindings = new JsonObject();
            foreach (var (key, value) in _bindings)
            {
                if (!string.IsNullOrEmpty(value) && value != NoBinding)
                {
                    bindings[key] = value;
                }
            }

            doc["bindings"] = bindings;
            _configService.Save(doc);
        }
        catch
        {
        }
    }

    private void BuildBindingsList()
    {
        BindingsList.Children.Clear();

        foreach (var (eventName, label) in KnownEvents)
        {
            var current = _bindings.TryGetValue(eventName, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : NoBinding;

            var row = new Border
            {
                Padding = new Avalonia.Thickness(8, 5),
                CornerRadius = new Avalonia.CornerRadius(8),
                Background = Brushes.Transparent,
            };
            row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(Color.Parse("#0AFFFFFF"));
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var labelText = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.Parse("#A0A0A0")),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(labelText, 0);
            grid.Children.Add(labelText);

            if (_editMode)
            {
                var capturedEvent = eventName;
                var combo = new ComboBox
                {
                    ItemsSource = KnownCommands,
                    SelectedItem = KnownCommands.Contains(current) ? current : NoBinding,
                    Width = 200,
                    Background = new SolidColorBrush(Color.Parse("#1AFFFFFF")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")),
                    Foreground = new SolidColorBrush(Color.Parse("#E0E0E0")),
                    FontSize = 12,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    var selected = combo.SelectedItem as string ?? NoBinding;
                    if (selected == NoBinding)
                    {
                        _bindings.Remove(capturedEvent);
                    }
                    else
                    {
                        _bindings[capturedEvent] = selected;
                    }
                };
                Grid.SetColumn(combo, 1);
                grid.Children.Add(combo);
            }
            else
            {
                var chip = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#0AFFFFFF")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#12FFFFFF")),
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Padding = new Avalonia.Thickness(11, 4),
                    MinWidth = 140,
                };
                chip.Child = new TextBlock
                {
                    Text = current,
                    Foreground = current == NoBinding
                        ? new SolidColorBrush(Color.Parse("#2e2e35"))
                        : new SolidColorBrush(Color.Parse("#4a4a50")),
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                Grid.SetColumn(chip, 1);
                grid.Children.Add(chip);
            }

            row.Child = grid;
            BindingsList.Children.Add(row);
        }
    }

    private void BindingsEditSwitch_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _editMode = BindingsEditSwitch.IsChecked == true;

        if (_editMode)
        {
            ListenerEditStatusText.Text = "Edit mode active. Save config to apply.";
            ListenerEditStatusText.Foreground = new SolidColorBrush(Color.Parse("#4ade80"));
        }
        else
        {
            ListenerEditStatusText.Text = "Read-only mode";
            ListenerEditStatusText.Foreground = new SolidColorBrush(Color.Parse("#888888"));
        }

        BuildBindingsList();
    }

    private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Card_PointerPressed(sender, e);
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        _vm.SaveConfig();
        SaveBindings();

        SaveButton.Content = "Saved";
        DispatcherTimer.RunOnce(() => SaveButton.Content = "Save Config",
            TimeSpan.FromMilliseconds(1200));
    }

    private async void ReloadButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.ReloadConfigAsync();
        }
        catch
        {
        }
    }

    private async void DoctorButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _vm.RunDoctorAsync();
        }
        catch
        {
        }
    }

    private void OpenLogDirButton_Click(object? sender, RoutedEventArgs e)
        => _vm.OpenLogDir();

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
            if (doc["runtime"]?["status_file"]?.GetValue<string>() is { Length: > 0 } path)
            {
                return path;
            }
        }
        catch
        {
        }

        return ConfigService.DefaultStatusPath();
    }
}
