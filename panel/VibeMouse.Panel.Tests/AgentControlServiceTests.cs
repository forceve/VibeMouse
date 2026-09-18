using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VibeMouse.Panel.Services;
using Xunit;

namespace VibeMouse.Panel.Tests;

public class AgentControlServiceTests
{
    [Fact]
    public void CreateCommandFrame_UsesLpJsonEnvelope()
    {
        var frame = AgentControlService.CreateCommandFrame("reload_config");

        var length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, sizeof(int)));
        Assert.Equal(frame.Length - sizeof(int), length);

        using var document = JsonDocument.Parse(frame.AsMemory(sizeof(int), length));
        var root = document.RootElement;
        Assert.Equal("command", root.GetProperty("type").GetString());
        Assert.Equal("reload_config", root.GetProperty("command").GetString());
    }

    [Fact]
    public void DefaultLogDirectory_MatchesPlatformDefaults()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetEnvironmentVariable("LOCALAPPDATA")
                    ?? Path.Combine(home, "AppData", "Local"),
                "VibeMouse"
            )
            : OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Logs", "VibeMouse")
                : Path.Combine(home, ".local", "state", "vibemouse");

        Assert.Equal(expected, AgentControlService.DefaultLogDirectory());
    }

    [Fact]
    public void DefaultStatusFilePath_UsesRuntimeDirectoryOrTemp()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var expectedBase = !string.IsNullOrWhiteSpace(runtimeDir)
            ? runtimeDir
            : Path.GetTempPath();
        var expected = Path.Combine(expectedBase, "vibemouse-status.json");

        Assert.Equal(expected, AppPaths.DefaultStatusFilePath());
    }

    [Fact]
    public void IsWindowsNamedPipe_RecognizesCurrentPlatformPipePaths()
    {
        var path = @"\\.\pipe\vibemouse";
        var expected = OperatingSystem.IsWindows();
        Assert.Equal(expected, AgentControlService.IsWindowsNamedPipe(path));
    }

    [Fact]
    public async Task RunDoctorAsync_SendsDoctorCommandOverIpc()
    {
        var service = new RecordingAgentControlService(
            new StatusService(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"))
        );

        await service.RunDoctorAsync();

        Assert.Equal("doctor", service.LastCommand);
    }

    [Fact]
    public void CreateOpenDirectoryStartInfo_UsesCurrentPlatformFileManager()
    {
        var directoryPath = Path.GetTempPath();

        var startInfo = AgentControlService.CreateOpenDirectoryStartInfo(directoryPath);

        var expectedCommand = OperatingSystem.IsWindows()
            ? "explorer.exe"
            : OperatingSystem.IsMacOS()
                ? "open"
                : "xdg-open";

        Assert.Equal(expectedCommand, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Single(startInfo.ArgumentList);
        Assert.Equal(directoryPath, startInfo.ArgumentList[0]);
    }

    private sealed class RecordingAgentControlService(StatusService statusService)
        : AgentControlService(statusService)
    {
        public string? LastCommand { get; private set; }

        public override Task SendCommandAsync(string command)
        {
            LastCommand = command;
            return Task.CompletedTask;
        }
    }
}
