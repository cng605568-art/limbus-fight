using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Gameplay;
using LimbusFight.UI;
using Raylib_cs;

namespace LimbusFight.Rendering;

/// <summary>
/// Draws the test arena: backdrop, floor, the two box fighters, hit sparks and (F1) the
/// collision boxes. Read-only view of the simulation; never changes game state.
/// </summary>
public static class ArenaRenderer
{
    private const int HitSparkFrames = 14;

    public static void Draw(TestArena arena, bool showBoxes, int width, int height, LookManager? looks = null)
    {
        DrawStage(width, height);

        DrawShadow(arena.Player1);
        DrawShadow(arena.Dummy);

        DrawFighter(arena, arena.Dummy, BodyColor(arena.Dummy, Theme.DummyBody), Theme.DummyTrim, looks, LookSlot.Dummy);
        DrawFighter(arena, arena.Player1, BodyColor(arena.Player1, Theme.Player1Body), Theme.Player1Trim, looks, LookSlot.Player1);

        if (looks is not null)
        {
            DrawSwingEffect(looks, LookSlot.Dummy, arena.Dummy);
            DrawSwingEffect(looks, LookSlot.Player1, arena.Player1);
        }

        if (!DrawHitEffect(arena, looks))
        {
            DrawHitSpark(arena);
        }

        if (showBoxes)
        {
            DrawBoxes(arena.Dummy);
            DrawBoxes(arena.Player1);
        }
    }

    private static void DrawStage(int width, int height)
    {
        int floorY = (int)TestArena.FloorY;

        Raylib.DrawRectangleGradientV(0, 0, width, floorY, Theme.Background, Theme.BackgroundHorizon);

        // Floor slab with distance marks every 80 px (makes walk speed easy to eyeball).
        Raylib.DrawRectangle(0, floorY, width, height - floorY, Theme.Floor);
        for (int x = 0; x <= width; x += 80)
        {
            Raylib.DrawLine(x, floorY + 4, x, floorY + 18, Theme.FloorMarks);
        }

        Raylib.DrawLineEx(new Vector2(0, floorY), new Vector2(width, floorY), 3f, Theme.Accent);

        // Stage walls.
        Raylib.DrawRectangle(0, MatchHud.Bottom, (int)TestArena.WallLeft, floorY - MatchHud.Bottom, Theme.Wall);
        Raylib.DrawRectangle((int)TestArena.WallRight, MatchHud.Bottom, width - (int)TestArena.WallRight, floorY - MatchHud.Bottom, Theme.Wall);
    }

    private static void DrawShadow(Fighter f)
    {
        float heightAboveFloor = f.FloorY - f.Y;
        float scale = Math.Clamp(1f - (heightAboveFloor / 400f), 0.45f, 1f);
        Raylib.DrawEllipse((int)f.X, (int)f.FloorY + 3, 50f * scale, 9f * scale, Theme.Shadow);
    }

