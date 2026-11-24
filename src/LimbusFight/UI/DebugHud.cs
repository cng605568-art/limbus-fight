using LimbusFight.Core;
using LimbusFight.Gameplay;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>F1 overlay text: FPS, frame counter, fighter states and a box colour legend.</summary>
public static class DebugHud
{
    private const int Font = 20;
    private const int Line = 24;

    public static void Draw(TestArena arena, FixedStepClock clock, SteamCheckResult steam, int canvasWidth)
    {
        const int w = 430;
        const int h = 246;
        int x = canvasWidth - w - 16;
        int y = MatchHud.Bottom + 10;

        Raylib.DrawRectangle(x, y, w, h, Theme.Panel);
        Raylib.DrawRectangleLines(x, y, w, h, Theme.PanelBorder);

        int tx = x + 14;
        int ty = y + 10;

        Ui.Text("DEBUG VIEW  [F1]", tx, ty, Font, Theme.Accent);
        ty += Line + 4;

        int fps = Raylib.GetFPS();
        Color fpsColor = fps >= 59 ? Theme.Good : fps >= 50 ? Theme.Warn : Theme.Bad;
        int cx = Ui.LabelValue("FPS:", fps.ToString(), tx, ty, Font, fpsColor);
        cx = Ui.LabelValue("frame:", arena.Frame.ToString(), cx + 24, ty, Font, Theme.Text);
        Ui.LabelValue("dt:", $"{Raylib.GetFrameTime() * 1000f:0.0}ms", cx + 24, ty, Font, Theme.Text);
        ty += Line;

        cx = Ui.LabelValue("ticks/frame:", clock.LastTickCount.ToString(), tx, ty, Font, clock.LastTickCount == 1 ? Theme.Text : Theme.Warn);
        Ui.LabelValue("hitstop:", arena.HitstopRemaining.ToString(), cx + 24, ty, Font, arena.HitstopRemaining > 0 ? Theme.Warn : Theme.Text);
        ty += Line;

        Fighter p1 = arena.Player1;
        Ui.LabelValue("P1:", p1.StateDescription, tx, ty, Font, StateColor(p1));
        ty += Line;
        Ui.Text($"    x {p1.X:0.0}  y {p1.Y:0.0}  vx {p1.VelocityX:0.0}  vy {p1.VelocityY:0.0}  {(p1.Facing > 0 ? "R" : "L")}", tx, ty, Font, Theme.TextDim);
        ty += Line;

        Fighter dummy = arena.Dummy;
        Ui.LabelValue("Dummy:", dummy.StateDescription, tx, ty, Font, StateColor(dummy));
        ty += Line;
        Ui.Text($"    hits taken {dummy.HitsTaken}   combo {dummy.ComboCount}", tx, ty, Font, Theme.TextDim);
        ty += Line;

        Ui.LabelValue("Steam:", Ui.FitMiddle(steam.SteamPath ?? "not found", Font, w - 100), tx, ty, Font, Theme.TextDim);
        ty += Line + 4;

        // Legend.
        int lx = tx;
        lx = LegendItem(lx, ty, Theme.Hurtbox, "hurtbox");
        lx = LegendItem(lx + 16, ty, Theme.Hitbox, "hitbox");
        LegendItem(lx + 16, ty, Theme.Pushbox, "pushbox");
    }

    private static int LegendItem(int x, int y, Color color, string label)
    {
        Raylib.DrawRectangle(x, y + 3, 14, 14, new Color(color.R, color.G, color.B, (byte)90));
        Raylib.DrawRectangleLines(x, y + 3, 14, 14, color);
        Ui.Text(label, x + 20, y, Font, Theme.Text);
        return x + 20 + Ui.Measure(label, Font);
    }

    private static Color StateColor(Fighter f) => f.State switch
    {
        FighterState.Attack when f.ActiveFrameHitbox is not null => Theme.Hitbox,
        FighterState.Attack => Theme.Warn,
        FighterState.Hitstun => Theme.Hurtbox,
        _ => Theme.Text,
    };
}
