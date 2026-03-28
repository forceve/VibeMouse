using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
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
}
