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

        return At(frames, fighter.StateFrame - attack.FirstActiveFrame);
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
