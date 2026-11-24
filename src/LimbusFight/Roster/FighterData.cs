using System.Text.Json.Serialization;
using LimbusFight.Animation;
using LimbusFight.Gameplay;

namespace LimbusFight.Roster;

/// <summary>A box relative to the fighter's feet: X forward from the feet, Y up is negative (stage pixels).</summary>
public sealed class BoxData
{
    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}

/// <summary>Body boxes (centred on the feet).</summary>
public sealed class BodyData
{
    public float HurtboxWidth { get; set; } = FighterStats.Default.HurtboxWidth;

    public float HurtboxHeight { get; set; } = FighterStats.Default.HurtboxHeight;

    public float PushboxWidth { get; set; } = FighterStats.Default.PushboxWidth;

    public float PushboxHeight { get; set; } = FighterStats.Default.PushboxHeight;
}

/// <summary>Health, Stagger bar and resistances (Step 5).</summary>
public sealed class CombatData
{
    public int MaxHp { get; set; } = FighterStats.Default.MaxHp;

    public int StaggerMax { get; set; } = FighterStats.Default.StaggerMax;

    /// <summary>Damage multipliers: 2 = Fatal, 1.5 = Weak, 1 = Normal, 0.75 = Endured, 0.5 = Ineffective.</summary>
    public float SlashResist { get; set; } = 1f;

    public float PierceResist { get; set; } = 1f;

    public float BluntResist { get; set; } = 1f;
}

/// <summary>A status a move applies, as written in the fighter file.</summary>
public sealed class StatusData
{
    public StatusKind Status { get; set; } = StatusKind.Burn;

    public int Potency { get; set; } = 1;

    public int Count { get; set; } = 1;

    public StatusApply ToApply() => new(Status, Potency, Count);
}

/// <summary>One Sin cost of an E.G.O.</summary>
public sealed class SinCostData
{
    public Sin Sin { get; set; } = Sin.Wrath;

    public int Amount { get; set; } = 1;
}

/// <summary>The defense button (Step 6).</summary>
public sealed class DefenseData
{
    public DefenseKind Kind { get; set; } = DefenseStats.Default.Kind;

    public float GuardDamage { get; set; } = DefenseStats.Default.GuardDamage;

    public float GuardStagger { get; set; } = DefenseStats.Default.GuardStagger;

    public int TotalFrames { get; set; } = DefenseStats.Default.TotalFrames;

    public int WindowStart { get; set; } = DefenseStats.Default.WindowStart;

    public int WindowEnd { get; set; } = DefenseStats.Default.WindowEnd;

    public float EvadeSpeed { get; set; } = DefenseStats.Default.EvadeSpeed;

    public DefenseStats ToStats() => new()
    {
        Kind = Kind, GuardDamage = GuardDamage, GuardStagger = GuardStagger, TotalFrames = TotalFrames,
        WindowStart = WindowStart, WindowEnd = WindowEnd, EvadeSpeed = EvadeSpeed,
    };
}

/// <summary>The always-on passive (Step 6).</summary>
public sealed class PassiveData
{
    public PassiveKind Kind { get; set; } = PassiveKind.None;

    public int Value { get; set; }

    public StatusKind Status { get; set; } = StatusKind.Burn;

    public PassiveStats ToStats() => new() { Kind = Kind, Value = Value, Status = Status };
}

/// <summary>Walking and jumping, per 60 Hz frame.</summary>
public sealed class MovementData
{
    public float WalkForwardSpeed { get; set; } = FighterStats.Default.WalkForwardSpeed;

    public float WalkBackwardSpeed { get; set; } = FighterStats.Default.WalkBackwardSpeed;

    public float JumpLaunchSpeed { get; set; } = FighterStats.Default.JumpLaunchSpeed;

    public float Gravity { get; set; } = FighterStats.Default.Gravity;

    public float JumpForwardSpeed { get; set; } = FighterStats.Default.JumpHorizontalSpeed;

