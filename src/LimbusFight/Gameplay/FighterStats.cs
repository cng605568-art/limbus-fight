namespace LimbusFight.Gameplay;

/// <summary>
/// Everything that makes one fighter play differently: body boxes, movement and moves. Built from a
/// fighter data file (Roster/FighterData) so a new fighter needs no new code. The defaults are the
/// Step 1 training box, so behaviour is unchanged unless a file says otherwise.
/// </summary>
public sealed record FighterStats
{
    public string Name { get; init; } = "Training Box";

    /// <summary>Box colour (0xRRGGBB) when the fighter has no look; 0 = the slot's default colour.</summary>
    public uint BodyRgb { get; init; }

    // ---- Body geometry (stage pixels) ------------------------------------------------------
    public float HurtboxWidth { get; init; } = 84f;

    public float HurtboxHeight { get; init; } = 182f;

    public float PushboxWidth { get; init; } = 70f;

    public float PushboxHeight { get; init; } = 170f;

    // ---- Movement (per 60 Hz frame) --------------------------------------------------------
    public float WalkForwardSpeed { get; init; } = 4.0f;

    public float WalkBackwardSpeed { get; init; } = 3.2f;

    public float JumpLaunchSpeed { get; init; } = 20f;

    public float Gravity { get; init; } = 1.0f;

    public float JumpHorizontalSpeed { get; init; } = 4.5f;

    public int JumpSquatFrames { get; init; } = 4;

    public int LandingFrames { get; init; } = 3;

    // ---- Combat (Step 5) -------------------------------------------------------------------
    public int MaxHp { get; init; } = 100;

    /// <summary>Size of the Stagger bar; when hits empty it, the fighter is Staggered.</summary>
    public int StaggerMax { get; init; } = 40;

    /// <summary>Damage multipliers by type: 2 = Fatal, 1 = Normal, 0.5 = Ineffective.</summary>
    public float SlashResist { get; init; } = 1f;

    public float PierceResist { get; init; } = 1f;

    public float BluntResist { get; init; } = 1f;

    public float Resist(DamageType type) => type switch
    {
        DamageType.Slash => SlashResist,
        DamageType.Pierce => PierceResist,
        _ => BluntResist,
    };

    // ---- Moves -----------------------------------------------------------------------------
    /// <summary>Skill 1 on J (called Punch since Step 1).</summary>
    public AttackData Punch { get; init; } = AttackData.TestPunch;

    /// <summary>Skill 2 on K; null = none.</summary>
    public AttackData? Skill2 { get; init; } = AttackData.DefaultSkill2;

    /// <summary>Skill 3 on L; null = none.</summary>
    public AttackData? Skill3 { get; init; } = AttackData.DefaultSkill3;

    /// <summary>E.G.O on I; null = none.</summary>
    public AttackData? Ego { get; init; } = AttackData.DefaultEgo;

    // ---- Defense and passive (Step 6) -------------------------------------------------------
    public DefenseStats Defense { get; init; } = DefenseStats.Default;

    public PassiveStats Passive { get; init; } = PassiveStats.None;

    public AttackData? Move(MoveSlot slot) => slot switch
    {
        MoveSlot.Skill1 => Punch,
        MoveSlot.Skill2 => Skill2,
        MoveSlot.Skill3 => Skill3,
        _ => Ego,
    };

    public static FighterStats Default { get; } = new();
}

/// <summary>What the defense button does.</summary>
public enum DefenseKind
{
    /// <summary>Hold: hits deal a little damage and blockstun instead of hitstun.</summary>
    Guard,

    /// <summary>Press: a quick step back with invincible frames.</summary>
    Evade,

    /// <summary>Press: a parry window; a hit inside it is cancelled and the attacker reels.</summary>
    Counter,
}

public sealed record DefenseStats
{
    public DefenseKind Kind { get; init; } = DefenseKind.Guard;

    /// <summary>Guard: share of HP damage still taken (0.25 = a quarter).</summary>
    public float GuardDamage { get; init; } = 0.25f;

    /// <summary>Guard: share of Stagger damage still taken.</summary>
    public float GuardStagger { get; init; } = 1f;

    /// <summary>Evade / Counter: total frames of the move.</summary>
    public int TotalFrames { get; init; } = 22;

    /// <summary>Evade: invincible frames; Counter: parry window (inclusive, 1-based).</summary>
    public int WindowStart { get; init; } = 3;

    public int WindowEnd { get; init; } = 12;

    /// <summary>Evade: speed of the backstep (px/frame).</summary>
    public float EvadeSpeed { get; init; } = 7f;

    public static DefenseStats Default { get; } = new();

    public bool InWindow(int frame) => frame >= WindowStart && frame <= WindowEnd;
}

/// <summary>A fighter's always-on passive.</summary>
public enum PassiveKind
{
    None,

    /// <summary>+Value clash power on every move.</summary>
    ClashPower,

    /// <summary>+Value% damage while below half health.</summary>
    Desperation,

    /// <summary>+Value extra Sanity on each hit landed.</summary>
    Composure,

    /// <summary>Every hit also gives the opponent Value potency (1 count) of Status.</summary>
    StatusOnHit,

    /// <summary>Takes Value% less Stagger damage.</summary>
    Sturdy,
}

public sealed record PassiveStats
{
    public PassiveKind Kind { get; init; } = PassiveKind.None;

    public int Value { get; init; }

    public StatusKind Status { get; init; } = StatusKind.Burn;

    public static PassiveStats None { get; } = new();

    public string Describe() => Kind switch
    {
        PassiveKind.ClashPower => $"+{Value} clash power",
        PassiveKind.Desperation => $"+{Value}% damage below half health",
        PassiveKind.Composure => $"+{Value} Sanity per hit",
        PassiveKind.StatusOnHit => $"hits add {Value} {Status}",
        PassiveKind.Sturdy => $"{Value}% less Stagger damage",
        _ => "no passive",
    };
}
