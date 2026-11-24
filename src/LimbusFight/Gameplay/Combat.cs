namespace LimbusFight.Gameplay;

/// <summary>The seven Sin affinities. Landing a move fills its Sin's resource (spent by E.G.O in Step 6).</summary>
public enum Sin
{
    Wrath,
    Lust,
    Sloth,
    Gluttony,
    Gloom,
    Pride,
    Envy,
}

/// <summary>Physical damage types; each fighter resists them differently.</summary>
public enum DamageType
{
    Slash,
    Pierce,
    Blunt,
}

/// <summary>Status effects: stacks with a potency (how strong) and a count (how many more times it triggers).</summary>
public enum StatusKind
{
    Burn,
    Bleed,
    Tremor,
    Rupture,
    Sinking,
    Poise,
    Charge,
}

/// <summary>A status a move applies: potency and count are added to any existing stack.</summary>
public sealed record StatusApply(StatusKind Kind, int Potency, int Count);

/// <summary>A status currently on a fighter.</summary>
public struct StatusStack
{
    public int Potency;
    public int Count;

    public readonly bool Active => Count > 0 && Potency > 0;
}

/// <summary>What happened this frame, for the popups and the HUD. Pure data.</summary>
public enum CombatEventKind
{
    Damage,
    Clash,
    Stagger,
    Panic,
    StatusDamage,
    Crit,
    Ko,
    TimeOver,
    RoundStart,
    Ego,
    Counter,
    Guard,
    Charge,
}

public sealed record CombatEvent(long Frame, CombatEventKind Kind, float X, float Y, string Text, bool FromPlayer1);

/// <summary>
/// The shared combat numbers (same for every fighter) and helpers. Limbus systems as fighting-game
/// rules: no coin flips, so every number here is fixed and the whole fight is deterministic.
/// </summary>
public static class CombatRules
{
    public const int SanityMin = -45;
    public const int SanityMax = 45;
    public const int SanityOnHitLanded = 2;
    public const int SanityOnHitTaken = -2;
    public const int SanityOnClashWon = 5;
    public const int SanityOnClashLost = -5;

    /// <summary>Panic lasts this long, then Sanity returns to 0.</summary>
    public const int PanicFrames = 180;

    /// <summary>A Staggered fighter cannot act for this long and takes extra damage.</summary>
    public const int StaggerFrames = 120;

    public const float StaggeredDamageMultiplier = 1.5f;

    /// <summary>A panicking fighter deals less damage and gains no Sin resources.</summary>
    public const float PanicDamageMultiplier = 0.8f;

    public const float CritMultiplier = 1.2f;

    /// <summary>Poise: each hit adds potency x this to a crit meter; at 100 the hit is critical.</summary>
    public const int PoisePerPotency = 5;

    public const int ClashHitstop = 8;
    public const int ClashLoserRecoil = 16;
    public const int ClashTieRecoil = 10;
    public const float ClashPushback = 9f;

    public const int SinCap = 10;

    /// <summary>A counter that lands knocks the attacker back for this long.</summary>
    public const int CounterRecoil = 24;

    public const int SanityOnCounter = 5;
    public const int StatusCap = 99;

    /// <summary>Burn deals its potency once per second.</summary>
    public const int BurnInterval = 60;

    /// <summary>Round length: 60 seconds.</summary>
    public const int RoundFrames = 60 * 60;

    /// <summary>Pause after a K.O. or time over before the next round.</summary>
    public const int RoundEndFrames = 150;

    /// <summary>Sanity changes damage a little instead of coin flips: +/-10% at the extremes.</summary>
    public static float SanityMultiplier(int sanity) => 1f + (sanity / 450f);

    /// <summary>Clash power: the move's power plus Sanity (+/-3 at the extremes).</summary>
    public static float ClashPower(AttackData attack, int sanity) => attack.ClashPower + (sanity / 15f);

    /// <summary>Limbus words for a resistance multiplier.</summary>
    public static string ResistLabel(float multiplier) => multiplier switch
    {
        >= 1.99f => "Fatal",
        >= 1.49f => "Weak",
        > 1.01f => "Weak",
        >= 0.99f => "Normal",
        >= 0.74f => "Endured",
        > 0.01f => "Ineffective",
        _ => "Immune",
    };
}
