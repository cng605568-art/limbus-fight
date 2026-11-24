namespace LimbusFight.Gameplay;

/// <summary>
/// Frame data and hitbox geometry for one attack. All timings are in 60 Hz frames, all
/// distances in stage pixels, and geometry is described for a fighter facing RIGHT
/// (it is mirrored automatically when facing left).
/// </summary>
/// <summary>Which button a move is on.</summary>
public enum MoveSlot
{
    Skill1,
    Skill2,
    Skill3,
    Ego,
}

/// <summary>A Sin and how many of its resources an E.G.O costs.</summary>
public sealed record SinCost(Sin Sin, int Amount);

public sealed record AttackData
{
    /// <summary>The button this move is on (decides its poses and effects).</summary>
    public MoveSlot Slot { get; init; } = MoveSlot.Skill1;

    /// <summary>Coins = hits: each coin is one hit, spread through the active frames.</summary>
    public int Coins { get; init; } = 1;

    /// <summary>Frames between coin hits.</summary>
    public int CoinInterval { get; init; } = 4;

    /// <summary>E.G.O only: the Sin resources it costs (all must be available).</summary>
    public IReadOnlyList<SinCost> SinCost { get; init; } = Array.Empty<SinCost>();

    /// <summary>E.G.O grade shown on use: ZAYIN, TETH, HE, WAW or ALEPH.</summary>
    public string Grade { get; init; } = string.Empty;

    /// <summary>Spends this much Charge (if the fighter has it) for <see cref="ChargeBonusDamage"/> per hit.</summary>
    public int ChargeCost { get; init; }

    public int ChargeBonusDamage { get; init; }

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

    // ---- Step 5: combat --------------------------------------------------------------------

    /// <summary>Health damage before resistances, Sanity and Stagger.</summary>
    public int Damage { get; init; } = 8;

    /// <summary>How much of the defender's Stagger bar a hit takes.</summary>
    public int StaggerDamage { get; init; } = 6;

    /// <summary>When two hitboxes meet, the higher clash power wins.</summary>
    public float ClashPower { get; init; } = 10f;

    public DamageType DamageType { get; init; } = DamageType.Blunt;

    /// <summary>The Sin whose resource fills when this lands.</summary>
    public Sin Sin { get; init; } = Sin.Wrath;

    /// <summary>Statuses added to the defender on hit.</summary>
    public IReadOnlyList<StatusApply> OnHit { get; init; } = Array.Empty<StatusApply>();

    /// <summary>Statuses the attacker gains on hit (e.g. Poise, Charge).</summary>
    public IReadOnlyList<StatusApply> SelfOnHit { get; init; } = Array.Empty<StatusApply>();

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

    /// <summary>Training box Skill 2: slower, harder, Pierce.</summary>
    public static AttackData DefaultSkill2 { get; } = TestPunch with
    {
        Slot = MoveSlot.Skill2, Name = "Skill 2", Startup = 9, Active = 4, Recovery = 14, Hitstun = 20, Hitstop = 10, Pushback = 9f,
        HitboxOffsetX = 18f, HitboxWidth = 118f, HitboxOffsetY = -130f, HitboxHeight = 40f, LimbHurtboxWidth = 100f,
        Damage = 12, StaggerDamage = 10, ClashPower = 13f, DamageType = DamageType.Pierce, Sin = Sin.Gloom,
    };

    /// <summary>Training box Skill 3: two coins (two hits), Slash.</summary>
    public static AttackData DefaultSkill3 { get; } = TestPunch with
    {
        Slot = MoveSlot.Skill3, Name = "Skill 3", Startup = 12, Active = 10, Recovery = 18, Hitstun = 18, Hitstop = 8, Pushback = 6f,
        HitboxOffsetX = 10f, HitboxWidth = 140f, HitboxOffsetY = -170f, HitboxHeight = 90f, LimbHurtboxWidth = 110f,
        Damage = 9, StaggerDamage = 8, ClashPower = 16f, DamageType = DamageType.Slash, Sin = Sin.Lust, Coins = 2, CoinInterval = 6,
    };

    /// <summary>Training box E.G.O: costs 4 Wrath, three coins, big damage.</summary>
    public static AttackData DefaultEgo { get; } = TestPunch with
    {
        Slot = MoveSlot.Ego, Name = "E.G.O", Startup = 14, Active = 14, Recovery = 22, Hitstun = 26, Hitstop = 12, Pushback = 10f,
        HitboxOffsetX = 0f, HitboxWidth = 190f, HitboxOffsetY = -200f, HitboxHeight = 160f, LimbHurtboxWidth = 120f,
        Damage = 14, StaggerDamage = 14, ClashPower = 25f, DamageType = DamageType.Slash, Sin = Sin.Wrath, Coins = 3, CoinInterval = 6,
        SinCost = new[] { new SinCost(Sin.Wrath, 4) }, Grade = "TETH",
    };
}
