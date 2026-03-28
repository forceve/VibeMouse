using System;
using System.IO;

namespace VibeMouse.Panel.Services;

internal static class AppPaths
{
    private const string WindowsLauncherFileName = "vibemouse-launch.ps1";
    private const string DeployEnvFileName = "deploy.env";
    private const string LogsDirectoryName = "logs";

    internal static string DefaultStatusFilePath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(runtimeDir))
        {
            return Path.Combine(runtimeDir, "vibemouse-status.json");
        }

        return Path.Combine(Path.GetTempPath(), "vibemouse-status.json");
    }

    internal static string DefaultLogDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            var baseDir = !string.IsNullOrWhiteSpace(localAppData)
                ? localAppData
                : Path.Combine(home, "AppData", "Local");
            return Path.Combine(baseDir, "VibeMouse");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Logs", "VibeMouse");
        }

        return Path.Combine(home, ".local", "state", "vibemouse");
    }

    internal static string ResolveLogDirectory(string? configPath, string? statusPath)
    {
        foreach (var candidate in EnumerateLogDirectoryCandidates(configPath, statusPath))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            if (LooksLikeDeployRoot(Path.GetDirectoryName(candidate)))
            {
                return candidate;
            }
        }

        return DefaultLogDirectory();
    }

    private static IEnumerable<string> EnumerateLogDirectoryCandidates(
        string? configPath,
        string? statusPath)
    {
        if (!string.IsNullOrWhiteSpace(statusPath))
        {
            foreach (var candidate in CandidateDirectoriesFromStatusPath(statusPath))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            foreach (var candidate in CandidateDirectoriesFromConfigPath(configPath))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> CandidateDirectoriesFromStatusPath(string statusPath)
    {
        var statusDir = SafeGetDirectoryName(statusPath);
        if (string.IsNullOrWhiteSpace(statusDir))
        {
            yield break;
        }

        if (string.Equals(Path.GetFileName(statusDir), "state", StringComparison.OrdinalIgnoreCase))
        {
            var rootDir = SafeGetDirectoryName(statusDir);
            if (!string.IsNullOrWhiteSpace(rootDir))
            {
                yield return Path.Combine(rootDir, LogsDirectoryName);
            }
        }
    }

    private static IEnumerable<string> CandidateDirectoriesFromConfigPath(string configPath)
    {
        var configDir = SafeGetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDir))
        {
            yield break;
        }

        yield return Path.Combine(configDir, LogsDirectoryName);
    }

    private static bool LooksLikeDeployRoot(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        return File.Exists(Path.Combine(directory, WindowsLauncherFileName))
            || File.Exists(Path.Combine(directory, DeployEnvFileName));
    }

    private static string? SafeGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch
        {
            return null;
        }
    }
}
