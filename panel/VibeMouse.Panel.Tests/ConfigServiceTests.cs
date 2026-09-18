using System;
using System.IO;
using VibeMouse.Panel.Services;
using Xunit;

namespace VibeMouse.Panel.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Save_CreatesMissingDirectoryAndReplacesExistingConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vibemouse-save-{Guid.NewGuid():N}");
        try
        {
            var service = new ConfigService(Path.Combine(directory, "nested", "config.json"));
            service.Save(new System.Text.Json.Nodes.JsonObject { ["value"] = 1 });
            service.Save(new System.Text.Json.Nodes.JsonObject { ["value"] = 2 });
            Assert.Equal(2, service.Load()["value"]!.GetValue<int>());
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "nested")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DefaultConfigPath_MirrorsAgentPlatformRules()
    {
        var originalXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var injectedXdgConfig = Path.Combine(
            Path.GetTempPath(),
            $"vibemouse-panel-xdg-{Guid.NewGuid():N}"
        );

        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", injectedXdgConfig);

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var expected = OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetEnvironmentVariable("APPDATA")
                        ?? Path.Combine(home, "AppData", "Roaming"),
                    "vibemouse",
                    "config.json"
                )
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(
                        home,
                        "Library",
                        "Application Support",
                        "vibemouse",
                        "config.json"
                    )
                    : Path.Combine(injectedXdgConfig, "vibemouse", "config.json");

            Assert.Equal(expected, ConfigService.DefaultConfigPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", originalXdgConfig);
        }
    }
}