    public int JumpSquatFrames { get; set; } = FighterStats.Default.JumpSquatFrames;

    public int LandingFrames { get; set; } = FighterStats.Default.LandingFrames;
}

/// <summary>One move: frame data, hitbox and combat numbers, on one button.</summary>
public sealed class MoveData
{
    /// <summary>Buttons, in order: Skill 1 (J, called "punch" since Step 1), Skill 2 (K), Skill 3 (L), E.G.O (I).</summary>
    public static readonly string[] Buttons = { "punch", "skill2", "skill3", "ego" };

    /// <summary>A move with the training box's numbers for a button.</summary>
    public static MoveData Default(string button)
    {
        AttackData a = button switch
        {
            "skill2" => AttackData.DefaultSkill2,
            "skill3" => AttackData.DefaultSkill3,
            "ego" => AttackData.DefaultEgo,
            _ => AttackData.TestPunch,
        };

        return new MoveData
        {
            Id = button == "punch" ? "punch" : button,
            Name = a.Name,
            Button = button,
            Startup = a.Startup, Active = a.Active, Recovery = a.Recovery, Hitstun = a.Hitstun, Hitstop = a.Hitstop, Pushback = a.Pushback,
            Hitbox = new BoxData { X = a.HitboxOffsetX, Y = a.HitboxOffsetY, Width = a.HitboxWidth, Height = a.HitboxHeight },
            ArmHurtboxWidth = a.LimbHurtboxWidth,
            Damage = a.Damage, StaggerDamage = a.StaggerDamage, ClashPower = a.ClashPower, DamageType = a.DamageType, Sin = a.Sin,
            Coins = a.Coins, CoinInterval = a.CoinInterval, Grade = a.Grade.Length > 0 ? a.Grade : null,
            SinCost = a.SinCost.Select(c => new SinCostData { Sin = c.Sin, Amount = c.Amount }).ToList(),
        };
    }

    /// <summary>False switches the move off (its button does nothing).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hits: each coin is one hit, <see cref="CoinInterval"/> frames apart.</summary>
    public int Coins { get; set; } = 1;

    public int CoinInterval { get; set; } = 4;

    /// <summary>E.G.O: Sin resources it costs.</summary>
    public List<SinCostData> SinCost { get; set; } = new();

    /// <summary>E.G.O grade label (ZAYIN, TETH, HE, WAW, ALEPH).</summary>
    public string? Grade { get; set; }

    /// <summary>Spends this much Charge, if the fighter has it, for <see cref="ChargeBonusDamage"/> extra damage per hit.</summary>
    public int ChargeCost { get; set; }

    public int ChargeBonusDamage { get; set; }

    public string Id { get; set; } = "punch";

    public string Name { get; set; } = "Punch";

    /// <summary>The button that does it: "punch" for now.</summary>
    public string Button { get; set; } = "punch";

    public int Startup { get; set; } = AttackData.TestPunch.Startup;

    public int Active { get; set; } = AttackData.TestPunch.Active;

    public int Recovery { get; set; } = AttackData.TestPunch.Recovery;

    public int Hitstun { get; set; } = AttackData.TestPunch.Hitstun;

    public int Hitstop { get; set; } = AttackData.TestPunch.Hitstop;

    public float Pushback { get; set; } = AttackData.TestPunch.Pushback;

    public BoxData Hitbox { get; set; } = new()
    {
        X = AttackData.TestPunch.HitboxOffsetX,
        Y = AttackData.TestPunch.HitboxOffsetY,
        Width = AttackData.TestPunch.HitboxWidth,
        Height = AttackData.TestPunch.HitboxHeight,
    };

    /// <summary>How far the extended arm can be hit, from the last startup frame through recovery.</summary>
    public float ArmHurtboxWidth { get; set; } = AttackData.TestPunch.LimbHurtboxWidth;

