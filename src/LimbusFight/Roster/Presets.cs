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
    public const int Version = 1;

    public sealed record Preset(string Id, Func<FighterData> Make);

    public static IReadOnlyList<Preset> All { get; } = new List<Preset>
    {
        new("yi-sang", YiSang),
        new("faust", Faust),
        new("don-quixote", DonQuixote),
        new("ryoshu", Ryoshu),
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
        f.Combat.MaxHp = 100;
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
        f.Combat.MaxHp = 115;
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
        f.Combat.MaxHp = 92;
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
            Ego("La Sangre de Sancho", "HE", Sin.Gluttony, 4, 14, 4, 24, dmg: 13, stagger: 10, clash: 26, DamageType.Pierce, Sin.Gluttony, reach: 170,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Bleed, 3, 3) }),
        };
        return f;
    }

    private static FighterData Ryoshu()
    {
        FighterData f = Base("Ryoshu", "#B8322E");
        f.Combat.MaxHp = 96;
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
            Ego("Forest for the Flames", "TETH", Sin.Wrath, 4, 15, 4, 24, dmg: 14, stagger: 10, clash: 26, DamageType.Slash, Sin.Wrath, reach: 165,
                coins: 3, interval: 6, onHit: new[] { S(StatusKind.Burn, 3, 3) }),
        };
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
        StatusData[]? onHit = null, StatusData[]? selfOnHit = null, int hitstop = 0)
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
