namespace LimbusFight.Core;

/// <summary>
/// Steam's EAppState bit flags, as written to "StateFlags" in appmanifest_*.acf.
/// A healthy, fully updated install is exactly <see cref="FullyInstalled"/> (4).
/// </summary>
[Flags]
public enum AppStateFlags : long
{
    Invalid = 0,
    Uninstalled = 1 << 0,
    UpdateRequired = 1 << 1,
    FullyInstalled = 1 << 2,
    Encrypted = 1 << 3,
    Locked = 1 << 4,
    FilesMissing = 1 << 5,
    AppRunning = 1 << 6,
    FilesCorrupt = 1 << 7,
    UpdateRunning = 1 << 8,
    UpdatePaused = 1 << 9,
    UpdateStarted = 1 << 10,
    Uninstalling = 1 << 11,
    BackupRunning = 1 << 12,
    Reconfiguring = 1 << 16,
    Validating = 1 << 17,
    AddingFiles = 1 << 18,
    Preallocating = 1 << 19,
    Downloading = 1 << 20,
    Staging = 1 << 21,
    Committing = 1 << 22,
    UpdateStopping = 1 << 23,
}

/// <summary>Overall verdict of a Steam scan, from worst to best.</summary>
public enum SteamGameStatus
{
    /// <summary>No Steam installation could be located.</summary>
    SteamNotFound,

    /// <summary>Steam found, but no library contains appmanifest_1973530.acf.</summary>
    GameNotInstalled,

    /// <summary>The manifest exists but could not be read or parsed.</summary>
    ManifestUnreadable,

    /// <summary>Steam is downloading / staging / validating an update right now.</summary>
    Updating,

    /// <summary>Steam has flagged an update, or the local buildid is older than TARGET_BUILD_ID.</summary>
    UpdateRequired,

    /// <summary>StateFlags is not 4 for another reason (files missing, corrupt, uninstalling...).</summary>
    InstallNotReady,

    /// <summary>StateFlags is 4 but the local buildid is newer than TARGET_BUILD_ID (Limbus Fight not yet verified on it).</summary>
    ReadyNewerBuild,

    /// <summary>StateFlags is 4 and buildid matches TARGET_BUILD_ID (or no target is configured yet).</summary>
    Ready,
}

/// <summary>Immutable snapshot of everything the Steam checker found.</summary>
public sealed record SteamCheckResult
{
    public required SteamGameStatus Status { get; init; }

    /// <summary>One-line human summary of <see cref="Status"/>.</summary>
    public required string Summary { get; init; }

    public required int AppId { get; init; }

    // ---- Steam --------------------------------------------------------------

    public bool SteamFound => SteamPath is not null;

    public string? SteamPath { get; init; }

    /// <summary>Every Steam library folder that was scanned, in scan order.</summary>
    public IReadOnlyList<string> LibraryFolders { get; init; } = Array.Empty<string>();

    // ---- Game (from appmanifest_1973530.acf) ---------------------------------

    /// <summary>True when an app manifest for the game was found (Found / Missing).</summary>
    public bool GameFound => ManifestPath is not null;

    public string? ManifestPath { get; init; }

    public string? LibraryPath { get; init; }

    /// <summary>steamapps/common/&lt;installdir&gt; inside the library that holds the manifest.</summary>
    public string? InstallPath { get; init; }

    public bool InstallDirectoryExists { get; init; }

    public string? AppName { get; init; }

    /// <summary>Raw "StateFlags" value (4 = fully installed and up to date).</summary>
    public long? StateFlags { get; init; }

    public AppStateFlags DecodedStateFlags => StateFlags is long raw ? (AppStateFlags)raw : AppStateFlags.Invalid;

    /// <summary>Raw "buildid" value of the installed game.</summary>
    public long? BuildId { get; init; }

    /// <summary>"TargetBuildID" written by Steam while an update is pending (null when absent / same as buildid).</summary>
    public long? SteamPendingBuildId { get; init; }

    public long BytesToDownload { get; init; }

    public long BytesDownloaded { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }

    // ---- Version gate -----------------------------------------------------------

    /// <summary>The build Limbus Fight was verified against (SteamChecker.TARGET_BUILD_ID).</summary>
    public required long TargetBuildId { get; init; }

    public bool TargetBuildConfigured => TargetBuildId > 0;

    /// <summary>Value of SteamChecker.BYPASS_VERSION_CHECK when this scan ran.</summary>
    public required bool BypassVersionCheck { get; init; }

    /// <summary>True when the player must update (or wait for the running update) before playing.</summary>
    public bool UpdateRequired => Status is SteamGameStatus.UpdateRequired or SteamGameStatus.Updating;

    /// <summary>True when gameplay must be blocked by the red warning screen.</summary>
    public bool ShouldBlockGameplay =>
        !BypassVersionCheck && Status is not (SteamGameStatus.Ready or SteamGameStatus.ReadyNewerBuild);

    // ---- Diagnostics --------------------------------------------------------------

    /// <summary>Step-by-step log of what the scan tried; shown by --selftest and useful for bug reports.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>"4 (FullyInstalled)" style text for UI.</summary>
    public string StateFlagsDisplay =>
        StateFlags is long raw ? $"{raw} ({DecodeFlags(raw)})" : "n/a";

    public string BuildIdDisplay => BuildId?.ToString() ?? "n/a";

    public string TargetBuildDisplay => TargetBuildConfigured ? TargetBuildId.ToString() : "not set (0)";

    /// <summary>Download progress 0..1 while Steam is updating, or null when unknown.</summary>
    public double? DownloadProgress =>
        BytesToDownload > 0 ? Math.Clamp((double)BytesDownloaded / BytesToDownload, 0.0, 1.0) : null;

    public static string DecodeFlags(long raw)
    {
        if (raw == 0)
        {
            return "Invalid";
        }

        var names = new List<string>();
        long known = 0;

        foreach (AppStateFlags flag in Enum.GetValues<AppStateFlags>())
        {
            long bit = (long)flag;
            if (bit != 0 && (raw & bit) == bit)
            {
                names.Add(flag.ToString());
                known |= bit;
            }
        }

        long unknown = raw & ~known;
        if (unknown != 0)
        {
            names.Add($"0x{unknown:X}");
        }

        return string.Join(" | ", names);
    }
}