    public int Damage { get; set; } = AttackData.TestPunch.Damage;

    public int StaggerDamage { get; set; } = AttackData.TestPunch.StaggerDamage;

    public float ClashPower { get; set; } = AttackData.TestPunch.ClashPower;

    public DamageType DamageType { get; set; } = AttackData.TestPunch.DamageType;

    public Sin Sin { get; set; } = AttackData.TestPunch.Sin;

    /// <summary>Statuses put on the opponent when it hits.</summary>
    public List<StatusData> OnHit { get; set; } = new();

    /// <summary>Statuses the attacker gains when it hits.</summary>
    public List<StatusData> SelfOnHit { get; set; } = new();

    public MoveSlot Slot => Button switch
    {
        "skill2" => MoveSlot.Skill2,
        "skill3" => MoveSlot.Skill3,
        "ego" => MoveSlot.Ego,
        _ => MoveSlot.Skill1,
    };

    public AttackData ToAttack() => new()
    {
        Slot = Slot,
        Coins = Coins,
        CoinInterval = CoinInterval,
        SinCost = SinCost.Select(c => new SinCost(c.Sin, c.Amount)).ToArray(),
        Grade = Grade ?? string.Empty,
        ChargeCost = ChargeCost,
        ChargeBonusDamage = ChargeBonusDamage,
        Name = Name,
        Startup = Startup,
        Active = Active,
        Recovery = Recovery,
        Hitstun = Hitstun,
        Hitstop = Hitstop,
        Pushback = Pushback,
        HitboxOffsetX = Hitbox.X,
        HitboxOffsetY = Hitbox.Y,
        HitboxWidth = Hitbox.Width,
        HitboxHeight = Hitbox.Height,
        LimbHurtboxWidth = ArmHurtboxWidth,
        Damage = Damage,
        StaggerDamage = StaggerDamage,
        ClashPower = ClashPower,
        DamageType = DamageType,
        Sin = Sin,
        OnHit = OnHit.Select(st => st.ToApply()).ToArray(),
        SelfOnHit = SelfOnHit.Select(st => st.ToApply()).ToArray(),
    };
}

