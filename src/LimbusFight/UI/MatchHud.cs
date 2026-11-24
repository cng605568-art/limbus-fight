using LimbusFight.Gameplay;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>
/// The match HUD (Step 5): health, Stagger bar, Sanity, Sin resources and statuses for both
/// fighters, the round timer and score, plus floating combat text (damage, CLASH, STAGGERED, K.O.).
/// Reads the simulation only.
/// </summary>
public static class MatchHud
{
    /// <summary>First stage pixel row below the HUD.</summary>
    public const int Bottom = 132;

    private const int Top = SteamStatusPanel.CompactHeight;
    private const int BarW = 500;
    private const int Margin = 24;
    private const int PopupFrames = 50;

    private static readonly Color[] SinColors =
    {
        new(222, 64, 52, 255),   // Wrath
        new(240, 132, 44, 255),  // Lust
        new(232, 198, 64, 255),  // Sloth
        new(128, 198, 72, 255),  // Gluttony
        new(72, 170, 222, 255),  // Gloom
        new(70, 96, 210, 255),   // Pride
        new(150, 84, 204, 255),  // Envy
    };

    private static readonly string[] StatusShort = { "Burn", "Bleed", "Tremor", "Rupture", "Sinking", "Poise", "Charge" };

    public static void Draw(TestArena arena, int width)
    {
        Raylib.DrawRectangle(0, Top, width, Bottom - Top, new Color((byte)14, (byte)14, (byte)20, (byte)255));
        Raylib.DrawRectangle(0, Bottom - 2, width, 2, Theme.PanelBorder);

        DrawSide(arena.Player1, Margin, false, Theme.Player1Trim);
        DrawSide(arena.Dummy, width - Margin - BarW, true, Theme.DummyTrim);
        DrawCentre(arena, width);
    }

    /// <summary>Floating combat text and the round banner, drawn over the stage.</summary>
    public static void DrawPopups(TestArena arena, int width)
    {
        foreach (CombatEvent e in arena.Events)
        {
            long age = arena.Frame - e.Frame;
            if (age < 0 || age > PopupFrames || e.Kind is CombatEventKind.Ko or CombatEventKind.TimeOver or CombatEventKind.RoundStart)
            {
                continue;
            }

            float t = (float)age / PopupFrames;
            byte alpha = (byte)(255 * (1f - (t * t)));
            (int size, Color color) = e.Kind switch
            {
                CombatEventKind.Clash => (30, Theme.Accent),
                CombatEventKind.Stagger => (30, new Color((byte)255, (byte)210, (byte)70, (byte)255)),
                CombatEventKind.Panic => (28, new Color((byte)170, (byte)110, (byte)255, (byte)255)),
                CombatEventKind.Crit => (24, new Color((byte)255, (byte)240, (byte)160, (byte)255)),
                CombatEventKind.Ego => (34, new Color((byte)255, (byte)90, (byte)70, (byte)255)),
                CombatEventKind.Counter => (32, new Color((byte)120, (byte)230, (byte)255, (byte)255)),
                CombatEventKind.Guard => (20, new Color((byte)150, (byte)190, (byte)255, (byte)255)),
                CombatEventKind.Charge => (20, new Color((byte)120, (byte)200, (byte)255, (byte)255)),
                CombatEventKind.StatusDamage => (20, new Color((byte)255, (byte)150, (byte)90, (byte)255)),
                _ when e.Text.Contains("Fatal", StringComparison.Ordinal) || e.Text.Contains("Weak", StringComparison.Ordinal) => (28, Theme.Bad),
                _ when e.Text.Contains("Ineffective", StringComparison.Ordinal) || e.Text.Contains("Endured", StringComparison.Ordinal) => (22, Theme.TextDim),
                _ => (24, Theme.Text),
            };

            int y = (int)(e.Y - 20 - (age * 1.2f));
            Ui.TextCentered(e.Text, (int)e.X + 2, y + 2, size, new Color((byte)0, (byte)0, (byte)0, (byte)(alpha / 2)));
            Ui.TextCentered(e.Text, (int)e.X, y, size, new Color(color.R, color.G, color.B, alpha));
        }

        // Round banners: ROUND n / FIGHT at the start, K.O. / TIME and the winner at the end.
        if (arena.Phase == RoundPhase.Over && arena.RoundResult is string result)
        {
            Banner(result, width, Theme.Accent);
        }
        else if (arena.Phase == RoundPhase.Fight && arena.PhaseFrames < 70)
        {
            Banner(arena.PhaseFrames < 40 ? $"ROUND {arena.RoundNumber}" : "FIGHT!", width, Theme.Text);
        }
    }