    private static void DrawFighter(TestArena arena, Fighter f, Color body, Color trim, LookManager? looks, LookSlot slot)
    {
        // Hitstop shake on the fighter being hit.
        float shake = 0f;
        if (arena.HitstopRemaining > 0 && f.State == FighterState.Hitstun)
        {
            shake = arena.HitstopRemaining % 2 == 0 ? 4f : -4f;
        }

        bool flash = f.State == FighterState.Hitstun && (f.StateFrame / 2) % 2 == 0;

        // Step 3: the fighter's look (pictures from the player's own Limbus files), if one is set up.
        if (looks is not null && looks.DrawFighter(slot, f, shake, flash))
        {
            float size = f.Stats.Boss?.DrawScale ?? 1f;
            Ui.TextCentered(Label(f), (int)(f.X + shake), (int)(f.Y - MathF.Max(f.Stats.HurtboxHeight, LookData.AutoHeight * size) - 24), 20, trim);
            return;
        }

        Color fill = flash ? Theme.HitFlash
            : f.ArmorFlash > 0 ? new Color((byte)210, (byte)214, (byte)228, (byte)255)
            : f.InPhase2 ? new Color((byte)Math.Min(255, body.R + 70), (byte)(body.G * 0.6f), (byte)(body.B * 0.6f), (byte)255)
            : body;

        // The box follows the hurtbox (76x176 for the standard 84x182 one), so Bosses are bigger.
        float bodyW = f.Stats.HurtboxWidth * (76f / 84f);
        float bodyH = f.Stats.HurtboxHeight * (176f / 182f);
        float left = f.X - (bodyW * 0.5f) + shake;
        float top = f.Y - bodyH;

        // Squash a little in jump squat / landing so the states are readable without boxes.
        if (f.State is FighterState.JumpSquat or FighterState.Landing)
        {
            top += 10f;
        }

        var bodyRect = new Rectangle(left, top, bodyW, f.Y - top);
        Raylib.DrawRectangleRec(bodyRect, fill);
        Raylib.DrawRectangleLinesEx(bodyRect, 2f, trim);

        // Visor on the facing side, so facing is obvious.
        float visorX = f.Facing > 0 ? left + bodyW - 30f : left + 6f;
        Raylib.DrawRectangleRec(new Rectangle(visorX, top + 18f, 24f, 10f), trim);

        // Belt.
        Raylib.DrawRectangleRec(new Rectangle(left, top + (bodyH * (96f / 176f)), bodyW, 6f), trim);

        if (f.Stats.IsBoss)
        {
            // A crown of spikes marks a Boss.
            for (int i = 0; i < 3; i++)
            {
                float cx = left + (bodyW * (0.25f + (i * 0.25f)));
                Raylib.DrawTriangle(new Vector2(cx - 9f, top), new Vector2(cx + 9f, top), new Vector2(cx, top - 18f), trim);
            }
        }

        // Punching arm.
        float extension = f.ArmExtension;
        if (extension > 0f && f.CurrentAttack is { } attack)
        {
            // Fully extended, the fist stops just inside the hitbox's far edge.
            float shoulderOffset = (bodyW * 0.5f) - 4f;
            float fullLength = attack.HitboxOffsetX + attack.HitboxWidth - 6f - shoulderOffset;
            float armLength = MathF.Max(16f, fullLength * extension);
            float armY = f.Y + attack.HitboxOffsetY + 4f;
            float armX = f.Facing > 0 ? left + bodyW - 4f : left + 4f - armLength;
            var arm = new Rectangle(armX, armY, armLength, attack.HitboxHeight - 8f);
            Raylib.DrawRectangleRec(arm, fill);
            Raylib.DrawRectangleLinesEx(arm, 2f, trim);
        }

        // Name tag.
        Ui.TextCentered(Label(f), (int)(f.X + shake), (int)top - (f.Stats.IsBoss ? 44 : 28), 20, trim);
    }

    /// <summary>The look's swing effect on the hitbox while the attack is out.</summary>
    private static void DrawSwingEffect(LookManager looks, LookSlot slot, Fighter f)
    {
        if (f.CurrentAttack is not { } attack)
        {
            return;
        }

        List<LookFrame> frames = EffectTiming.SwingFrames(looks.Get(slot), attack.Slot);
        if (EffectTiming.Swing(frames, f) is not (int index, float alpha))
        {
            return;
        }

        (float x, float y) = EffectTiming.SwingAnchor(f, attack);
        looks.DrawEffect(slot, frames[index], x, y, f.Facing, alpha);
    }