/// <summary>
/// One fighter (a Sinner ID, a Boss, or a test box): everything about how it plays and looks, in
/// one JSON file in %LOCALAPPDATA%\LimbusFight\fighters. Holds numbers and Limbus picture names
/// only, never pictures, so fighter files can be shared without sharing game files.
/// </summary>
public sealed class FighterData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = "Training Box";

    public BodyData Body { get; set; } = new();

    public MovementData Movement { get; set; } = new();

    public CombatData Combat { get; set; } = new();

    public DefenseData Defense { get; set; } = new();

    public PassiveData Passive { get; set; } = new();

    public List<MoveData> Moves { get; set; } = MoveData.Buttons.Select(MoveData.Default).ToList();

    /// <summary>How it looks: clips of Limbus sprite names (see Animation/LookData).</summary>
    public LookData Look { get; set; } = new();

    /// <summary>The move on Skill 1 / the punch button (created if missing).</summary>
    [JsonIgnore]
    public MoveData Punch => Move("punch");

    /// <summary>The move on a button ("punch", "skill2", "skill3", "ego"); created with defaults if missing.</summary>
    public MoveData Move(string button)
    {
        MoveData? move = Moves.FirstOrDefault(m => m.Button == button);
        if (move is null)
        {
            move = MoveData.Default(button);
            Moves.Add(move);
        }

        return move;
    }

    public FighterStats ToStats() => new()
    {
        Name = Name,
        HurtboxWidth = Body.HurtboxWidth,
        HurtboxHeight = Body.HurtboxHeight,
        PushboxWidth = Body.PushboxWidth,
        PushboxHeight = Body.PushboxHeight,
        WalkForwardSpeed = Movement.WalkForwardSpeed,
        WalkBackwardSpeed = Movement.WalkBackwardSpeed,
        JumpLaunchSpeed = Movement.JumpLaunchSpeed,
        Gravity = Movement.Gravity,
        JumpHorizontalSpeed = Movement.JumpForwardSpeed,
        JumpSquatFrames = Movement.JumpSquatFrames,
        LandingFrames = Movement.LandingFrames,
        MaxHp = Combat.MaxHp,
        StaggerMax = Combat.StaggerMax,
        SlashResist = Combat.SlashResist,
        PierceResist = Combat.PierceResist,
        BluntResist = Combat.BluntResist,
        Punch = Punch.ToAttack(),
        Skill2 = Move("skill2") is { Enabled: true } s2 ? s2.ToAttack() : null,
        Skill3 = Move("skill3") is { Enabled: true } s3 ? s3.ToAttack() : null,
        Ego = Move("ego") is { Enabled: true } eg ? eg.ToAttack() : null,
        Defense = Defense.ToStats(),
        Passive = Passive.ToStats(),
    };

    /// <summary>Clamps every number to a playable range (hand-edited files, typos).</summary>
    public void Sanitise()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Fighter" : Name.Trim();
        if (Name.Length > 40)
        {
            Name = Name[..40];
        }

        Body ??= new BodyData();
        Movement ??= new MovementData();
        Combat ??= new CombatData();
        Combat.MaxHp = Math.Clamp(Combat.MaxHp, 1, 9999);
        Combat.StaggerMax = Math.Clamp(Combat.StaggerMax, 1, 9999);
        Combat.SlashResist = Clamp(Combat.SlashResist, 0, 4, 1);
        Combat.PierceResist = Clamp(Combat.PierceResist, 0, 4, 1);
        Combat.BluntResist = Clamp(Combat.BluntResist, 0, 4, 1);
        Moves ??= new List<MoveData>();
        Moves.RemoveAll(m => m is null);
        Look ??= new LookData();
        Look.Sanitise();

        Body.HurtboxWidth = Clamp(Body.HurtboxWidth, 10, 600, 84);
        Body.HurtboxHeight = Clamp(Body.HurtboxHeight, 10, 600, 182);
        Body.PushboxWidth = Clamp(Body.PushboxWidth, 10, 600, 70);
        Body.PushboxHeight = Clamp(Body.PushboxHeight, 10, 600, 170);

        Movement.WalkForwardSpeed = Clamp(Movement.WalkForwardSpeed, 0, 30, 4);
        Movement.WalkBackwardSpeed = Clamp(Movement.WalkBackwardSpeed, 0, 30, 3.2f);
        Movement.JumpLaunchSpeed = Clamp(Movement.JumpLaunchSpeed, 1, 60, 20);
        Movement.Gravity = Clamp(Movement.Gravity, 0.05f, 5, 1);
        Movement.JumpForwardSpeed = Clamp(Movement.JumpForwardSpeed, 0, 30, 4.5f);
        Movement.JumpSquatFrames = Math.Clamp(Movement.JumpSquatFrames, 1, 30);
        Movement.LandingFrames = Math.Clamp(Movement.LandingFrames, 1, 30);

        foreach (MoveData move in Moves)
        {
            move.Id = string.IsNullOrWhiteSpace(move.Id) ? "move" : move.Id;
            move.Name = string.IsNullOrWhiteSpace(move.Name) ? move.Id : move.Name;
            move.Button = string.IsNullOrWhiteSpace(move.Button) ? "punch" : move.Button;
            move.Startup = Math.Clamp(move.Startup, 1, 120);
            move.Active = Math.Clamp(move.Active, 1, 120);
            move.Recovery = Math.Clamp(move.Recovery, 0, 180);
            move.Hitstun = Math.Clamp(move.Hitstun, 0, 180);
            move.Hitstop = Math.Clamp(move.Hitstop, 0, 60);
            move.Pushback = Clamp(move.Pushback, 0, 60, 7);
            move.Hitbox ??= new BoxData();
            move.Hitbox.X = Clamp(move.Hitbox.X, -400, 800, 22);
            move.Hitbox.Y = Clamp(move.Hitbox.Y, -800, 200, -154);
            move.Hitbox.Width = Clamp(move.Hitbox.Width, 4, 800, 96);
            move.Hitbox.Height = Clamp(move.Hitbox.Height, 4, 800, 30);
            move.ArmHurtboxWidth = Clamp(move.ArmHurtboxWidth, 0, 800, 80);
            move.Damage = Math.Clamp(move.Damage, 0, 999);
            move.StaggerDamage = Math.Clamp(move.StaggerDamage, 0, 999);
            move.ClashPower = Clamp(move.ClashPower, 0, 999, 10);
            move.DamageType = Enum.IsDefined(move.DamageType) ? move.DamageType : DamageType.Blunt;
            move.Sin = Enum.IsDefined(move.Sin) ? move.Sin : Sin.Wrath;
            move.OnHit ??= new List<StatusData>();
            move.SelfOnHit ??= new List<StatusData>();
            foreach (StatusData st in move.OnHit.Concat(move.SelfOnHit))
            {
                st.Status = Enum.IsDefined(st.Status) ? st.Status : StatusKind.Burn;
                st.Potency = Math.Clamp(st.Potency, 0, CombatRules.StatusCap);
                st.Count = Math.Clamp(st.Count, 0, CombatRules.StatusCap);
            }

            move.OnHit.RemoveAll(st => st is null || st.Potency == 0 || st.Count == 0);
            move.SelfOnHit.RemoveAll(st => st is null || st.Potency == 0 || st.Count == 0);

            move.Coins = Math.Clamp(move.Coins, 1, 10);
            move.CoinInterval = Math.Clamp(move.CoinInterval, 1, 60);
            move.Active = Math.Max(move.Active, ((move.Coins - 1) * move.CoinInterval) + 1); // room for every coin
            move.SinCost ??= new List<SinCostData>();
            move.SinCost.RemoveAll(c => c is null || c.Amount <= 0);
            foreach (SinCostData c in move.SinCost)
            {
                c.Sin = Enum.IsDefined(c.Sin) ? c.Sin : Sin.Wrath;
                c.Amount = Math.Clamp(c.Amount, 1, CombatRules.SinCap);
            }

            move.ChargeCost = Math.Clamp(move.ChargeCost, 0, CombatRules.StatusCap);
            move.ChargeBonusDamage = Math.Clamp(move.ChargeBonusDamage, 0, 999);
        }

        // Files from Steps 4-5 only have the punch: add the other buttons with training box numbers.
        foreach (string button in MoveData.Buttons)
        {
            _ = Move(button);
        }

        Defense ??= new DefenseData();
        Defense.Kind = Enum.IsDefined(Defense.Kind) ? Defense.Kind : DefenseKind.Guard;
        Defense.GuardDamage = Clamp(Defense.GuardDamage, 0, 1, 0.25f);
        Defense.GuardStagger = Clamp(Defense.GuardStagger, 0, 2, 1f);
        Defense.TotalFrames = Math.Clamp(Defense.TotalFrames, 2, 120);
        Defense.WindowStart = Math.Clamp(Defense.WindowStart, 1, Defense.TotalFrames);
        Defense.WindowEnd = Math.Clamp(Defense.WindowEnd, Defense.WindowStart, Defense.TotalFrames);
        Defense.EvadeSpeed = Clamp(Defense.EvadeSpeed, 0, 40, 7);

        Passive ??= new PassiveData();
        Passive.Kind = Enum.IsDefined(Passive.Kind) ? Passive.Kind : PassiveKind.None;
        Passive.Status = Enum.IsDefined(Passive.Status) ? Passive.Status : StatusKind.Burn;
        Passive.Value = Math.Clamp(Passive.Value, 0, 999);
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
