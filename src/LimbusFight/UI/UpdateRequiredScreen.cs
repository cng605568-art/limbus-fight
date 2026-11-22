using LimbusFight.Core;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>
/// Full-screen red warning shown instead of gameplay when BYPASS_VERSION_CHECK is false and
/// Limbus Company is missing, updating, not fully installed or older than TARGET_BUILD_ID.
/// </summary>
public static class UpdateRequiredScreen
{
    private const int Font = 20;

    public static void Draw(SteamCheckResult r, int width, int height, double secondsUntilRescan)
    {
        int cx = width / 2;

        Raylib.ClearBackground(Theme.AlertBackground);
        Raylib.DrawRectangle(0, 0, width, 10, Theme.AlertBorder);
        Raylib.DrawRectangle(0, height - 10, width, 10, Theme.AlertBorder);

        // Warning icon.
        Raylib.DrawCircle(cx, 98, 48, Theme.AlertBorder);
        Raylib.DrawCircleLines(cx, 98, 56, Theme.AlertBorder);
        Ui.TextCentered("!", cx, 62, 80, Color.White);

        (string headline, string subline, string[] steps) = Copy(r);

        Ui.TextCentered(headline, cx, 168, 50, Color.White);
        Ui.TextCentered(subline, cx, 230, Font, Theme.AlertText);
        Ui.TextCentered(Ui.FitEnd(r.Summary, Font, width - 160), cx, 260, Font, Theme.Warn);

        // Details box.
        const int boxW = 860;
        const int boxH = 140;
        int boxX = cx - (boxW / 2);
        const int boxY = 300;
        Raylib.DrawRectangle(boxX, boxY, boxW, boxH, Theme.AlertPanel);
        Raylib.DrawRectangleLines(boxX, boxY, boxW, boxH, Theme.AlertBorder);

        int rowX = boxX + 20;
        int valueMax = boxW - 40 - Ui.Measure("Install path:", Font) - 8;
        Ui.LabelValue("Install path:", Ui.FitMiddle(r.InstallPath ?? "(not found)", Font, valueMax), rowX, boxY + 16, Font, Color.White);
        Ui.LabelValue("StateFlags:", $"{r.StateFlagsDisplay}   (must be 4)", rowX, boxY + 46, Font, Color.White);

        int x = Ui.LabelValue("Installed buildid:", r.BuildIdDisplay, rowX, boxY + 76, Font, Color.White);
        Ui.LabelValue("Required buildid:", r.TargetBuildDisplay, x + 40, boxY + 76, Font, Color.White);

        string steamLine = r.SteamPath is null ? "(not found)" : r.SteamPath;
        if (r.SteamPendingBuildId is long pending)
        {
            string progress = r.DownloadProgress is double p ? $"  ({p * 100:0}% downloaded)" : string.Empty;
            Ui.LabelValue("Steam is updating to build:", $"{pending}{progress}", rowX, boxY + 106, Font, Theme.Warn);
        }
        else
        {
            Ui.LabelValue("Steam:", Ui.FitMiddle(steamLine, Font, boxW - 140), rowX, boxY + 106, Font, Color.White);
        }

        // Steps.
        int stepY = 472;
        foreach (string step in steps)
        {
            Ui.Text(Ui.FitEnd(step, Font, width - boxX - 30), boxX + 10, stepY, Font, Color.White);
            stepY += 30;
        }

        // Footer.
        string recheck = secondsUntilRescan > 0
            ? $"Re-checking Steam automatically in {Math.Ceiling(secondsUntilRescan):0}s   |   F5 = re-check now   |   ESC = quit"
            : "Re-checking Steam...   |   ESC = quit";
        Ui.TextCentered(recheck, cx, height - 48, Font, Theme.AlertText);
    }

    private static (string Headline, string Subline, string[] Steps) Copy(SteamCheckResult r) => r.Status switch
    {
        SteamGameStatus.Updating => (
            "LIMBUS COMPANY IS UPDATING",
            "Let Steam finish updating Limbus Company before you play Limbus Fight.",
            new[]
            {
                "1.  Keep Steam open until the Limbus Company download finishes.",
                "2.  If the update is paused or queued: Steam > Downloads > resume it.",
                "3.  This screen unlocks by itself as soon as Steam reports StateFlags = 4.",
            }),

        SteamGameStatus.GameNotInstalled => (
            "LIMBUS COMPANY NOT FOUND",
            "Limbus Fight needs your own Steam install of Limbus Company (free to play).",
            new[]
            {
                "1.  Install Limbus Company from your Steam library (App ID 1973530).",
                "2.  Installed on another drive? Make sure that drive is plugged in.",
                "3.  Press F5 after the install finishes.",
            }),

        SteamGameStatus.SteamNotFound => (
            "STEAM NOT FOUND",
            "Steam could not be located on this PC.",
            new[]
            {
                "1.  Install Steam, then install Limbus Company (App ID 1973530).",
                $"2.  Portable Steam? Set {SteamChecker.SteamPathOverrideEnvVar} to your Steam folder.",
                "3.  Press F5 to scan again.",
            }),

        SteamGameStatus.InstallNotReady => (
            "LIMBUS COMPANY INSTALL NOT READY",
            "Steam does not report Limbus Company as fully installed.",
            new[]
            {
                "1.  Steam > Limbus Company > Properties > Installed Files > Verify integrity.",
                "2.  Wait until Steam shows the game as ready to PLAY.",
                "3.  Press F5 to scan again.",
            }),

        SteamGameStatus.ManifestUnreadable => (
            "STEAM DATA UNREADABLE",
            "Steam's record for Limbus Company (appmanifest_1973530.acf) could not be read.",
            new[]
            {
                "1.  Restart Steam so it rewrites its app manifests.",
                "2.  Then verify integrity of game files for Limbus Company.",
                "3.  Press F5 to scan again.",
            }),

        _ => (
            "UPDATE REQUIRED",
            "Please update Limbus Company on Steam before playing Limbus Fight.",
            new[]
            {
                "1.  Open Steam > Library > Limbus Company.",
                "2.  Click UPDATE and wait for the download to finish.",
                "3.  This screen unlocks by itself once the update is installed (or press F5).",
            }),
    };
}
