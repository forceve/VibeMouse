using System;
using System.IO;
using VibeMouse.Panel.Services;
using Xunit;

namespace VibeMouse.Panel.Tests;

public class ConfigServiceTests
{
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
