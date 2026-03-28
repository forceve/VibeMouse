using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using VibeMouse.Panel.Services;

namespace VibeMouse.Panel.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ConfigService _configService;
    private readonly StatusService _statusService;
    private readonly AgentControlService _agentControl;

    // ── Status (read-only, polled) ──────────────────────────────────────────

    private string _agentState = "offline";
    public string AgentState
    {
        get => _agentState;
        private set { _agentState = value; OnPropertyChanged(); }
    }

    private string _listenerMode = "unknown";
    public string ListenerMode
    {
        get => _listenerMode;
        private set { _listenerMode = value; OnPropertyChanged(); }
    }

    private string _listenerState = "offline";
    public string ListenerState
    {
        get => _listenerState;
        private set { _listenerState = value; OnPropertyChanged(); }
    }

    private string _lastTranscript = string.Empty;
    public string LastTranscript
    {
        get => _lastTranscript;
        private set { _lastTranscript = value; OnPropertyChanged(); }
    }

    private bool _ipcAvailable;
    public bool IpcAvailable
    {
        get => _ipcAvailable;
        private set { _ipcAvailable = value; OnPropertyChanged(); }
    }

    // ── Config (editable) ──────────────────────────────────────────────────

    private string _model = string.Empty;
    public string Model
    {
        get => _model;
        set { _model = value; OnPropertyChanged(); }
    }

    private string _language = string.Empty;
    public string Language
    {
        get => _language;
        set { _language = value; OnPropertyChanged(); }
    }

    private string _hotkey = string.Empty;
    public string Hotkey
    {
        get => _hotkey;
        set { _hotkey = value; OnPropertyChanged(); }
    }

    private string _logLevel = "INFO";
    public string LogLevel
    {
        get => _logLevel;
        set { _logLevel = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> LogLevelOptions { get; } =
        ["DEBUG", "INFO", "WARNING", "ERROR"];

    public IReadOnlyList<string> ModelOptions { get; } =
        ["funasr_onnx", "funasr", "sense_voice"];

    // ── Internals ──────────────────────────────────────────────────────────

    private JsonObject _configDocument = [];

    public MainViewModel(ConfigService configService,
                         StatusService statusService,
                         AgentControlService agentControl,
                         bool startStatusPolling = true)
    {
        _configService   = configService;
        _statusService   = statusService;
        _agentControl    = agentControl;

        _statusService.StatusChanged += OnStatusChanged;
        if (startStatusPolling)
        {
            _statusService.Start();
        }

        LoadConfig();
    }

    private void OnStatusChanged(object? sender, StatusSnapshot snapshot)
        => ApplyStatusSnapshot(snapshot);

    internal void ApplyStatusSnapshot(StatusSnapshot snapshot)
    {
        AgentState    = snapshot.State;
        ListenerState = snapshot.ListenerState;
        ListenerMode  = snapshot.ListenerMode;
        IpcAvailable  = snapshot.IpcAvailable;
        var raw = snapshot.LastTranscript;
        LastTranscript = raw.Length > 60 ? raw[..57] + "..." : raw;
    }

    // ── Config ─────────────────────────────────────────────────────────────

    private void LoadConfig()
    {
        _configDocument = _configService.Load();

        var transcriber = _configDocument["transcriber"] as JsonObject;
        Model    = transcriber?["backend"]?.GetValue<string>() ?? "funasr_onnx";
        Language = transcriber?["language"]?.GetValue<string>() ?? string.Empty;

        var logs = _configDocument["logs"] as JsonObject;
        LogLevel = (logs?["level"]?.GetValue<string>() ?? "INFO").ToUpperInvariant();

        // Hotkey: stored as record_hotkey_keycodes list — show as read-only text
        var input = _configDocument["input"] as JsonObject;
        if (input?["record_hotkey_keycodes"] is JsonArray codes)
            Hotkey = string.Join("+", codes);
        else
            Hotkey = string.Empty;
    }

    /// <summary>Saves editable fields back to config.json without auto-save.</summary>
    public void SaveConfig()
    {
        // Ensure nested objects exist
        if (_configDocument["transcriber"] is not JsonObject transcriber)
        {
            transcriber = [];
            _configDocument["transcriber"] = transcriber;
        }

        transcriber["backend"]  = Model;
        if (!string.IsNullOrWhiteSpace(Language))
            transcriber["language"] = Language;

        if (_configDocument["logs"] is not JsonObject logs)
        {
            logs = [];
            _configDocument["logs"] = logs;
        }
        logs["level"] = LogLevel.ToLowerInvariant();

        _configService.Save(_configDocument);
    }

    // ── Agent control ──────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task ReloadConfigAsync()
    {
        if (!IpcAvailable) return;
        await _agentControl.SendCommandAsync("reload_config");
    }

    public async System.Threading.Tasks.Task RunDoctorAsync()
    {
        await _agentControl.RunDoctorAsync();
    }

    public void OpenLogDir() => _agentControl.OpenLogDir();

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _statusService.StatusChanged -= OnStatusChanged;
        _statusService.Dispose();
    }
}
