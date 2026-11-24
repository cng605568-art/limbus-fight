using LimbusFight.Core;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>Top-of-screen panel: Limbus Company detection status, path, StateFlags and buildid.</summary>
public static class SteamStatusPanel
{
    public const int Height = 160;

    private const int Margin = 20;
    private const int Font = 20;

    /// <summary>Height of the one-line version shown above the match HUD.</summary>
    public const int CompactHeight = 26;

    /// <summary>One line: game found / verdict, buildid, version gate, build. Full details: F3 report / F5.</summary>
    public static void DrawCompact(SteamCheckResult r, int width)
    {
        Raylib.DrawRectangle(0, 0, width, CompactHeight, Theme.Panel);
        Raylib.DrawRectangle(0, CompactHeight - 1, width, 1, Theme.PanelBorder);
        const int font = 18;
        (string label, Color color) = StatusStyle(r.Status);
        int x = Ui.LabelValue("Limbus Company:", r.GameFound ? "FOUND" : "MISSING", 10, 4, font, r.GameFound ? Theme.Good : Theme.Bad);
        Ui.Text(label, x + 12, 4, font, color);
        x += 12 + Ui.Measure(label, font);
        x = Ui.LabelValue("buildid:", r.BuildIdDisplay, x + 24, 4, font, r.BuildId is null ? Theme.TextDim : Theme.Text);
        Ui.LabelValue("version check:", r.BypassVersionCheck ? "bypassed" : "on", x + 24, 4, font, r.BypassVersionCheck ? Theme.Warn : Theme.Good);
        Ui.TextRight($"LIMBUS FIGHT  training  v{AppInfo.Version}", width - 10, 4, font, Theme.TextDim);
    }

    public static void Draw(SteamCheckResult r, int width)
    {
        Raylib.DrawRectangle(0, 0, width, Height, Theme.Panel);
        Raylib.DrawRectangle(0, Height - 3, width, 3, Theme.PanelBorder);
        Raylib.DrawRectangle(0, 0, 6, Height - 3, Theme.Accent);

        int right = width - Margin;

        // Row 0: title + build.
        Ui.Text("LIMBUS FIGHT", Margin, 12, 30, Theme.Accent);
        Ui.Text("STEP 9  -  BOSSES", Margin + Ui.Measure("LIMBUS FIGHT", 30) + 18, 20, Font, Theme.TextDim);
        Ui.TextRight($"v{AppInfo.Version}", right, 20, Font, Theme.TextDim);

        // Row 1: found / missing + verdict, and the version gate mode on the right.
        (string label, Color color) = StatusStyle(r.Status);
        int x = Ui.LabelValue("Limbus Company:", r.GameFound ? "FOUND" : "MISSING", Margin, 54, Font, r.GameFound ? Theme.Good : Theme.Bad);
        x += 18;
        Raylib.DrawRectangle(x, 50, Ui.Measure(label, Font) + 16, 28, Fade(color, 0.18f));
        Raylib.DrawRectangleLines(x, 50, Ui.Measure(label, Font) + 16, 28, color);
        Ui.Text(label, x + 8, 54, Font, color);

        string gate = r.BypassVersionCheck ? "BYPASSED (early testing)" : "ENFORCED";
        Color gateColor = r.BypassVersionCheck ? Theme.Warn : Theme.Good;
        string gateLabel = "Version check:";
        int gateWidth = Ui.Measure(gateLabel, Font) + 8 + Ui.Measure(gate, Font);
        Ui.LabelValue(gateLabel, gate, right - gateWidth, 54, Font, gateColor);

        // Row 2: install path (+ library count on the right).
        string libs = $"{r.LibraryFolders.Count} Steam librar{(r.LibraryFolders.Count == 1 ? "y" : "ies")} scanned";
        int libsWidth = Ui.Measure(libs, Font);
        Ui.TextRight(libs, right, 82, Font, Theme.TextDim);

        string pathLabel = "Path:";
        string path = r.InstallPath ?? (r.SteamFound ? "(not installed)" : "(Steam not found)");
        if (r.InstallPath is not null && !r.InstallDirectoryExists)
        {
            path += "  [FOLDER MISSING]";
        }

        int pathMax = right - libsWidth - 30 - (Margin + Ui.Measure(pathLabel, Font) + 8);
        Ui.LabelValue(pathLabel, Ui.FitMiddle(path, Font, pathMax), Margin, 82, Font, r.InstallPath is null ? Theme.TextDim : Theme.Text);

        // Row 3: StateFlags / buildid / target (+ Steam's pending build when there is room).
        Color flagsColor = r.StateFlags is null ? Theme.TextDim
            : r.StateFlags == SteamChecker.FullyInstalledStateFlags ? Theme.Good
            : Theme.Bad;
        x = Ui.LabelValue("StateFlags:", r.StateFlagsDisplay, Margin, 110, Font, flagsColor);
        x = Ui.LabelValue("buildid:", r.BuildIdDisplay, x + 28, 110, Font, r.BuildId is null ? Theme.TextDim : Theme.Text);
        x = Ui.LabelValue("TARGET_BUILD_ID:", r.TargetBuildDisplay, x + 28, 110, Font, r.TargetBuildConfigured ? Theme.Text : Theme.TextDim);
        if (r.SteamPendingBuildId is long pending)
        {
            string pendingText = pending.ToString();
            int needed = 28 + Ui.Measure("Steam pending:", Font) + 8 + Ui.Measure(pendingText, Font);
            if (x + needed <= right)
            {
                Ui.LabelValue("Steam pending:", pendingText, x + 28, 110, Font, Theme.Warn);
            }
        }

        // Row 4: one-line verdict + scan time.
        string checkedAt = $"scanned {r.CheckedAt:HH:mm:ss}  [F5]";
        int checkedWidth = Ui.Measure(checkedAt, Font);
        Ui.TextRight(checkedAt, right, 134, Font, Theme.TextDim);

        string summary = r.Summary;
        Ui.Text(Ui.FitEnd(summary, Font, right - checkedWidth - 30 - Margin), Margin, 134, Font, Theme.TextDim);
    }

    /// <summary>Short badge text and colour for each scan verdict.</summary>
    public static (string Label, Color Color) StatusStyle(SteamGameStatus status) => status switch
    {
        SteamGameStatus.Ready => ("READY", Theme.Good),
        SteamGameStatus.ReadyNewerBuild => ("NEWER THAN VERIFIED BUILD", Theme.Warn),
        SteamGameStatus.UpdateRequired => ("UPDATE REQUIRED", Theme.Bad),
        SteamGameStatus.Updating => ("UPDATING", Theme.Bad),
        SteamGameStatus.InstallNotReady => ("NOT READY", Theme.Bad),
        SteamGameStatus.ManifestUnreadable => ("MANIFEST ERROR", Theme.Bad),
        SteamGameStatus.GameNotInstalled => ("NOT INSTALLED", Theme.Bad),
        SteamGameStatus.SteamNotFound => ("STEAM NOT FOUND", Theme.Bad),
        _ => (status.ToString().ToUpperInvariant(), Theme.Bad),
    };

    private static Color Fade(Color c, float alpha) => new(c.R, c.G, c.B, (byte)(alpha * 255));
}
