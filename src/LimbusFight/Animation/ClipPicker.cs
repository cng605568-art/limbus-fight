using LimbusFight.Gameplay;

namespace LimbusFight.Animation;

/// <summary>
/// Chooses which clip and which frame of a look to draw for a fighter this frame. Pure function of
/// the simulation state (no clocks, no Raylib), so replays and rollback will draw the same thing.
/// </summary>
public static class ClipPicker
{
    /// <summary>Returns the clip and frame index to draw, or null if the look has nothing usable.</summary>
    public static (ClipId Clip, int Frame)? Pick(Fighter fighter, LookData look)
    {
        ClipId wanted = Wanted(fighter.State);
        List<LookFrame> frames = look.Frames(wanted);

        if (frames.Count > 0)
        {
            return (wanted, FrameFor(wanted, frames, fighter));
        }

        // Fall back to idle (looping), then to any clip that has frames.
        foreach (ClipId fallback in new[] { ClipId.Idle, ClipId.Walk, ClipId.Punch, ClipId.Jump, ClipId.Hit })
        {
            List<LookFrame> other = look.Frames(fallback);
            if (other.Count > 0)
            {
                return (fallback, Looping(other, fighter.StateFrame));
            }
        }

        return null;
    }

    public static ClipId Wanted(FighterState state) => state switch
    {
        FighterState.Hitstun or FighterState.Staggered or FighterState.ClashRecoil or FighterState.Down => ClipId.Hit,
        FighterState.Attack => ClipId.Punch,
        FighterState.JumpSquat or FighterState.Airborne => ClipId.Jump,
        FighterState.WalkForward or FighterState.WalkBackward => ClipId.Walk,
        _ => ClipId.Idle,
    };

    private static int FrameFor(ClipId clip, List<LookFrame> frames, Fighter fighter) => clip switch
    {
        ClipId.Punch when fighter.CurrentAttack is AttackData attack => PunchFrame(frames.Count, attack, fighter.StateFrame),
        ClipId.Jump => JumpFrame(frames.Count, fighter.State, fighter.VelocityY),
        ClipId.Hit => Clamped(frames, fighter.StateFrame),
        _ => Looping(frames, fighter.StateFrame),
    };

    /// <summary>
    /// Punch frames follow the attack's timing instead of their own holds, so the "hit" picture is on
    /// screen exactly while the hitbox is out: 1st frame = wind-up (startup), 2nd = hit (active
    /// frames), the rest share the recovery.
    /// </summary>
    public static int PunchFrame(int count, AttackData attack, int stateFrame)
    {
        if (count <= 1)
        {
            return 0;
        }

        if (stateFrame <= attack.Startup)
        {
            return 0;
        }

        if (count == 2 || stateFrame <= attack.LastActiveFrame)
        {
            return 1;
        }

        int recoveryFrames = count - 2;
        int intoRecovery = stateFrame - attack.LastActiveFrame - 1;
        int index = intoRecovery * recoveryFrames / Math.Max(1, attack.Recovery);
        return 2 + Math.Clamp(index, 0, recoveryFrames - 1);
    }

    /// <summary>Jump frames by phase: with 3+ frames 1st = crouch before take-off, 2nd = rising, last = falling.</summary>
    public static int JumpFrame(int count, FighterState state, float velocityY)
    {
        if (count <= 1)
        {
            return 0;
        }

        if (count == 2)
        {
            return state == FighterState.Airborne && velocityY >= 0 ? 1 : 0;
        }

        if (state == FighterState.JumpSquat)
        {
            return 0;
        }

        return velocityY < 0 ? 1 : count - 1;
    }

    /// <summary>Plays the frames by their holds and repeats.</summary>
    public static int Looping(List<LookFrame> frames, int stateFrame)
    {
        int total = frames.Sum(f => Math.Max(1, f.Hold));
        int t = Math.Max(0, stateFrame - 1) % Math.Max(1, total);
        return IndexAt(frames, t);
    }

    /// <summary>Plays the frames by their holds once and stays on the last one.</summary>
    public static int Clamped(List<LookFrame> frames, int stateFrame)
    {
        int total = frames.Sum(f => Math.Max(1, f.Hold));
        int t = Math.Clamp(stateFrame - 1, 0, Math.Max(0, total - 1));
        return IndexAt(frames, t);
    }

    private static int IndexAt(List<LookFrame> frames, int t)
    {
        for (int i = 0; i < frames.Count; i++)
        {
            t -= Math.Max(1, frames[i].Hold);
            if (t < 0)
            {
                return i;
            }
        }

        return frames.Count - 1;
    }
}
