using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VibeMouse.Panel.Services;

/// <summary>
/// Reads and writes the VibeMouse config.json file.
/// </summary>
public class ConfigService
{
    private readonly string _configPath;

    public ConfigService(string configPath)
    {
        _configPath = configPath;
    }

    public string ConfigPath => _configPath;

    /// <summary>
    /// Loads the config document. Returns an empty object if the file does not exist.
    /// </summary>
    public JsonObject Load()
    {
        if (!File.Exists(_configPath))
            return [];

        var json = File.ReadAllText(_configPath);
        return JsonNode.Parse(json) as JsonObject ?? [];
    }

    /// <summary>
    /// Persists a config document back to disk.
    /// </summary>
    public void Save(JsonObject document)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = document.ToJsonString(options);
        File.WriteAllText(_configPath, json);
    }

    /// <summary>
    /// Returns the default config file path for the current user.
    /// Mirrors the Python agent's config discovery logic.
    /// </summary>
    public static string DefaultConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            var baseDir = !string.IsNullOrEmpty(appData)
                ? appData
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                               "AppData", "Roaming");
            return Path.Combine(baseDir, "vibemouse", "config.json");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "vibemouse", "config.json");
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig))
            return Path.Combine(xdgConfig, "vibemouse", "config.json");

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, ".config", "vibemouse", "config.json");
    }

    /// <summary>
    /// Returns the default status.json path for the current platform.
    /// Mirrors the Python agent's runtime status discovery logic.
    /// </summary>
    public static string DefaultStatusPath()
        => AppPaths.DefaultStatusFilePath();
}
