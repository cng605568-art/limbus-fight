using LimbusFight.Gameplay;

namespace LimbusFight.Roster;

/// <summary>
/// The built-in fighters: the Sinners (and later Bosses) as fighter files. They are written to the
/// fighters folder on first run and refreshed when a newer game version changes their numbers,
/// unless the player has edited them in F2 (then <see cref="FighterData.Preset"/> is 0 and the file
/// is left alone). Looks are never overwritten: those come from the player's own Limbus files.
/// Numbers are fan-made balance for a fighting game, inspired by each Sinner's base Identity.
/// </summary>
public static class Presets
{
    /// <summary>Bump when preset numbers change so existing (unedited) preset files get refreshed.</summary>
    public const int Version = 3;

    public sealed record Preset(string Id, Func<FighterData> Make);

    public static IReadOnlyList<Preset> All { get; } = new List<Preset>
    {
        new("yi-sang", YiSang),
        new("faust", Faust),
        new("don-quixote", DonQuixote),
        new("ryoshu", Ryoshu),
        new("meursault", Meursault),
        new("hong-lu", HongLu),
        new("heathcliff", Heathcliff),
        new("ishmael", Ishmael),
        new("rodion", Rodion),
        new("sinclair", Sinclair),
        new("outis", Outis),
        new("gregor", Gregor),
        new("yi-sang-blade-lineage", YiSangBladeLineage),
        new("heathcliff-shi", HeathcliffShi),
        new("heathcliff-r-corp", HeathcliffRabbit),
        new("vergilius", Vergilius),
        new("erlking-heathcliff", ErlkingHeathcliff),
    };

    /// <summary>Writes missing preset files and refreshes outdated unedited ones. Returns problems, if any.</summary>
    public static string? EnsureWritten()
    {
        string? problem = null;
        foreach (Preset preset in All)
        {
            string path = FighterStore.PathFor(preset.Id);
            FighterData fresh = preset.Make();
            fresh.Preset = Version;
            fresh.Sanitise();

            if (File.Exists(path))
            {
                FighterData old = FighterStore.Load(preset.Id, out string? loadProblem);
                if (loadProblem is null && (old.Preset == 0 || old.Preset >= Version))
                {
                    continue; // edited by the player, or already current
                }

                fresh.Look = old.Look; // keep a look the player (or the auto roster) made
            }

            problem ??= FighterStore.Save(preset.Id, fresh);
        }

        return problem;
    }

    // ---- The Sinners ------------------------------------------------------------------------

