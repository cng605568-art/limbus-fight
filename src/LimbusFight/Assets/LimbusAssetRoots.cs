using System.Diagnostics;
using LimbusFight.Core;

namespace LimbusFight.Assets;

/// <summary>One place on disk where Limbus Company keeps game files.</summary>
public sealed record AssetRoot(string Kind, string Path, bool Exists);

/// <summary>
/// Finds the two places the Steam version of Limbus Company stores its files:
/// <list type="bullet">
/// <item><c>&lt;install&gt;\LimbusCompany_Data</c>, shipped by Steam.</item>
/// <item><c>%USERPROFILE%\AppData\LocalLow\Unity\ProjectMoon_LimbusCompany</c>, the files the game
/// downloads by itself after launch (most character art lives in downloaded bundles).</item>
/// </list>
/// Read-only: nothing here ever writes to these folders.
/// </summary>
public static class LimbusAssetRoots
{
    public const string GameRootKind = "Game folder";
    public const string CacheRootKind = "Download cache";

    public const string GameDataFolderName = "LimbusCompany_Data";
    public const string UnityCacheFolderName = "ProjectMoon_LimbusCompany";

    /// <summary>Process name of the running game (LimbusCompany.exe).</summary>
    public const string GameProcessName = "LimbusCompany";

    /// <summary>Optional override for the download cache, for players who moved it to another drive.</summary>
    public const string CacheOverrideEnvVar = "LIMBUSFIGHT_ASSET_CACHE";

    public static IReadOnlyList<AssetRoot> Find(SteamCheckResult steam)
    {
        var roots = new List<AssetRoot>();

        if (steam.InstallPath is string install)
        {
            string data = Path.Combine(install, GameDataFolderName);
            roots.Add(new AssetRoot(GameRootKind, data, Directory.Exists(data)));
        }

        if (FindDownloadCache() is string cache)
        {
            roots.Add(new AssetRoot(CacheRootKind, cache, Directory.Exists(cache)));
        }

        return roots;
    }

    /// <summary>Where Unity keeps the game's downloaded bundles (LocalLow\Unity\ProjectMoon_LimbusCompany).</summary>
    public static string? FindDownloadCache()
    {
        string? overridePath = Environment.GetEnvironmentVariable(CacheOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath.Trim().Trim('"'));
        }

        // LocalApplicationData = C:\Users\<name>\AppData\Local  ->  sibling folder "LocalLow".
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            return null;
        }

        string? appData = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(local));
        return appData is null ? null : Path.Combine(appData, "LocalLow", "Unity", UnityCacheFolderName);
    }

    /// <summary>
    /// True while LimbusCompany.exe is running. Limbus Fight never reads game files while the game
    /// is open, so it can't interfere with the game or its anti-tamper checks.
    /// </summary>
    public static bool IsGameRunning()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(GameProcessName);
            bool running = processes.Length > 0;
            foreach (Process process in processes)
            {
                process.Dispose();
            }

            return running;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Replaces the Windows user folder with %USERPROFILE% so reports don't include the user's name.</summary>
    public static string Redact(string path)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
        {
            return "%USERPROFILE%" + path[profile.Length..];
        }

        return path;
    }
}
