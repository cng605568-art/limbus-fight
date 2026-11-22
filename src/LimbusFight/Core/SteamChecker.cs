using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace LimbusFight.Core;

/// <summary>
/// Locates the local Steam install of Limbus Company (App ID 1973530) and reports whether
/// it is present and fully updated. Read-only: it never touches game files, only Steam's
/// registry keys, libraryfolders.vdf and appmanifest_1973530.acf.
/// </summary>
public static class SteamChecker
{
    /// <summary>Limbus Company's Steam App ID.</summary>
    public const int LIMBUS_APP_ID = 1973530;

    // =========================================================================================
    //  VERSION GATE CONFIGURATION
    // =========================================================================================

    /// <summary>
    /// The Limbus Company "buildid" that Limbus Fight's asset pipeline was verified against.
    /// HOW TO SET IT: run Limbus Fight, read the "buildid" shown in the status panel (or run
    /// <c>LimbusFight.exe --selftest</c>), and paste that number here.
    /// 0 = placeholder / not configured: the build comparison is skipped and only StateFlags counts.
    /// </summary>
    public const long TARGET_BUILD_ID = 0;

    /// <summary>
    /// true  = EARLY TESTING MODE. Never block gameplay; the status is only displayed.
    /// false = ENFORCED. Gameplay is replaced by the red warning screen whenever Limbus Company is
    ///         missing, updating, not fully installed (StateFlags != 4) or older than TARGET_BUILD_ID.
    /// >>> Flip this to false once TARGET_BUILD_ID is set and the asset extractor exists. <<<
    /// </summary>
    public const bool BYPASS_VERSION_CHECK = true;

    /// <summary>
    /// Optional manual override: set this environment variable to a Steam folder (the one that
    /// contains "steamapps") to skip registry detection. Handy for portable Steam installs.
    /// </summary>
    public const string SteamPathOverrideEnvVar = "LIMBUSFIGHT_STEAM_PATH";

    /// <summary>The only StateFlags value that means "fully installed and up to date".</summary>
    public const long FullyInstalledStateFlags = (long)AppStateFlags.FullyInstalled;

    private const AppStateFlags UpdateInProgressFlags =
        AppStateFlags.UpdateRunning | AppStateFlags.UpdateStarted | AppStateFlags.Downloading |
        AppStateFlags.Staging | AppStateFlags.Committing | AppStateFlags.Preallocating |
        AppStateFlags.AddingFiles | AppStateFlags.Validating | AppStateFlags.Reconfiguring |
        AppStateFlags.UpdateStopping;

    private const AppStateFlags UpdateNeededFlags = AppStateFlags.UpdateRequired | AppStateFlags.UpdatePaused;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Scans for Limbus Company using the constants above.</summary>
    public static SteamCheckResult Check() => Check(LIMBUS_APP_ID, TARGET_BUILD_ID, BYPASS_VERSION_CHECK);

    /// <summary>Scans for any Steam app. Never throws; failures are reported in the result.</summary>
    public static SteamCheckResult Check(int appId, long targetBuildId, bool bypassVersionCheck)
    {
        var log = new List<string>();

        try
        {
            return CheckCore(appId, targetBuildId, bypassVersionCheck, log);
        }
        catch (Exception ex)
        {
            log.Add($"Unexpected error during Steam scan: {ex.GetType().Name}: {ex.Message}");
            return new SteamCheckResult
            {
                Status = SteamGameStatus.SteamNotFound,
                Summary = $"Steam scan failed: {ex.Message}",
                AppId = appId,
                TargetBuildId = targetBuildId,
                BypassVersionCheck = bypassVersionCheck,
                Diagnostics = log,
            };
        }
    }

