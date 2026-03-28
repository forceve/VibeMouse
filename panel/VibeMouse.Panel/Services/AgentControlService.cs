using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace VibeMouse.Panel.Services;

/// <summary>
/// Sends runtime control commands to the VibeMouse agent via the local IPC channel.
/// </summary>
public class AgentControlService
{
    private readonly StatusService _statusService;
    private readonly string? _logDirectory;
    private StatusSnapshot _lastStatus = StatusSnapshot.Offline;

    public AgentControlService(StatusService statusService, string? logDirectory = null)
    {
        _statusService = statusService;
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory;
        _statusService.StatusChanged += (_, s) => _lastStatus = s;
    }

    public bool IsAvailable => _lastStatus.IpcAvailable;

    /// <summary>
    /// Sends a named command through the IPC channel.
    /// </summary>
    public virtual async Task SendCommandAsync(string command)
    {
        var frame = CreateCommandFrame(command);

        if (_lastStatus.IpcSocket is { } socketPath)
        {
            if (IsWindowsNamedPipe(socketPath))
            {
                await SendViaNamedPipeAsync(socketPath, frame);
                return;
            }

            await SendViaUnixSocketAsync(socketPath, frame);
            return;
        }

        if (_lastStatus.IpcPort is { } port)
        {
            throw new InvalidOperationException(
                $"Unsupported legacy IPC port endpoint: {port}. Expected ipc_socket."
            );
        }

        throw new InvalidOperationException("IPC is not available.");
    }

    public virtual async Task RunDoctorAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "vibemouse",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("doctor");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start 'vibemouse doctor'.");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'vibemouse doctor' failed with exit code {process.ExitCode}."
            );
        }
    }

    internal static byte[] CreateCommandFrame(string command)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "command",
            command,
        });
        var frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, sizeof(int)), body.Length);
        body.CopyTo(frame.AsSpan(sizeof(int)));
        return frame;
    }

    internal static bool IsWindowsNamedPipe(string path) =>
        OperatingSystem.IsWindows()
        && path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase);

    internal static string DefaultLogDirectory() => AppPaths.DefaultLogDirectory();

    private static async Task SendViaUnixSocketAsync(string socketPath, byte[] frame)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static async Task SendViaNamedPipeAsync(string pipePath, byte[] frame)
    {
        var pipeName = pipePath.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
            ? pipePath[@"\\.\pipe\".Length..]
            : pipePath;

        await using var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous
        );
        await stream.ConnectAsync(2000);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    /// <summary>Opens the log directory in the system file manager.</summary>
    public virtual void OpenLogDir()
    {
        var logDir = _logDirectory ?? DefaultLogDirectory();
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{logDir}\"",
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }
}
