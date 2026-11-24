namespace LimbusFight.Gameplay;

/// <summary>Logical buttons, independent of the physical keys bound to them.</summary>
[Flags]
public enum InputButtons : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Up = 1 << 2,

    /// <summary>Skill 1 (J). Kept as "Punch" so Step 1-5 code and tests read the same.</summary>
    Punch = 1 << 3,

    /// <summary>Skill 2 (K).</summary>
    Skill2 = 1 << 4,

    /// <summary>Skill 3 (L).</summary>
    Skill3 = 1 << 5,

    /// <summary>Defense (S): guard while held, or evade / counter on press, per fighter.</summary>
    Defend = 1 << 6,

    /// <summary>E.G.O (I): super move paid with Sin resources.</summary>
    Ego = 1 << 7,
}

/// <summary>
/// The buttons held during one simulation frame. Press edges are derived inside the
/// simulation (held this frame, not held last frame), so a frame's input is a single byte:
/// trivial to record for replays or send over the network later.
/// </summary>
public readonly record struct FrameInput(InputButtons Held)
{
    public static FrameInput None => new(InputButtons.None);

    public bool IsHeld(InputButtons button) => (Held & button) == button;

    /// <summary>-1 = left, +1 = right, 0 = neither or both (SOCD neutral).</summary>
    public int Horizontal
    {
        get
        {
            int x = 0;
            if (IsHeld(InputButtons.Left))
            {
                x -= 1;
            }

            if (IsHeld(InputButtons.Right))
            {
                x += 1;
            }

            return x;
        }
    }
}