    private static SteamCheckResult CheckCore(int appId, long targetBuildId, bool bypass, List<string> log)
    {
        // ---- 1. Steam root ----------------------------------------------------------------
        string? steamPath = FindSteamRoot(log);

        if (steamPath is null)
        {
            return new SteamCheckResult
            {
                Status = SteamGameStatus.SteamNotFound,
                Summary = "Steam is not installed (no registry entry or Steam folder found).",
                AppId = appId,
                TargetBuildId = targetBuildId,
                BypassVersionCheck = bypass,
                Diagnostics = log,
            };
        }

        // ---- 2. Library folders (C:\, D:\SteamLibrary, E:\...) ------------------------------
        List<string> libraries = FindLibraryFolders(steamPath, appId, log);

        // ---- 3. appmanifest_<appid>.acf ---------------------------------------------------
        var manifests = new List<(string Library, string Manifest)>();
        foreach (string library in libraries)
        {
            string? manifest = FindManifestInLibrary(library, appId);
            if (manifest is not null)
            {
                manifests.Add((library, manifest));
                log.Add($"Manifest found: {manifest}");
            }
        }

        if (manifests.Count == 0)
        {
            log.Add($"appmanifest_{appId}.acf is not present in any of the {libraries.Count} library folder(s).");
            return new SteamCheckResult
            {
                Status = SteamGameStatus.GameNotInstalled,
                Summary = "Limbus Company is not installed in any Steam library.",
                AppId = appId,
                SteamPath = steamPath,
                LibraryFolders = libraries,
                TargetBuildId = targetBuildId,
                BypassVersionCheck = bypass,
                Diagnostics = log,
            };
        }

        if (manifests.Count > 1)
        {
            log.Add($"{manifests.Count} manifests found; using the first (libraries that list the app come first).");
        }

        (string libraryPath, string manifestPath) = manifests[0];

        // ---- 4. Parse the manifest ------------------------------------------------------------
        KvNode? appState = ReadAppState(manifestPath, log);

        if (appState is null)
        {
            return new SteamCheckResult
            {
                Status = SteamGameStatus.ManifestUnreadable,
                Summary = "The Limbus Company app manifest could not be read.",
                AppId = appId,
                SteamPath = steamPath,
                LibraryFolders = libraries,
                LibraryPath = libraryPath,
                ManifestPath = manifestPath,
                TargetBuildId = targetBuildId,
                BypassVersionCheck = bypass,
                Diagnostics = log,
            };
        }

        long? manifestAppId = appState.GetInt64("appid");
        if (manifestAppId is long id && id != appId)
        {
            log.Add($"Warning: manifest reports appid {id}, expected {appId}.");
        }

        string? appName = appState.GetString("name");
        string? installDir = appState.GetString("installdir");
        long? stateFlags = appState.GetInt64("StateFlags");
        long? buildId = appState.GetInt64("buildid");
        long? steamTargetBuild = appState.GetInt64("TargetBuildID");
        long bytesToDownload = appState.GetInt64("BytesToDownload") ?? 0;
        long bytesDownloaded = appState.GetInt64("BytesDownloaded") ?? 0;
        long? lastUpdatedUnix = appState.GetInt64("LastUpdated");

        string? installPath = string.IsNullOrWhiteSpace(installDir)
            ? null
            : Path.Combine(libraryPath, "steamapps", "common", installDir);
        bool installExists = installPath is not null && Directory.Exists(installPath);

        log.Add($"StateFlags={stateFlags?.ToString(CultureInfo.InvariantCulture) ?? "missing"}, " +
                $"buildid={buildId?.ToString(CultureInfo.InvariantCulture) ?? "missing"}, " +
                $"installdir=\"{installDir}\" (exists: {installExists})");

        // ---- 5. Verdict ------------------------------------------------------------------------
        (SteamGameStatus status, string summary) =
            Evaluate(stateFlags, buildId, targetBuildId, installPath, installExists);

        log.Add($"Verdict: {status} - {summary}");

        return new SteamCheckResult
        {
            Status = status,
            Summary = summary,
            AppId = appId,
            SteamPath = steamPath,
            LibraryFolders = libraries,
            LibraryPath = libraryPath,
            ManifestPath = manifestPath,
            InstallPath = installPath,
            InstallDirectoryExists = installExists,
            AppName = appName,
            StateFlags = stateFlags,
            BuildId = buildId,
            SteamPendingBuildId = steamTargetBuild is long t && t > 0 && t != buildId ? t : null,
            BytesToDownload = bytesToDownload,
            BytesDownloaded = bytesDownloaded,
            LastUpdated = lastUpdatedUnix is long unix && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix) : null,
            TargetBuildId = targetBuildId,
            BypassVersionCheck = bypass,
            Diagnostics = log,
        };
    }

    /// <summary>Pure decision logic: StateFlags + buildid -> status. Public so it can be unit tested later.</summary>
    public static (SteamGameStatus Status, string Summary) Evaluate(
        long? stateFlags, long? buildId, long targetBuildId, string? installPath, bool installExists)
    {
        if (stateFlags is null)
        {
            return (SteamGameStatus.ManifestUnreadable, "The app manifest has no \"StateFlags\" entry.");
        }

        long raw = stateFlags.Value;
        var flags = (AppStateFlags)raw;

        if (raw != FullyInstalledStateFlags)
        {
            if ((flags & UpdateInProgressFlags) != 0)
            {
                return (SteamGameStatus.Updating,
                    $"Steam is updating Limbus Company right now (StateFlags {raw}: {SteamCheckResult.DecodeFlags(raw)}).");
            }

            if ((flags & UpdateNeededFlags) != 0)
            {
                return (SteamGameStatus.UpdateRequired,
                    $"Steam reports an update is required (StateFlags {raw}: {SteamCheckResult.DecodeFlags(raw)}).");
            }

            return (SteamGameStatus.InstallNotReady,
                $"Install is not ready (StateFlags {raw}: {SteamCheckResult.DecodeFlags(raw)}; expected 4).");
        }

        if (installPath is null || !installExists)
        {
            return (SteamGameStatus.InstallNotReady,
                "Steam lists the game as installed, but its folder is missing. Verify integrity of game files in Steam.");
        }

        if (buildId is null)
        {
            return (SteamGameStatus.ManifestUnreadable, "The app manifest has no \"buildid\" entry.");
        }

        if (targetBuildId <= 0)
        {
            return (SteamGameStatus.Ready,
                $"Fully installed (StateFlags 4). buildid {buildId}; TARGET_BUILD_ID not set, build comparison skipped.");
        }

        if (buildId.Value < targetBuildId)
        {
            return (SteamGameStatus.UpdateRequired,
                $"Installed buildid {buildId} is older than the required build {targetBuildId}.");
        }

        if (buildId.Value > targetBuildId)
        {
            return (SteamGameStatus.ReadyNewerBuild,
                $"Installed buildid {buildId} is newer than verified build {targetBuildId}; Limbus Fight may need an update.");
        }

        return (SteamGameStatus.Ready, $"Fully installed and up to date (StateFlags 4, buildid {buildId}).");
    }

    /// <summary>Multi-line plain-text report, used by --selftest.</summary>
    public static string Describe(SteamCheckResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Status            : {r.Status}");
        sb.AppendLine($"  Summary           : {r.Summary}");
        sb.AppendLine($"  Steam path        : {r.SteamPath ?? "not found"}");
        sb.AppendLine($"  Libraries scanned : {r.LibraryFolders.Count}");
        foreach (string lib in r.LibraryFolders)
        {
            sb.AppendLine($"                      - {lib}");
        }

        sb.AppendLine($"  Game              : {(r.GameFound ? "FOUND" : "MISSING")}{(r.AppName is null ? string.Empty : $" ({r.AppName})")}");
        sb.AppendLine($"  Manifest          : {r.ManifestPath ?? "n/a"}");
        sb.AppendLine($"  Install path      : {r.InstallPath ?? "n/a"}{(r.InstallPath is not null && !r.InstallDirectoryExists ? "  [FOLDER MISSING]" : string.Empty)}");
        sb.AppendLine($"  StateFlags        : {r.StateFlagsDisplay}");
        sb.AppendLine($"  buildid           : {r.BuildIdDisplay}");
        sb.AppendLine($"  TARGET_BUILD_ID   : {r.TargetBuildDisplay}");
        if (r.SteamPendingBuildId is long pending)
        {
            sb.AppendLine($"  Steam pending build: {pending}");
        }

        sb.AppendLine($"  Update required   : {r.UpdateRequired}");
        sb.AppendLine($"  Bypass active     : {r.BypassVersionCheck}");
        sb.AppendLine($"  Would block play  : {r.ShouldBlockGameplay}");
        sb.AppendLine("  Scan log:");
        foreach (string line in r.Diagnostics)
        {
            sb.AppendLine($"    * {line}");
        }

        return sb.ToString();
    }

    // =========================================================================================
    //  Steam root discovery
    // =========================================================================================

    private static string? FindSteamRoot(List<string> log)
    {
        foreach ((string source, string? rawPath) in EnumerateSteamRootCandidates())
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                log.Add($"{source}: not present");
                continue;
            }

            string? path = NormalizePath(rawPath);
            if (path is null)
            {
                log.Add($"{source}: invalid path \"{rawPath}\"");
                continue;
            }

            if (Directory.Exists(Path.Combine(path, "steamapps")) || Directory.Exists(Path.Combine(path, "SteamApps")))
            {
                log.Add($"{source}: {path}  <- using this Steam install");
                return path;
            }

            log.Add($"{source}: {path} (no steamapps folder, skipped)");
        }

        return null;
    }

    private static IEnumerable<(string Source, string? Path)> EnumerateSteamRootCandidates()
    {
        yield return ($"Env {SteamPathOverrideEnvVar}", Environment.GetEnvironmentVariable(SteamPathOverrideEnvVar));

        if (OperatingSystem.IsWindows())
        {
            yield return (@"HKCU\Software\Valve\Steam [SteamPath]",
                ReadRegistryString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", RegistryView.Default));

            yield return (@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam [InstallPath]",
                ReadRegistryString(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", RegistryView.Registry64));

            yield return (@"HKLM\SOFTWARE\Valve\Steam [InstallPath]",
                ReadRegistryString(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryView.Default));

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrEmpty(programFilesX86))
            {
                yield return ("Default %ProgramFiles(x86)%\\Steam", Path.Combine(programFilesX86, "Steam"));
            }

            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return ("Default %ProgramFiles%\\Steam", Path.Combine(programFiles, "Steam"));
            }
        }
        else
        {
            // Non-Windows dev machines (dotnet run on Linux / Steam Deck desktop mode).
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return ("~/.steam/steam", Path.Combine(home, ".steam", "steam"));
            yield return ("~/.local/share/Steam", Path.Combine(home, ".local", "share", "Steam"));
            yield return ("Flatpak Steam", Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryString(RegistryHive hive, string subKey, string valueName, RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // =========================================================================================
    //  libraryfolders.vdf
    // =========================================================================================

    private static List<string> FindLibraryFolders(string steamPath, int appId, List<string> log)
    {
        // Ordered, de-duplicated; libraries whose "apps" block lists the game are tried first.
        var order = new List<string>();
        var listsApp = new Dictionary<string, bool>(PathComparer);

        void AddLibrary(string rawPath, bool appListed)
        {
            string? normalized = NormalizePath(rawPath);
            if (normalized is null)
            {
                log.Add($"Ignoring invalid library path \"{rawPath}\"");
                return;
            }

            if (listsApp.TryGetValue(normalized, out bool existing))
            {
                listsApp[normalized] = existing || appListed;
                return;
            }

            listsApp[normalized] = appListed;
            order.Add(normalized);
        }

        // The Steam folder itself is always library #0.
        AddLibrary(steamPath, appListed: false);

        string[] vdfCandidates =
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "SteamApps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf"),
        };

        string appKey = appId.ToString(CultureInfo.InvariantCulture);
        bool parsedAny = false;

        foreach (string vdfPath in vdfCandidates.Distinct(PathComparer))
        {
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            try
            {
                KvNode root = ValveKeyValues.ParseFile(vdfPath);
                KvNode? folders = root.Child("libraryfolders") ?? root.Children.FirstOrDefault(c => c.IsSection);

                if (folders is null)
                {
                    log.Add($"{vdfPath}: no \"libraryfolders\" section");
                    continue;
                }

                int count = 0;
                foreach (KvNode entry in folders.Children)
                {
                    if (entry.IsSection)
                    {
                        // Modern format: "1" { "path" "D:\\SteamLibrary" ... "apps" { "1973530" "..." } }
                        string? path = entry.GetString("path");
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        bool appListed = entry.Child("apps")?.Child(appKey) is not null;
                        AddLibrary(path, appListed);
                        count++;
                    }
                    else if (entry.Key.Length > 0 && entry.Key.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        // Legacy format (pre-2021): "1" "D:\\SteamLibrary"
                        AddLibrary(entry.Value!, appListed: false);
                        count++;
                    }
                }

                parsedAny = true;
                log.Add($"{vdfPath}: {count} library entr{(count == 1 ? "y" : "ies")}");
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                log.Add($"{vdfPath}: could not parse ({ex.Message})");
            }
        }

        if (!parsedAny)
        {
            log.Add("No readable libraryfolders.vdf; scanning the main Steam folder only.");
        }

        var ordered = order.Where(p => listsApp[p]).Concat(order.Where(p => !listsApp[p])).ToList();

        foreach (string lib in ordered)
        {
            bool exists = Directory.Exists(lib);
            log.Add($"Library: {lib}{(listsApp[lib] ? "  [lists app]" : string.Empty)}{(exists ? string.Empty : "  [drive/folder not available]")}");
        }

        return ordered;
    }

    private static string? FindManifestInLibrary(string libraryPath, int appId)
    {
        string fileName = $"appmanifest_{appId.ToString(CultureInfo.InvariantCulture)}.acf";

        foreach (string steamApps in new[] { "steamapps", "SteamApps" })
        {
            string candidate = Path.Combine(libraryPath, steamApps, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (OperatingSystem.IsWindows())
            {
                break; // case-insensitive file system: one probe is enough
            }
        }

        return null;
    }

    // =========================================================================================
    //  appmanifest_<appid>.acf
    // =========================================================================================

    private static KvNode? ReadAppState(string manifestPath, List<string> log)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                KvNode root = ValveKeyValues.ParseFile(manifestPath);
                KvNode? appState = root.Child("AppState") ?? root.Children.FirstOrDefault(c => c.IsSection);

                if (appState is null)
                {
                    log.Add($"{manifestPath}: no \"AppState\" section");
                }

                return appState;
            }
            catch (IOException ex) when (attempt == 1)
            {
                // Steam may be rewriting the file this instant; try once more.
                log.Add($"{manifestPath}: busy ({ex.Message}), retrying");
                Thread.Sleep(150);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                log.Add($"{manifestPath}: could not parse ({ex.Message})");
                return null;
            }
        }

        return null;
    }

    // =========================================================================================
    //  Helpers
    // =========================================================================================

    /// <summary>
    /// Normalises Steam's paths ("c:/program files (x86)/steam", "D:\\SteamLibrary") to a full,
    /// OS-native path without a trailing separator. Returns null if the path is malformed.
    /// </summary>
    private static string? NormalizePath(string raw)
    {
        string trimmed = raw.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            trimmed = trimmed.Replace('/', '\\');
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }
    }
}