    private static FighterData YiSang()
    {
        FighterData f = Base("Yi Sang", "#3E6FB5");
        f.Combat.MaxHp = 180;
        f.Combat.StaggerMax = 40;
        f.Combat.PierceResist = 0.75f;
        f.Combat.BluntResist = 1.5f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.StatusOnHit, Value = 1, Status = StatusKind.Sinking };
        f.Moves = new()
        {
            M("punch", "Grade 1 Cut", 6, 3, 11, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Slash, Sin.Gloom, reach: 104),
            M("skill2", "Pierce Through", 9, 4, 15, hitstun: 18, dmg: 11, stagger: 6, clash: 13, DamageType.Pierce, Sin.Sloth, reach: 128,
                onHit: new[] { S(StatusKind.Sinking, 2, 2) }),
            M("skill3", "Rip Space", 12, 0, 19, hitstun: 20, dmg: 8, stagger: 7, clash: 16, DamageType.Slash, Sin.Gloom, reach: 120,
                coins: 3, interval: 5, onHit: new[] { S(StatusKind.Sinking, 1, 2) }),
            Ego("Crow's Eye View", "ZAYIN", Sin.Sloth, 3, 14, 4, 22, dmg: 12, stagger: 10, clash: 24, DamageType.Pierce, Sin.Sloth, reach: 150,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Sinking, 3, 3) }),
        };
        return f;
    }

    private static FighterData Faust()
    {
        FighterData f = Base("Faust", "#C9A93E");
        f.Combat.MaxHp = 205;
        f.Combat.StaggerMax = 52;
        f.Combat.BluntResist = 0.75f;
        f.Combat.SlashResist = 1.25f;
        f.Movement.WalkForwardSpeed = 3.4f;
        f.Movement.WalkBackwardSpeed = 2.8f;
        f.Movement.JumpLaunchSpeed = 18.5f;
        f.Body.HurtboxWidth = 90;
        f.Defense = new DefenseData { Kind = DefenseKind.Guard, GuardDamage = 0.15f, GuardStagger = 0.75f };
        f.Passive = new PassiveData { Kind = PassiveKind.Sturdy, Value = 25 };
        f.Moves = new()
        {
            M("punch", "Strike", 7, 3, 13, hitstun: 16, dmg: 9, stagger: 6, clash: 11, DamageType.Blunt, Sin.Pride, reach: 100),
            M("skill2", "Shatter", 11, 4, 18, hitstun: 19, dmg: 11, stagger: 9, clash: 14, DamageType.Blunt, Sin.Envy, reach: 116,
                onHit: new[] { S(StatusKind.Tremor, 3, 2) }),
            M("skill3", "Mighty Swing", 16, 6, 24, hitstun: 24, dmg: 18, stagger: 15, clash: 18, DamageType.Blunt, Sin.Pride, reach: 140),
            Ego("Representation Emitter", "ZAYIN", Sin.Pride, 3, 16, 6, 24, dmg: 14, stagger: 16, clash: 25, DamageType.Blunt, Sin.Pride, reach: 170,
                coins: 2, interval: 8, onHit: new[] { S(StatusKind.Tremor, 4, 3) }),
        };
        return f;
    }

    private static FighterData DonQuixote()
    {
        FighterData f = Base("Don Quixote", "#E0D05A");
        f.Combat.MaxHp = 165;
        f.Combat.StaggerMax = 38;
        f.Combat.PierceResist = 0.75f;
        f.Combat.SlashResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4.6f;
        f.Movement.WalkBackwardSpeed = 3.6f;
        f.Movement.JumpLaunchSpeed = 21.5f;
        f.Movement.JumpForwardSpeed = 5.2f;
        f.Defense = new DefenseData { Kind = DefenseKind.Counter, WindowStart = 3, WindowEnd = 11, TotalFrames = 24 };
        f.Passive = new PassiveData { Kind = PassiveKind.ClashPower, Value = 2 };
        f.Moves = new()
        {
            M("punch", "Lance Thrust", 7, 3, 13, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Pierce, Sin.Lust, reach: 136),
            M("skill2", "Justice Charge", 10, 5, 18, hitstun: 18, dmg: 10, stagger: 7, clash: 14, DamageType.Pierce, Sin.Wrath, reach: 150,
                pushback: 12, onHit: new[] { S(StatusKind.Bleed, 2, 2) }),
            M("skill3", "Joust", 13, 0, 20, hitstun: 20, dmg: 7, stagger: 6, clash: 17, DamageType.Pierce, Sin.Lust, reach: 150,
                coins: 3, interval: 5, onHit: new[] { S(StatusKind.Bleed, 1, 2) }),
            Ego("La Sangre de Sancho", "ZAYIN", Sin.Lust, 4, 14, 4, 24, dmg: 13, stagger: 10, clash: 26, DamageType.Pierce, Sin.Gluttony, reach: 170,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Bleed, 3, 3) }),
        };
        return f;
    }

    private static FighterData Ryoshu()
    {
        FighterData f = Base("Ryoshu", "#B8322E");
        f.Combat.MaxHp = 172;
        f.Combat.StaggerMax = 36;
        f.Combat.SlashResist = 0.75f;
        f.Combat.PierceResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4.2f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.Desperation, Value = 25 };
        f.Moves = new()
        {
            M("punch", "Swift Cut", 6, 3, 12, hitstun: 15, dmg: 8, stagger: 5, clash: 10, DamageType.Slash, Sin.Wrath, reach: 108,
                selfOnHit: new[] { S(StatusKind.Poise, 1, 1) }),
            M("skill2", "Bloodlust", 9, 4, 16, hitstun: 18, dmg: 12, stagger: 6, clash: 13, DamageType.Slash, Sin.Wrath, reach: 118,
                selfOnHit: new[] { S(StatusKind.Poise, 2, 1) }),
            M("skill3", "Raging Cut", 14, 0, 22, hitstun: 22, dmg: 11, stagger: 8, clash: 17, DamageType.Slash, Sin.Pride, reach: 128,
                coins: 2, interval: 7, onHit: new[] { S(StatusKind.Burn, 2, 2) }),
            Ego("Forest for the Flames", "ZAYIN", Sin.Lust, 4, 15, 4, 24, dmg: 14, stagger: 10, clash: 26, DamageType.Slash, Sin.Wrath, reach: 165,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Burn, 3, 3) }),
        };
        return f;
    }

    // ---- Wave 2: the other eight Sinners ----------------------------------------------------

    private static FighterData Meursault()
    {
        FighterData f = Base("Meursault", "#4A5A73");
        f.Combat.MaxHp = 210;
        f.Combat.StaggerMax = 50;
        f.Combat.BluntResist = 0.75f;
        f.Combat.PierceResist = 1.25f;
        f.Movement.WalkForwardSpeed = 3.5f;
        f.Movement.WalkBackwardSpeed = 2.9f;
        f.Movement.JumpLaunchSpeed = 18.5f;
        f.Body.HurtboxWidth = 92;
        f.Defense = new DefenseData { Kind = DefenseKind.Guard, GuardDamage = 0.2f, GuardStagger = 0.8f };
        f.Passive = new PassiveData { Kind = PassiveKind.Sturdy, Value = 20 };
        f.Moves = new()
        {
            M("punch", "Strike Down", 7, 3, 13, hitstun: 16, dmg: 9, stagger: 6, clash: 11, DamageType.Blunt, Sin.Sloth, reach: 104),
            M("skill2", "Pulverize", 11, 4, 18, hitstun: 19, dmg: 11, stagger: 9, clash: 14, DamageType.Blunt, Sin.Pride, reach: 118,
                onHit: new[] { S(StatusKind.Tremor, 2, 2) }),
            M("skill3", "Suppress", 15, 0, 22, hitstun: 22, dmg: 10, stagger: 10, clash: 17, DamageType.Blunt, Sin.Gloom, reach: 130, coins: 2, interval: 8),
            Ego("Chains of Others", "ZAYIN", Sin.Pride, 3, 15, 4, 24, dmg: 11, stagger: 14, clash: 24, DamageType.Blunt, Sin.Pride, reach: 180,
                coins: 2, interval: 8, onHit: new[] { S(StatusKind.Rupture, 3, 3) }),
        };
        return f;
    }

    private static FighterData HongLu()
    {
        FighterData f = Base("Hong Lu", "#2E8B57");
        f.Combat.MaxHp = 175;
        f.Combat.StaggerMax = 42;
        f.Combat.SlashResist = 0.75f;
        f.Combat.BluntResist = 1.25f;
        f.Movement.WalkForwardSpeed = 4.1f;
        f.Defense = new DefenseData { Kind = DefenseKind.Counter, WindowStart = 2, WindowEnd = 10, TotalFrames = 22 };
        f.Passive = new PassiveData { Kind = PassiveKind.Composure, Value = 3 };
        f.Moves = new()
        {
            M("punch", "Swipe", 6, 3, 12, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Slash, Sin.Gluttony, reach: 112),
            M("skill2", "Heel Turn", 9, 4, 16, hitstun: 18, dmg: 10, stagger: 7, clash: 13, DamageType.Pierce, Sin.Gloom, reach: 124,
                onHit: new[] { S(StatusKind.Rupture, 2, 2) }),
            M("skill3", "Ascent", 13, 0, 20, hitstun: 20, dmg: 7, stagger: 6, clash: 16, DamageType.Slash, Sin.Gloom, reach: 126,
                coins: 3, interval: 5, onHit: new[] { S(StatusKind.Rupture, 1, 1) }),
            Ego("Land of Illusion", "ZAYIN", Sin.Gloom, 3, 14, 4, 24, dmg: 12, stagger: 10, clash: 24, DamageType.Slash, Sin.Gloom, reach: 165,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Sinking, 3, 2) }),
        };
        return f;
    }

    private static FighterData Heathcliff()
    {
        FighterData f = Base("Heathcliff", "#5B3F8C");
        f.Combat.MaxHp = 175;
        f.Combat.StaggerMax = 38;
        f.Combat.BluntResist = 0.75f;
        f.Combat.SlashResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4.3f;
        f.Movement.WalkBackwardSpeed = 3.3f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.Desperation, Value = 30 };
        f.Moves = new()
        {
            M("punch", "Bat Smash", 7, 3, 13, hitstun: 16, dmg: 9, stagger: 6, clash: 11, DamageType.Blunt, Sin.Envy, reach: 110),
            M("skill2", "Upswing", 10, 4, 18, hitstun: 19, dmg: 12, stagger: 8, clash: 14, DamageType.Blunt, Sin.Wrath, reach: 118,
                onHit: new[] { S(StatusKind.Tremor, 2, 2) }),
            M("skill3", "Ferocious Swing", 15, 6, 23, hitstun: 24, dmg: 19, stagger: 12, clash: 18, DamageType.Blunt, Sin.Envy, reach: 138),
            Ego("Bodysack", "ZAYIN", Sin.Envy, 3, 15, 4, 24, dmg: 13, stagger: 12, clash: 24, DamageType.Blunt, Sin.Envy, reach: 165,
                coins: 2, interval: 8, onHit: new[] { S(StatusKind.Tremor, 3, 3) }),
        };
        return f;
    }

    private static FighterData Ishmael()
    {
        FighterData f = Base("Ishmael", "#D98C3A");
        f.Combat.MaxHp = 185;
        f.Combat.StaggerMax = 46;
        f.Combat.BluntResist = 0.75f;
        f.Combat.PierceResist = 1.5f;
        f.Movement.WalkForwardSpeed = 3.8f;
        f.Defense = new DefenseData { Kind = DefenseKind.Guard, GuardDamage = 0.25f, GuardStagger = 0.7f };
        f.Passive = new PassiveData { Kind = PassiveKind.StatusOnHit, Value = 1, Status = StatusKind.Tremor };
        f.Moves = new()
        {
            M("punch", "Bash", 7, 3, 13, hitstun: 16, dmg: 8, stagger: 6, clash: 11, DamageType.Blunt, Sin.Gloom, reach: 106),
            M("skill2", "Tremble", 10, 4, 17, hitstun: 18, dmg: 10, stagger: 9, clash: 14, DamageType.Blunt, Sin.Envy, reach: 120,
                onHit: new[] { S(StatusKind.Tremor, 3, 2) }),
            M("skill3", "Wallop", 14, 0, 22, hitstun: 22, dmg: 11, stagger: 9, clash: 17, DamageType.Blunt, Sin.Gloom, reach: 128, coins: 2, interval: 8),
            Ego("Snagharpoon", "ZAYIN", Sin.Gloom, 3, 14, 4, 24, dmg: 13, stagger: 11, clash: 24, DamageType.Pierce, Sin.Gloom, reach: 200,
                coins: 2, interval: 7, onHit: new[] { S(StatusKind.Tremor, 3, 2) }),
        };
        return f;
    }

    private static FighterData Rodion()
    {
        FighterData f = Base("Rodion", "#8B1E3F");
        f.Combat.MaxHp = 178;
        f.Combat.StaggerMax = 40;
        f.Combat.SlashResist = 0.75f;
        f.Combat.BluntResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.Desperation, Value = 20 };
        f.Moves = new()
        {
            M("punch", "Chop", 7, 3, 13, hitstun: 16, dmg: 9, stagger: 5, clash: 11, DamageType.Slash, Sin.Pride, reach: 110),
            M("skill2", "Downward Swing", 11, 4, 18, hitstun: 19, dmg: 12, stagger: 7, clash: 14, DamageType.Slash, Sin.Gloom, reach: 122,
                onHit: new[] { S(StatusKind.Bleed, 2, 3) }),
            M("skill3", "Axe Combo", 14, 0, 22, hitstun: 22, dmg: 11, stagger: 8, clash: 17, DamageType.Slash, Sin.Wrath, reach: 130,
                coins: 2, interval: 7, onHit: new[] { S(StatusKind.Bleed, 2, 2) }),
            Ego("What is Cast", "ZAYIN", Sin.Pride, 3, 14, 4, 24, dmg: 13, stagger: 10, clash: 24, DamageType.Slash, Sin.Pride, reach: 165,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Bleed, 3, 3) }),
        };
        return f;
    }

    private static FighterData Sinclair()
    {
        FighterData f = Base("Sinclair", "#C8B98A");
        f.Combat.MaxHp = 168;
        f.Combat.StaggerMax = 38;
        f.Combat.PierceResist = 0.75f;
        f.Combat.SlashResist = 1.25f;
        f.Movement.WalkForwardSpeed = 4.2f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.ClashPower, Value = 1 };
        MoveData s3 = M("skill3", "Rip Apart", 13, 0, 20, hitstun: 20, dmg: 7, stagger: 6, clash: 16, DamageType.Slash, Sin.Gluttony, reach: 128,
            coins: 3, interval: 5);
        s3.ChargeCost = 3;
        s3.ChargeBonusDamage = 5;
        f.Moves = new()
        {
            M("punch", "Swipe", 6, 3, 12, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Slash, Sin.Gluttony, reach: 108,
                selfOnHit: new[] { S(StatusKind.Charge, 1, 1) }),
            M("skill2", "Pierce Through", 9, 4, 16, hitstun: 18, dmg: 10, stagger: 6, clash: 13, DamageType.Pierce, Sin.Sloth, reach: 130,
                selfOnHit: new[] { S(StatusKind.Charge, 1, 2) }),
            s3,
            Ego("Branch of Knowledge", "ZAYIN", Sin.Gluttony, 3, 14, 4, 24, dmg: 12, stagger: 10, clash: 24, DamageType.Pierce, Sin.Gluttony, reach: 170,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Rupture, 2, 2) }),
        };
        return f;
    }

    private static FighterData Outis()
    {
        FighterData f = Base("Outis", "#556B2F");
        f.Combat.MaxHp = 172;
        f.Combat.StaggerMax = 40;
        f.Combat.PierceResist = 0.75f;
        f.Combat.BluntResist = 1.25f;
        f.Movement.WalkForwardSpeed = 4f;
        f.Defense = new DefenseData { Kind = DefenseKind.Counter, WindowStart = 3, WindowEnd = 11, TotalFrames = 24 };
        f.Passive = new PassiveData { Kind = PassiveKind.Composure, Value = 2 };
        f.Moves = new()
        {
            M("punch", "Shank", 6, 3, 12, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Pierce, Sin.Pride, reach: 116,
                selfOnHit: new[] { S(StatusKind.Poise, 1, 1) }),
            M("skill2", "Strategic Thrust", 10, 4, 17, hitstun: 18, dmg: 11, stagger: 6, clash: 14, DamageType.Pierce, Sin.Gloom, reach: 140,
                selfOnHit: new[] { S(StatusKind.Poise, 2, 1) }),
            M("skill3", "Tactical Flurry", 13, 0, 21, hitstun: 20, dmg: 7, stagger: 6, clash: 16, DamageType.Pierce, Sin.Pride, reach: 134,
                coins: 3, interval: 5),
            Ego("To Pathos Mathos", "ZAYIN", Sin.Pride, 3, 14, 4, 24, dmg: 13, stagger: 10, clash: 24, DamageType.Pierce, Sin.Pride, reach: 175,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Bleed, 2, 2) }),
        };
        return f;
    }

    private static FighterData Gregor()
    {
        FighterData f = Base("Gregor", "#6B4F2A");
        f.Combat.MaxHp = 182;
        f.Combat.StaggerMax = 42;
        f.Combat.PierceResist = 0.75f;
        f.Combat.SlashResist = 1.25f;
        f.Movement.WalkForwardSpeed = 3.9f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.StatusOnHit, Value = 1, Status = StatusKind.Rupture };
        f.Moves = new()
        {
            M("punch", "Insect Jab", 6, 3, 12, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Pierce, Sin.Gloom, reach: 118),
            M("skill2", "Swipe", 10, 4, 17, hitstun: 18, dmg: 11, stagger: 7, clash: 14, DamageType.Slash, Sin.Sloth, reach: 124,
                onHit: new[] { S(StatusKind.Rupture, 2, 2) }),
            M("skill3", "Chop Up", 14, 0, 22, hitstun: 22, dmg: 11, stagger: 8, clash: 17, DamageType.Slash, Sin.Gluttony, reach: 128, coins: 2, interval: 7),
            Ego("Suddenly, One Day", "ZAYIN", Sin.Sloth, 3, 14, 4, 24, dmg: 13, stagger: 10, clash: 24, DamageType.Pierce, Sin.Sloth, reach: 170,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Rupture, 3, 2) }),
        };
        return f;
    }

    // ---- Extra Identities (Wave 3, fan-made numbers) ----------------------------------------

    private static FighterData YiSangBladeLineage()
    {
        FighterData f = Base("Yi Sang", "#2F3E5C");
        f.Name = "Blade Lineage Yi Sang";
        f.Identity = "Blade Lineage Salsu";
        f.Combat.MaxHp = 165;
        f.Combat.StaggerMax = 36;
        f.Combat.SlashResist = 0.75f;
        f.Combat.BluntResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4.6f;
        f.Movement.WalkBackwardSpeed = 3.6f;
        f.Defense = new DefenseData { Kind = DefenseKind.Counter, WindowStart = 2, WindowEnd = 10, TotalFrames = 22 };
        f.Passive = new PassiveData { Kind = PassiveKind.ClashPower, Value = 2 };
        f.Moves = new()
        {
            M("punch", "Flash Draw", 5, 3, 12, hitstun: 15, dmg: 7, stagger: 4, clash: 11, DamageType.Slash, Sin.Wrath, reach: 120,
                selfOnHit: new[] { S(StatusKind.Poise, 2, 1) }),
            M("skill2", "Swift Cut", 9, 4, 16, hitstun: 18, dmg: 11, stagger: 6, clash: 14, DamageType.Slash, Sin.Gloom, reach: 132,
                selfOnHit: new[] { S(StatusKind.Poise, 2, 1) }),
            M("skill3", "Yield My Flesh", 13, 0, 21, hitstun: 22, dmg: 9, stagger: 7, clash: 18, DamageType.Slash, Sin.Wrath, reach: 140,
                coins: 3, interval: 5, selfOnHit: new[] { S(StatusKind.Poise, 1, 1) }),
            Ego("Crow's Eye View", "ZAYIN", Sin.Sloth, 3, 14, 4, 24, dmg: 13, stagger: 9, clash: 24, DamageType.Slash, Sin.Sloth, reach: 170,
                coins: 3, interval: 6),
        };
        return f;
    }

    private static FighterData HeathcliffShi()
    {
        FighterData f = Base("Heathcliff", "#7A2E2E");
        f.Name = "Shi Heathcliff";
        f.Identity = "Shi South Section 5";
        f.Combat.MaxHp = 170;
        f.Combat.StaggerMax = 38;
        f.Combat.SlashResist = 0.75f;
        f.Combat.PierceResist = 1.5f;
        f.Movement.WalkForwardSpeed = 4.4f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.Desperation, Value = 25 };
        f.Moves = new()
        {
            M("punch", "Quick Slash", 6, 3, 12, hitstun: 15, dmg: 8, stagger: 5, clash: 10, DamageType.Slash, Sin.Envy, reach: 114,
                selfOnHit: new[] { S(StatusKind.Poise, 1, 1) }),
            M("skill2", "Drawing Strike", 10, 4, 17, hitstun: 18, dmg: 12, stagger: 7, clash: 14, DamageType.Slash, Sin.Wrath, reach: 128,
                selfOnHit: new[] { S(StatusKind.Poise, 2, 2) }),
            M("skill3", "Fatal Blow", 15, 5, 22, hitstun: 24, dmg: 20, stagger: 10, clash: 18, DamageType.Slash, Sin.Envy, reach: 134),
            Ego("Bodysack", "ZAYIN", Sin.Envy, 3, 15, 4, 24, dmg: 13, stagger: 12, clash: 24, DamageType.Blunt, Sin.Envy, reach: 165,
                coins: 2, interval: 8, onHit: new[] { S(StatusKind.Tremor, 3, 3) }),
        };
        return f;
    }

    private static FighterData HeathcliffRabbit()
    {
        FighterData f = Base("Heathcliff", "#4F6D8F");
        f.Name = "Rabbit Heathcliff";
        f.Identity = "R Corp. 4th Pack Rabbit";
        f.Combat.MaxHp = 168;
        f.Combat.StaggerMax = 40;
        f.Combat.PierceResist = 0.75f;
        f.Combat.BluntResist = 1.25f;
        f.Movement.WalkForwardSpeed = 4.8f;
        f.Movement.JumpLaunchSpeed = 22f;
        f.Movement.JumpForwardSpeed = 5.6f;
        f.Defense.Kind = DefenseKind.Evade;
        f.Passive = new PassiveData { Kind = PassiveKind.StatusOnHit, Value = 1, Status = StatusKind.Rupture };
        MoveData s3 = M("skill3", "Rabbit Rush", 12, 0, 20, hitstun: 20, dmg: 6, stagger: 5, clash: 16, DamageType.Pierce, Sin.Wrath, reach: 150,
            coins: 4, interval: 4);
        s3.ChargeCost = 2;
        s3.ChargeBonusDamage = 4;
        f.Moves = new()
        {
            M("punch", "Mark", 6, 3, 12, hitstun: 15, dmg: 7, stagger: 5, clash: 10, DamageType.Pierce, Sin.Envy, reach: 124,
                selfOnHit: new[] { S(StatusKind.Charge, 1, 1) }),
            M("skill2", "Shoot", 9, 4, 16, hitstun: 17, dmg: 10, stagger: 6, clash: 13, DamageType.Pierce, Sin.Wrath, reach: 160,
                selfOnHit: new[] { S(StatusKind.Charge, 1, 2) }),
            s3,
            Ego("Bodysack", "ZAYIN", Sin.Envy, 3, 15, 4, 24, dmg: 13, stagger: 12, clash: 24, DamageType.Blunt, Sin.Envy, reach: 165,
                coins: 2, interval: 8, onHit: new[] { S(StatusKind.Tremor, 3, 3) }),
        };
        return f;
    }

    // ---- Bosses -----------------------------------------------------------------------------

    private static FighterData Vergilius()
    {
        var f = new FighterData
        {
            Name = "Vergilius",
            Identity = "BOSS - The Red Gaze",
            Color = "#8E1B1F",
            LookSearch = "vergilius",
        };
        f.Boss = new BossData { Armor = ArmorKind.Attacks, DrawScale = 1.45f, Phase2HpPercent = 50, Phase2DamagePercent = 25, Phase2ClashBonus = 4 };
        f.Combat.MaxHp = 300;
        f.Combat.StaggerMax = 120;
        f.Combat.SlashResist = 1f;
        f.Combat.PierceResist = 1f;
        f.Combat.BluntResist = 1.25f;
        f.Body = new BodyData { HurtboxWidth = 118, HurtboxHeight = 270, PushboxWidth = 100, PushboxHeight = 250 };
        f.Movement.WalkForwardSpeed = 2.9f;
        f.Movement.WalkBackwardSpeed = 2.4f;
        f.Movement.JumpLaunchSpeed = 17f;
        f.Movement.JumpForwardSpeed = 3.6f;
        f.Movement.JumpSquatFrames = 6;
        f.Movement.LandingFrames = 6;
        f.Defense = new DefenseData { Kind = DefenseKind.Guard, GuardDamage = 0.2f, GuardStagger = 0.6f };
        f.Passive = new PassiveData { Kind = PassiveKind.Composure, Value = 2 };
        f.Moves = new()
        {
            M("punch", "Red Slash", 13, 4, 20, hitstun: 14, dmg: 7, stagger: 4, clash: 11, DamageType.Slash, Sin.Wrath, reach: 150, y: -170, h: 60),
            M("skill2", "Gaze Thrust", 18, 5, 25, hitstun: 18, dmg: 9, stagger: 6, clash: 13, DamageType.Pierce, Sin.Pride, reach: 180, y: -165, h: 50,
                onHit: new[] { S(StatusKind.Bleed, 2, 2) }),
            M("skill3", "Crimson Flurry", 22, 0, 30, hitstun: 18, dmg: 5, stagger: 3, clash: 16, DamageType.Slash, Sin.Lust, reach: 170, y: -180, h: 80,
                coins: 3, interval: 6),
            Ego("The Red Gaze", "BOSS", Sin.Wrath, 5, 22, 4, 32, dmg: 12, stagger: 8, clash: 26, DamageType.Slash, Sin.Wrath, reach: 220,
                coins: 3, interval: 7, onHit: new[] { S(StatusKind.Bleed, 2, 2) }),
        };
        foreach (MoveData m in f.Moves.Where(m => m.Button == "ego"))
        {
            m.Hitbox.Y = -190;
            m.Hitbox.Height = 120;
        }

        return f;
    }

    private static FighterData ErlkingHeathcliff()
    {
        var f = new FighterData
        {
            Name = "Erlking Heathcliff",
            Identity = "BOSS - The Wild Hunt",
            Color = "#2B2340",
            LookSearch = "erlking",
        };
        f.Boss = new BossData { Armor = ArmorKind.Attacks, DrawScale = 1.5f, Phase2HpPercent = 40, Phase2DamagePercent = 30, Phase2ClashBonus = 4 };
        f.Combat.MaxHp = 320;
        f.Combat.StaggerMax = 130;
        f.Combat.BluntResist = 0.75f;
        f.Combat.SlashResist = 1f;
        f.Combat.PierceResist = 1.25f;
        f.Body = new BodyData { HurtboxWidth = 126, HurtboxHeight = 280, PushboxWidth = 108, PushboxHeight = 260 };
        f.Movement.WalkForwardSpeed = 2.7f;
        f.Movement.WalkBackwardSpeed = 2.2f;
        f.Movement.JumpLaunchSpeed = 16f;
        f.Movement.JumpForwardSpeed = 3.4f;
        f.Movement.JumpSquatFrames = 7;
        f.Movement.LandingFrames = 7;
        f.Defense = new DefenseData { Kind = DefenseKind.Guard, GuardDamage = 0.2f, GuardStagger = 0.6f };
        f.Passive = new PassiveData { Kind = PassiveKind.StatusOnHit, Value = 1, Status = StatusKind.Sinking };
        f.Moves = new()
        {
            M("punch", "Coffin Swing", 14, 5, 21, hitstun: 14, dmg: 8, stagger: 5, clash: 12, DamageType.Blunt, Sin.Envy, reach: 155, y: -170, h: 70),
            M("skill2", "Hunt", 18, 6, 25, hitstun: 18, dmg: 10, stagger: 6, clash: 14, DamageType.Blunt, Sin.Gloom, reach: 185, y: -160, h: 60,
                onHit: new[] { S(StatusKind.Sinking, 2, 2) }),
            M("skill3", "Wild Hunt", 22, 0, 30, hitstun: 18, dmg: 6, stagger: 4, clash: 16, DamageType.Slash, Sin.Envy, reach: 175, y: -185, h: 90,
                coins: 3, interval: 7),
            Ego("Dullahan's Charge", "BOSS", Sin.Envy, 5, 24, 4, 34, dmg: 12, stagger: 8, clash: 26, DamageType.Blunt, Sin.Envy, reach: 240,
                coins: 3, interval: 7, onHit: new[] { S(StatusKind.Sinking, 3, 2) }),
        };
        foreach (MoveData m in f.Moves.Where(m => m.Button == "ego"))
        {
            m.Hitbox.Y = -200;
            m.Hitbox.Height = 140;
        }

        return f;
    }

    // ---- Builders ---------------------------------------------------------------------------

    private static FighterData Base(string sinner, string color) => new()
    {
        Name = sinner,
        Sinner = sinner,
        Identity = "LCB Sinner",
        Color = color,
    };

    /// <summary>A status a move applies.</summary>
    public static StatusData S(StatusKind status, int potency, int count) => new() { Status = status, Potency = potency, Count = count };

    /// <summary>
    /// One move. <paramref name="active"/> 0 = just long enough for every coin. <paramref name="reach"/>
    /// is how far in front of the body the hitbox ends.
    /// </summary>
    public static MoveData M(
        string button, string name, int startup, int active, int recovery, int hitstun, int dmg, int stagger, float clash,
        DamageType type, Sin sin, float reach = 118, int coins = 1, int interval = 4, float pushback = 7,
        StatusData[]? onHit = null, StatusData[]? selfOnHit = null, int hitstop = 0, float y = float.NaN, float h = float.NaN)
    {
        MoveData m = MoveData.Default(button);
        m.Id = button;
        m.Name = name;
        m.Startup = startup;
        m.Coins = coins;
        m.CoinInterval = interval;
        m.Active = Math.Max(active, ((coins - 1) * interval) + 2);
        m.Recovery = recovery;
        m.Hitstun = hitstun;
        m.Hitstop = hitstop > 0 ? hitstop : Math.Clamp(6 + (dmg / 4), 6, 12);
        m.Pushback = pushback;
        m.Damage = dmg;
        m.StaggerDamage = stagger;
        m.ClashPower = clash;
        m.DamageType = type;
        m.Sin = sin;
        float start = m.Hitbox.X;
        m.Hitbox.Width = Math.Max(24, reach - start);
        m.ArmHurtboxWidth = Math.Max(0, m.Hitbox.Width - 16);
        if (!float.IsNaN(y))
        {
            m.Hitbox.Y = y;
        }

        if (!float.IsNaN(h))
        {
            m.Hitbox.Height = h;
        }
        m.OnHit = (onHit ?? Array.Empty<StatusData>()).ToList();
        m.SelfOnHit = (selfOnHit ?? Array.Empty<StatusData>()).ToList();
        return m;
    }

    /// <summary>An E.G.O on the E.G.O button: costs Sins, has a grade.</summary>
    public static MoveData Ego(
        string name, string grade, Sin costSin, int cost, int startup, int active, int recovery, int dmg, int stagger, float clash,
        DamageType type, Sin sin, float reach = 150, int coins = 3, int interval = 6, StatusData[]? onHit = null)
    {
        MoveData m = M("ego", name, startup, active, recovery, hitstun: 26, dmg, stagger, clash, type, sin, reach, coins, interval,
            pushback: 10, onHit: onHit, hitstop: 12);
        m.Grade = grade;
        m.SinCost = new() { new SinCostData { Sin = costSin, Amount = cost } };
        m.Hitbox.Height = 60;
        m.Hitbox.Y = -170;
        return m;
    }
}