    /// <summary>The attacker's hit effect where the hit landed. False if there is none (the spark is drawn instead).</summary>
    private static bool DrawHitEffect(TestArena arena, LookManager? looks)
    {
        if (looks is null || arena.LastHitFrame < 0 || arena.LastHitAttacker is not Fighter attacker)
        {
            return false;
        }

        LookSlot slot = attacker == arena.Player1 ? LookSlot.Player1 : LookSlot.Dummy;
        List<LookFrame> frames = looks.Get(slot).Frames(ClipId.HitFx);
        if (frames.Count == 0)
        {
            return false;
        }

        if (EffectTiming.Hit(frames, arena.Frame - arena.LastHitFrame) is (int index, float alpha))
        {
            (float hx, float hy) = arena.LastHitPoint;
            return looks.DrawEffect(slot, frames[index], hx, hy, attacker.Facing, alpha);
        }

        return true; // finished playing: no spark either
    }

    /// <summary>"P1" / "DUMMY", plus the fighter's name once it has a real one.</summary>
    /// <summary>The fighter file's box colour, else the slot's.</summary>
    public static Color BodyColor(Fighter f, Color fallback)
    {
        uint rgb = f.Stats.BodyRgb;
        return rgb == 0 ? fallback : new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
    }

    private static string Label(Fighter f)
    {
        string tag = f.Name;
        return f.Stats.Name is "Player 1" or "Training Box" or "Training Dummy" ? tag : $"{tag}  {f.Stats.Name}";
    }

    private static void DrawHitSpark(TestArena arena)
    {
        if (arena.LastHitFrame < 0)
        {
            return;
        }

        long age = arena.Frame - arena.LastHitFrame;
        if (age > HitSparkFrames)
        {
            return;
        }

        (float hx, float hy) = arena.LastHitPoint;
        float t = (float)age / HitSparkFrames;
        float radius = 10f + (40f * t);
        byte alpha = (byte)(255 * (1f - t));
        var spark = new Color((byte)255, (byte)230, (byte)140, alpha);

        Raylib.DrawCircleLines((int)hx, (int)hy, radius, spark);
        Raylib.DrawCircleLines((int)hx, (int)hy, radius * 0.6f, spark);

        for (int i = 0; i < 8; i++)
        {
            float angle = (MathF.PI / 4f * i) + 0.3f;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 from = new Vector2(hx, hy) + (dir * radius * 0.4f);
            Vector2 to = new Vector2(hx, hy) + (dir * radius * 1.2f);
            Raylib.DrawLineEx(from, to, 3f, spark);
        }
    }

    private static void DrawBoxes(Fighter f)
    {
        // Yellow pushbox (thin outline).
        Raylib.DrawRectangleLinesEx(ToRect(f.Pushbox), 1f, Theme.Pushbox);

        // Blue hurtboxes.
        foreach (Box hurt in f.Hurtboxes)
        {
            Rectangle r = ToRect(hurt);
            Raylib.DrawRectangleRec(r, WithAlpha(Theme.Hurtbox, 60));
            Raylib.DrawRectangleLinesEx(r, 2f, Theme.Hurtbox);
        }

        // Red active hitbox (dim outline once it has already connected).
        if (f.LiveHitbox is Box live)
        {
            Rectangle r = ToRect(live);
            Raylib.DrawRectangleRec(r, WithAlpha(Theme.Hitbox, 110));
            Raylib.DrawRectangleLinesEx(r, 2f, Theme.Hitbox);
        }
        else if (f.ActiveFrameHitbox is Box spent)
        {
            Raylib.DrawRectangleLinesEx(ToRect(spent), 2f, Theme.HitboxSpent);
        }

        // Origin cross at the feet.
        Raylib.DrawLine((int)f.X - 8, (int)f.Y, (int)f.X + 8, (int)f.Y, Color.White);
        Raylib.DrawLine((int)f.X, (int)f.Y - 8, (int)f.X, (int)f.Y + 8, Color.White);
    }

    private static Rectangle ToRect(Box b) => new(b.X, b.Y, b.Width, b.Height);

    private static Color WithAlpha(Color c, byte alpha) => new(c.R, c.G, c.B, alpha);
}
