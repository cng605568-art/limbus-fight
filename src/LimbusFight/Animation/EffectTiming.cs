using LimbusFight.Gameplay;

namespace LimbusFight.Animation;

/// <summary>
/// When effect frames are on screen. Pure functions of the simulation state, like <see cref="ClipPicker"/>.
/// Effects play once through their frames (each for its hold) and then disappear.
/// </summary>
public static class EffectTiming
{
    /// <summary>
    /// Swing effect: starts on the attack's first active frame, so it appears exactly with the hitbox.
    /// Returns the frame index and an opacity, or null when nothing should show.
    /// </summary>
    public static (int Frame, float Alpha)? Swing(IReadOnlyList<LookFrame> frames, Fighter fighter)
    {
        if (frames.Count == 0 || fighter.State != FighterState.Attack || fighter.CurrentAttack is not AttackData attack)
        {
            return null;
        }

        // Multi-coin moves replay the effect for each coin.
        int sinceActive = fighter.StateFrame - attack.FirstActiveFrame;
        int coin = sinceActive < 0 ? 0 : Math.Min(attack.Coins - 1, sinceActive / Math.Max(1, attack.CoinInterval));
        return At(frames, sinceActive - (coin * attack.CoinInterval));
    }

    /// <summary>The swing-effect frames for a move: its own effect clip, else Skill 1's.</summary>
    public static List<LookFrame> SwingFrames(LookData look, MoveSlot slot)
    {
        ClipId[] chain = slot switch
        {
            MoveSlot.Skill2 => new[] { ClipId.Skill2Fx, ClipId.SwingFx },
            MoveSlot.Skill3 => new[] { ClipId.Skill3Fx, ClipId.SwingFx },
            MoveSlot.Ego => new[] { ClipId.EgoFx, ClipId.Skill3Fx, ClipId.SwingFx },
            _ => new[] { ClipId.SwingFx },
        };

        foreach (ClipId clip in chain)
        {
            List<LookFrame> frames = look.Frames(clip);
            if (frames.Count > 0)
            {
                return frames;
            }
        }

        return new List<LookFrame>();
    }

    /// <summary>Hit effect: starts on the frame the hit landed (<paramref name="age"/> 0) and keeps playing through hitstop.</summary>
    public static (int Frame, float Alpha)? Hit(IReadOnlyList<LookFrame> frames, long age)
    {
        if (frames.Count == 0 || age < 0 || age > 600)
        {
            return null;
        }

        return At(frames, (int)age);
    }

    /// <summary>Where the swing effect is centred: the middle of the attack's hitbox, in front of the fighter.</summary>
    public static (float X, float Y) SwingAnchor(Fighter fighter, AttackData attack) =>
        (fighter.X + (fighter.Facing * (attack.HitboxOffsetX + (attack.HitboxWidth / 2f))),
         fighter.Y + attack.HitboxOffsetY + (attack.HitboxHeight / 2f));

    /// <summary>Frame for time <paramref name="t"/> (0 = first frame); fades over the last 4 frames of the run.</summary>
    public static (int Frame, float Alpha)? At(IReadOnlyList<LookFrame> frames, int t)
    {
        int total = frames.Sum(f => Math.Max(1, f.Hold));
        if (t < 0 || t >= total)
        {
            return null;
        }

        int index = 0;
        int left = t;
        while (index < frames.Count - 1 && left >= Math.Max(1, frames[index].Hold))
        {
            left -= Math.Max(1, frames[index].Hold);
            index++;
        }

        int remaining = total - t;
        float alpha = remaining >= 4 ? 1f : remaining / 4f;
        return (index, Math.Max(alpha, 0.25f));
    }
}
