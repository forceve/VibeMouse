using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace VibeMouse.Panel.Services;

/// <summary>
/// Polls status.json every 2 seconds and fires StatusChanged when the content changes.
/// </summary>
public class StatusService : IDisposable
{
    private const string OfflineMarker = "<offline>";

    private readonly string _statusPath;
    private Timer? _timer;
    private string _lastContent = string.Empty;

    public event EventHandler<StatusSnapshot>? StatusChanged;

    public StatusService(string statusPath)
    {
        _statusPath = statusPath;
    }

    public void Start()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Stop() => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose() => _timer?.Dispose();

    private void Poll()
    {
        try
        {
            if (!File.Exists(_statusPath))
            {
                if (_lastContent == OfflineMarker)
                {
                    return;
                }

                _lastContent = OfflineMarker;
                StatusChanged?.Invoke(this, StatusSnapshot.Offline);
                return;
            }

            var content = File.ReadAllText(_statusPath);
            if (content == _lastContent) return;
            _lastContent = content;

            var node = JsonNode.Parse(content) as JsonObject;
            var snapshot = StatusSnapshot.FromJson(node);
            StatusChanged?.Invoke(this, snapshot);
        }
        catch
        {
            // Swallow read errors; the file may be mid-write
        }
    }
}

public record StatusSnapshot(
    string State,
    string ListenerMode,
    string LastTranscript,
    string? IpcSocket,
    int? IpcPort)
{
    public bool IpcAvailable => IpcSocket is not null || IpcPort is not null;

    public string ListenerState => State == "offline"
        ? "offline"
        : ListenerMode switch
        {
            "inline" => "running",
            "child" => "running",
            "off" => "disabled",
            _ => "unknown",
        };

    public static StatusSnapshot Offline { get; } =
        new("offline", "unknown", string.Empty, null, null);

    public static StatusSnapshot FromJson(JsonObject? obj)
    {
        if (obj is null) return Offline;

        return new StatusSnapshot(
            State:          obj["state"]?.GetValue<string>() ?? "unknown",
            ListenerMode:   obj["listener_mode"]?.GetValue<string>() ?? "unknown",
            LastTranscript: obj["last_transcript"]?.GetValue<string>() ?? string.Empty,
            IpcSocket:      obj["ipc_socket"]?.GetValue<string>(),
            IpcPort:        obj["ipc_port"]?.AsValue().TryGetValue<int>(out var p) == true ? p : null);
    }
}
