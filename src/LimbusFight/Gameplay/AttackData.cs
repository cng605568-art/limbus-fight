namespace LimbusFight.Gameplay;

/// <summary>
/// Frame data and hitbox geometry for one attack. All timings are in 60 Hz frames, all
/// distances in stage pixels, and geometry is described for a fighter facing RIGHT
/// (it is mirrored automatically when facing left).
/// </summary>
public sealed record AttackData
{
    public required string Name { get; init; }

    /// <summary>Frames before the hitbox appears (the press frame is frame 1).</summary>
    public required int Startup { get; init; }

    /// <summary>Frames the hitbox is live.</summary>
    public required int Active { get; init; }

    /// <summary>Frames after the hitbox disappears before the fighter can act again.</summary>
    public required int Recovery { get; init; }

    /// <summary>Frames the defender is stunned after the hit (counted after hitstop).</summary>
    public required int Hitstun { get; init; }

    /// <summary>Frames both fighters freeze on impact (the "crunch" of a hit).</summary>
    public required int Hitstop { get; init; }

    /// <summary>Initial speed (px/frame) at which the attacker slides back after connecting.</summary>
    public required float Pushback { get; init; }

    /// <summary>Hitbox: distance from the fighter's centre line to the box's near edge.</summary>
    public required float HitboxOffsetX { get; init; }

    public required float HitboxWidth { get; init; }

    /// <summary>Hitbox: top edge relative to the feet (negative = above the floor).</summary>
    public required float HitboxOffsetY { get; init; }

    public required float HitboxHeight { get; init; }

    /// <summary>Extended-limb hurtbox (usually a little shorter than the hitbox).</summary>
    public required float LimbHurtboxWidth { get; init; }

    public int TotalFrames => Startup + Active + Recovery;

    public int FirstActiveFrame => Startup + 1;

    public int LastActiveFrame => Startup + Active;

    public bool IsActiveFrame(int frame) => frame >= FirstActiveFrame && frame <= LastActiveFrame;

    public string PhaseAt(int frame) =>
        frame <= Startup ? "STARTUP" : frame <= LastActiveFrame ? "ACTIVE" : "RECOVERY";

    /// <summary>The Step 1 test jab: 5f startup, 3f active, 9f recovery (17f total).</summary>
    public static AttackData TestPunch { get; } = new()
    {
        Name = "Punch",
        Startup = 5,
        Active = 3,
        Recovery = 9,
        Hitstun = 16,
        Hitstop = 9,
        Pushback = 7f,
        HitboxOffsetX = 22f,
        HitboxWidth = 96f,
        HitboxOffsetY = -154f,
        HitboxHeight = 30f,
        LimbHurtboxWidth = 80f,
    };
}