    private static void Banner(string text, int width, Color color)
    {
        const int size = 60;
        int y = 300;
        int w = Ui.Measure(text, size) + 80;
        Raylib.DrawRectangle((width - w) / 2, y - 14, w, size + 28, new Color((byte)0, (byte)0, (byte)0, (byte)150));
        Ui.TextCentered(text, width / 2, y, size, color);
    }

    private static void DrawSide(Fighter f, int x, bool mirrored, Color nameColor)
    {
        FighterStats stats = f.Stats;
        string tag = f.IsDummy ? "DUMMY" : "P1";
        string name = stats.Name is "Player 1" or "Training Box" or "Training Dummy" ? tag : $"{tag}  {stats.Name}";
        int nameY = Top + 6;
        if (mirrored)
        {
            Ui.TextRight(name, x + BarW, nameY, 20, nameColor);
        }
        else
        {
            Ui.Text(name, x, nameY, 20, nameColor);
        }

        // Health bar (drains toward the centre of the screen).
        int hpY = Top + 30;
        float hp = Math.Clamp((float)f.Hp / Math.Max(1, stats.MaxHp), 0f, 1f);
        Raylib.DrawRectangle(x, hpY, BarW, 22, new Color((byte)40, (byte)16, (byte)18, (byte)255));
        int hpW = (int)(BarW * hp);
        Color hpColor = hp > 0.3f ? new Color((byte)210, (byte)50, (byte)52, (byte)255) : new Color((byte)255, (byte)110, (byte)60, (byte)255);
        Raylib.DrawRectangle(mirrored ? x + BarW - hpW : x, hpY, hpW, 22, hpColor);
        Raylib.DrawRectangleLines(x, hpY, BarW, 22, Theme.PanelBorder);
        string hpText = $"{f.Hp} / {stats.MaxHp}";
        if (mirrored)
        {
            Ui.Text(hpText, x + 8, hpY + 2, 18, Theme.Text);
        }
        else
        {
            Ui.TextRight(hpText, x + BarW - 8, hpY + 2, 18, Theme.Text);
        }

        // Stagger bar: yellow; flashes while Staggered.
        int stY = hpY + 26;
        float st = Math.Clamp((float)f.Stagger / Math.Max(1, stats.StaggerMax), 0f, 1f);
        bool staggered = f.State == FighterState.Staggered;
        Raylib.DrawRectangle(x, stY, BarW, 8, new Color((byte)44, (byte)40, (byte)20, (byte)255));
        int stW = (int)(BarW * st);
        Color stColor = staggered ? ((f.StateFrame / 6) % 2 == 0 ? Theme.Bad : Theme.Warn) : new Color((byte)236, (byte)196, (byte)70, (byte)255);
        if (staggered)
        {
            stW = BarW; // the whole bar flashes while Staggered
        }

        Raylib.DrawRectangle(mirrored ? x + BarW - stW : x, stY, stW, 8, stColor);

        // Sanity: a centred meter, -45 .. +45.
        int spY = stY + 14;
        int spW = 150;
        int spX = mirrored ? x + BarW - spW : x;
        int mid = spX + (spW / 2);
        Raylib.DrawRectangle(spX, spY, spW, 10, new Color((byte)30, (byte)30, (byte)40, (byte)255));
        int fill = (int)(spW / 2f * f.Sanity / CombatRules.SanityMax);
        Color spColor = f.IsPanicking ? new Color((byte)170, (byte)110, (byte)255, (byte)255) : f.Sanity >= 0 ? new Color((byte)90, (byte)200, (byte)230, (byte)255) : new Color((byte)200, (byte)110, (byte)90, (byte)255);
        Raylib.DrawRectangle(Math.Min(mid, mid + fill), spY, Math.Abs(fill), 10, spColor);
        Raylib.DrawLine(mid, spY - 2, mid, spY + 12, Theme.TextDim);
        string spText = f.IsPanicking ? "PANIC" : $"SP {f.Sanity:+0;-0;0}";
        int spTextX = mirrored ? spX - 10 - Ui.Measure(spText, 18) : spX + spW + 10;
        Ui.Text(spText, spTextX, spY - 4, 18, f.IsPanicking ? spColor : Theme.TextDim);

        // Sin resources: seven pips with counts.
        int pipX = mirrored ? x + BarW - 250 - 7 * 26 : x + 250;
        for (int i = 0; i < 7; i++)
        {
            int px = pipX + (i * 26);
            Color c = SinColors[i];
            bool has = f.Sins[i] > 0;
            Raylib.DrawRectangle(px, spY - 3, 20, 16, has ? c : new Color(c.R, c.G, c.B, (byte)50));
            if (has)
            {
                Ui.TextCentered(f.Sins[i].ToString(), px + 10, spY - 3, 16, Color.Black);
            }
        }

        // E.G.O readiness, defense and passive (Step 6).
        string ego = stats.Ego is not { } egoMove ? string.Empty
            : f.CanUseEgo ? "E.G.O READY (I)"
            : f.IsPanicking ? "E.G.O: Panic"
            : "E.G.O " + string.Join(" ", egoMove.SinCost.Select(c => $"{Math.Min(f.Sins[(int)c.Sin], c.Amount)}/{c.Amount} {c.Sin}"));
        string kit = $"{stats.Defense.Kind}" + (stats.Passive.Kind == PassiveKind.None ? string.Empty : $"  ·  {stats.Passive.Describe()}");
        int kitY = Top + 82;
        Color egoColor = f.CanUseEgo ? Theme.Accent : Theme.TextDim;
        if (mirrored)
        {
            Ui.Text(ego, x, kitY, 18, egoColor);
            Ui.TextRight(Ui.FitEnd(kit, 18, BarW - Ui.Measure(ego, 18) - 20), x + BarW, kitY, 18, Theme.TextDim);
        }
        else
        {
            Ui.TextRight(ego, x + BarW, kitY, 18, egoColor);
            Ui.Text(Ui.FitEnd(kit, 18, BarW - Ui.Measure(ego, 18) - 20), x, kitY, 18, Theme.TextDim);
        }

        // Statuses: "Burn 3x2" etc.
        var parts = new List<string>();
        for (int i = 0; i < f.Statuses.Length; i++)
        {
            if (f.Statuses[i].Active)
            {
                parts.Add($"{StatusShort[i]} {f.Statuses[i].Potency}x{f.Statuses[i].Count}");
            }
        }

        if (parts.Count > 0)
        {
            string statuses = string.Join("  ", parts);
            int sy = Top + 102;
            if (mirrored)
            {
                Ui.TextRight(Ui.FitEnd(statuses, 18, BarW), x + BarW, sy, 18, new Color((byte)255, (byte)170, (byte)110, (byte)255));
            }
            else
            {
                Ui.Text(Ui.FitEnd(statuses, 18, BarW), x, sy, 18, new Color((byte)255, (byte)170, (byte)110, (byte)255));
            }
        }
    }

    private static void DrawCentre(TestArena arena, int width)
    {
        int seconds = (arena.RoundTimer + 59) / 60;
        Color timeColor = seconds <= 10 ? Theme.Bad : Theme.Text;
        Ui.TextCentered(seconds.ToString(), width / 2, Top + 14, 44, timeColor);
        Ui.TextCentered($"ROUND {arena.RoundNumber}", width / 2, Top + 62, 18, Theme.TextDim);
        Ui.TextCentered($"{arena.Player1Wins} - {arena.DummyWins}", width / 2, Top + 80, 18, Theme.Accent);
    }
}
