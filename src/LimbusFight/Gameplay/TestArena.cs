namespace LimbusFight.Gameplay;

/// <summary>What the training dummy does.</summary>
public enum DummyMode
{
    /// <summary>Stands still and takes hits (Steps 1-4 behaviour).</summary>
    Stand,

    /// <summary>Walks up and uses its skills (and E.G.O), so clashes, trades and K.O.s on P1 can happen.</summary>
    Fight,

    /// <summary>Holds its defense (guards, or evades / counters when P1 attacks).</summary>
    Defend,

    /// <summary>Fights like a real opponent: the CPU (<see cref="TestArena.Cpu"/>).</summary>
    Cpu,
}

public enum RoundPhase
{
    Fight,

    /// <summary>Someone was knocked out or time ran out; the next round starts shortly.</summary>
    Over,

    /// <summary>Someone won enough rounds: the match is over (versus, arcade).</summary>
    MatchOver,
}

/// <summary>
/// The sandbox match: Player 1 vs the training dummy on a flat stage, in rounds. Owns the frame
/// counter, hitstop, collision, hits, clashes and the Step 5 combat rules (health, Stagger,
/// Sanity, Sins, statuses). Pure simulation: no rendering, no Raylib, no clocks, no randomness.
/// </summary>
public sealed class TestArena
{
    public const float StageWidth = 1280f;
    public const float FloorY = 640f;
    public const float WallLeft = 40f;
    public const float WallRight = 1240f;

    public const float Player1StartX = 460f;
    public const float DummyStartX = 820f;

    /// <summary>The fighting dummy throws a punch at most this often (frames).</summary>
    public const int DummyAttackInterval = 40;

    private const int MaxEvents = 48;

    private readonly List<CombatEvent> _events = new();

    public TestArena()
    {
        Player1 = new Fighter("P1", Player1StartX, FloorY, facing: 1, isDummy: false);
        Dummy = new Fighter("DUMMY", DummyStartX, FloorY, facing: -1, isDummy: true);
        RoundTimer = CombatRules.RoundFrames;
    }

    public Fighter Player1 { get; }

    public Fighter Dummy { get; }

    /// <summary>Simulation frames since the arena was created (keeps counting during hitstop).</summary>
    public long Frame { get; private set; }

    /// <summary>Remaining freeze frames from the last hit or clash.</summary>
    public int HitstopRemaining { get; private set; }

    public int TotalHits { get; private set; }

    /// <summary>Frame number of the most recent hit (-1 = none yet).</summary>
    public long LastHitFrame { get; private set; } = -1;

    /// <summary>Stage position of the most recent hit spark.</summary>
    public (float X, float Y) LastHitPoint { get; private set; }

    /// <summary>Who landed the most recent hit (for the hit effect), or null.</summary>
    public Fighter? LastHitAttacker { get; private set; }

    public DummyMode DummyMode { get; set; } = DummyMode.Stand;

    /// <summary>The computer opponent used when <see cref="DummyMode"/> is <see cref="DummyMode.Cpu"/>.</summary>
    public CpuController Cpu { get; } = new();

    /// <summary>Seed for the CPU's random choices, applied by <see cref="NewMatch"/>.</summary>
    public uint Seed { get; set; } = 1;

    /// <summary>Round length in frames; 0 = no timer.</summary>
    public int RoundLengthFrames { get; set; } = CombatRules.RoundFrames;

    /// <summary>Training: health and Stagger refill after a short rest, and nobody is knocked out.</summary>
    public bool InfiniteHealth { get; set; }

    /// <summary>Training: both fighters always have the Sins their E.G.O needs.</summary>
    public bool FreeEgo { get; set; }

    // ---- Rounds ----------------------------------------------------------------------------
    public RoundPhase Phase { get; private set; } = RoundPhase.Fight;

    public int RoundNumber { get; private set; } = 1;

    /// <summary>Frames left in the round.</summary>
    public int RoundTimer { get; private set; }

    /// <summary>Frames since the current phase began.</summary>
    public int PhaseFrames { get; private set; }

    public int Player1Wins { get; private set; }

    public int DummyWins { get; private set; }

    /// <summary>"K.O." / "TIME" and who won, while the round is over.</summary>
    public string? RoundResult { get; private set; }

    /// <summary>Recent combat events (newest last), for popups.</summary>
    public IReadOnlyList<CombatEvent> Events => _events;

    /// <summary>Rounds needed to win the match; 0 = endless rounds (training).</summary>
    public int RoundsToWin { get; set; }

    /// <summary>After <see cref="RoundPhase.MatchOver"/>: 1 = Player 1 won, -1 = Player 2, 0 = draw.</summary>
    public int MatchWinner { get; private set; }

    /// <summary>Starts a fresh match: round 1, no wins.</summary>
    public void NewMatch()
    {
        Player1Wins = 0;
        DummyWins = 0;
        MatchWinner = 0;
        Cpu.Reset(Seed);
        StartRound(1);
    }

    /// <summary>Advances one frame with Player 2 controlled from outside (a second player or the CPU).</summary>
    public void Tick(FrameInput player1Input, FrameInput player2Input) => TickCore(player1Input, player2Input);

    /// <summary>Advances the match by exactly one 60 Hz frame; the dummy decides what Player 2 does.</summary>
    public void Tick(FrameInput player1Input) => TickCore(player1Input, null);

    private void TickCore(FrameInput player1Input, FrameInput? player2Input)
    {
        Frame++;
        PhaseFrames++;

        if (Phase is RoundPhase.Over or RoundPhase.MatchOver)
        {
            player1Input = FrameInput.None;
            player2Input = FrameInput.None;
            if (Phase == RoundPhase.Over && PhaseFrames >= CombatRules.RoundEndFrames)
            {
                StartRound(RoundNumber + 1);
                return;
            }
        }

        bool frozen = HitstopRemaining > 0;
        Player1.ReadInput(player1Input, frozen);
        Dummy.ReadInput(player2Input ?? DummyInput(), frozen);

        if (frozen)
        {
            HitstopRemaining--;
            return;
        }

        Player1.Update();
        Dummy.Update();

        ApplyStartOfAttackStatuses(Player1);
        ApplyStartOfAttackStatuses(Dummy);
        AnnounceEgo(Player1);
        AnnounceEgo(Dummy);
        TickBurn(Player1);
        TickBurn(Dummy);

        ResolvePushboxes();
        ClampToStage(Player1);
        ClampToStage(Dummy);

        Player1.FaceTowards(Dummy);
        Dummy.FaceTowards(Player1);

        if (!ResolveClash())
        {
            ResolveHits();
        }

        TrainingRefill(Player1);
        TrainingRefill(Dummy);
        CheckRoundEnd();
    }

    /// <summary>Training helpers: refill health / Stagger once a fighter has rested a second; keep E.G.O paid for.</summary>
    private void TrainingRefill(Fighter f)
    {
        if (InfiniteHealth && f.IsActionable && Frame - LastHitFrame > 60 && (f.Hp < f.Stats.MaxHp || f.Stagger < f.Stats.StaggerMax))
        {
            f.Hp = f.Stats.MaxHp;
            f.Stagger = f.Stats.StaggerMax;
        }

        if (FreeEgo && f.Stats.Ego is { } ego)
        {
            foreach (SinCost cost in ego.SinCost)
            {
                f.Sins[(int)cost.Sin] = Math.Max(f.Sins[(int)cost.Sin], cost.Amount);
            }
        }
    }

    /// <summary>Gives Player 1 or the dummy another fighter's stats (from its data file). Positions are kept.</summary>
    public void SetFighter(bool dummy, FighterStats stats)
    {
        (dummy ? Dummy : Player1).SetStats(stats);
        HitstopRemaining = 0;
        LastHitFrame = -1;
        LastHitAttacker = null;
    }

    /// <summary>Restarts the current round: start positions, full health (R key). Wins are kept.</summary>
    public void ResetPositions() => StartRound(RoundNumber);

    // =========================================================================================
    //  Rounds
    // =========================================================================================

    private void StartRound(int number)
    {
        RoundNumber = number;
        Phase = RoundPhase.Fight;
        PhaseFrames = 0;
        RoundTimer = RoundLengthFrames;
        RoundResult = null;
        Player1.ResetTo(Player1StartX, facing: 1);
        Dummy.ResetTo(DummyStartX, facing: -1);
        HitstopRemaining = 0;
        TotalHits = 0;
        LastHitFrame = -1;
        LastHitAttacker = null;
        Emit(CombatEventKind.RoundStart, StageWidth / 2f, 300f, $"ROUND {number}", true);
    }

    private void CheckRoundEnd()
    {
        if (Phase != RoundPhase.Fight)
        {
            return;
        }

        if (InfiniteHealth)
        {
            Player1.Hp = Math.Max(1, Player1.Hp);
            Dummy.Hp = Math.Max(1, Dummy.Hp);
        }

        bool p1Down = Player1.Hp <= 0;
        bool dummyDown = Dummy.Hp <= 0;
        if (p1Down || dummyDown)
        {
            if (p1Down)
            {
                Player1.EnterDown();
            }

            if (dummyDown)
            {
                Dummy.EnterDown();
            }

            EndRound("K.O.", p1Down && dummyDown ? 0 : dummyDown ? 1 : -1);
            Emit(CombatEventKind.Ko, StageWidth / 2f, 300f, "K.O.", dummyDown);
            return;
        }

        if (RoundLengthFrames > 0 && --RoundTimer <= 0)
        {
            float p1 = (float)Player1.Hp / Math.Max(1, Player1.Stats.MaxHp);
            float dummy = (float)Dummy.Hp / Math.Max(1, Dummy.Stats.MaxHp);
            int winner = MathF.Abs(p1 - dummy) < 0.0001f ? 0 : p1 > dummy ? 1 : -1;
            EndRound("TIME", winner);
            Emit(CombatEventKind.TimeOver, StageWidth / 2f, 300f, "TIME", winner >= 0);
        }
    }

    /// <param name="winner">1 = Player 1, -1 = dummy, 0 = draw.</param>
    private void EndRound(string how, int winner)
    {
        Phase = RoundPhase.Over;
        PhaseFrames = 0;
        if (winner > 0)
        {
            Player1Wins++;
        }
        else if (winner < 0)
        {
            DummyWins++;
        }

        string who = winner > 0 ? $"{Player1.Name} WINS" : winner < 0 ? $"{Dummy.Name} WINS" : "DRAW";
        RoundResult = $"{how}  {who}";

        if (RoundsToWin > 0 && (Player1Wins >= RoundsToWin || DummyWins >= RoundsToWin))
        {
            Phase = RoundPhase.MatchOver;
            MatchWinner = Player1Wins > DummyWins ? 1 : DummyWins > Player1Wins ? -1 : 0;
        }
    }

    // =========================================================================================
    //  The fighting dummy
    // =========================================================================================

    /// <summary>
    /// Stand: presses nothing. Fight: walks into range and uses Skill 1, 2, 3 in turn on a steady beat
    /// (E.G.O when it can). Defend: holds guard, or evades / counters as P1's attack starts.
    /// </summary>
    private FrameInput DummyInput()
    {
        if (DummyMode == DummyMode.Stand || Phase != RoundPhase.Fight)
        {
            return FrameInput.None;
        }

        if (DummyMode == DummyMode.Cpu)
        {
            return Cpu.Think(this, Dummy, Player1);
        }

        if (DummyMode == DummyMode.Defend)
        {
            bool hold = Dummy.Stats.Defense.Kind == DefenseKind.Guard
                || (Player1.State == FighterState.Attack && Player1.StateFrame <= 2);
            return hold ? new FrameInput(InputButtons.Defend) : FrameInput.None;
        }

        if (!Dummy.IsActionable)
        {
            return FrameInput.None;
        }

        AttackData punch = Dummy.Stats.Punch;
        float reach = punch.HitboxOffsetX + punch.HitboxWidth + (Player1.Stats.HurtboxWidth / 2f) - 6f;
        float distance = MathF.Abs(Player1.X - Dummy.X);

        if (distance > reach)
        {
            return new FrameInput(Player1.X < Dummy.X ? InputButtons.Left : InputButtons.Right);
        }

        if (Frame % DummyAttackInterval != 0)
        {
            return FrameInput.None;
        }

        if (Dummy.CanUseEgo)
        {
            return new FrameInput(InputButtons.Ego);
        }

        return ((Frame / DummyAttackInterval) % 3) switch
        {
            1 when Dummy.Stats.Skill2 is not null => new FrameInput(InputButtons.Skill2),
            2 when Dummy.Stats.Skill3 is not null => new FrameInput(InputButtons.Skill3),
            _ => new FrameInput(InputButtons.Punch),
        };
    }

    // =========================================================================================
    //  Collision, clashes and hits
    // =========================================================================================

    private void ResolvePushboxes()
    {
        Box a = Player1.Pushbox;
        Box b = Dummy.Pushbox;

        if (!a.Intersects(b))
        {
            return;
        }

        float overlap = a.OverlapX(b);
        int side = Player1.X < Dummy.X ? -1
            : Player1.X > Dummy.X ? 1
            : -Player1.Facing;

        // Player 1 absorbs the correction...
        Player1.X += side * overlap;
        ClampToStage(Player1);

        // ...unless Player 1 is pinned against a wall, then the dummy gives way.
        Box after = Player1.Pushbox;
        if (after.Intersects(Dummy.Pushbox))
        {
            Dummy.X -= side * after.OverlapX(Dummy.Pushbox);
        }
    }

    private static void ClampToStage(Fighter fighter)
    {
        float half = fighter.Stats.PushboxWidth * 0.5f;
        fighter.X = Math.Clamp(fighter.X, WallLeft + half, WallRight - half);
    }

    /// <summary>
    /// Both hitboxes live and touching: a clash. The higher clash power (move power + Sanity) wins
    /// and keeps attacking; the loser's attack is cancelled and it is knocked back. A tie cancels both.
    /// </summary>
    private bool ResolveClash()
    {
        if (Player1.LiveHitbox is not Box a || Dummy.LiveHitbox is not Box b || !a.Intersects(b)
            || Player1.CurrentAttack is not { } pa || Dummy.CurrentAttack is not { } da)
        {
            return false;
        }

        float p1Power = CombatRules.ClashPower(pa, Player1.Sanity) + PassiveClashBonus(Player1);
        float dummyPower = CombatRules.ClashPower(da, Dummy.Sanity) + PassiveClashBonus(Dummy);
        (float x, float y) = a.IntersectionCenter(b);
        HitstopRemaining = CombatRules.ClashHitstop;

        if (MathF.Abs(p1Power - dummyPower) < 0.01f)
        {
            Player1.EnterClashRecoil(CombatRules.ClashTieRecoil, CombatRules.ClashPushback);
            Dummy.EnterClashRecoil(CombatRules.ClashTieRecoil, CombatRules.ClashPushback);
            Emit(CombatEventKind.Clash, x, y, "CLASH! even", true);
            return true;
        }

        bool p1Wins = p1Power > dummyPower;
        Fighter winner = p1Wins ? Player1 : Dummy;
        Fighter loser = p1Wins ? Dummy : Player1;
        loser.EnterClashRecoil(CombatRules.ClashLoserRecoil, CombatRules.ClashPushback);
        Emit(CombatEventKind.Clash, x, y, $"CLASH! {(p1Wins ? Player1.Name : Dummy.Name)} wins {MathF.Max(p1Power, dummyPower):0.#}-{MathF.Min(p1Power, dummyPower):0.#}", p1Wins);

        if (winner.AddSanity(CombatRules.SanityOnClashWon))
        {
            Emit(CombatEventKind.Panic, winner.X, winner.Y - winner.Stats.HurtboxHeight, "PANIC", winner == Player1);
        }

        if (loser.AddSanity(CombatRules.SanityOnClashLost))
        {
            Emit(CombatEventKind.Panic, loser.X, loser.Y - loser.Stats.HurtboxHeight, "PANIC", loser == Player1);
        }

        return true;
    }

    /// <summary>Checks both directions first, then applies, so two hits on the same frame trade.</summary>
    private void ResolveHits()
    {
        (Box Hit, Box Hurt)? p1Hit = FindHit(Player1, Dummy);
        (Box Hit, Box Hurt)? dummyHit = FindHit(Dummy, Player1);
        AttackData? p1Attack = Player1.CurrentAttack;
        AttackData? dummyAttack = Dummy.CurrentAttack;

        if (p1Hit is { } h1 && p1Attack is not null)
        {
            ApplyHit(Player1, Dummy, p1Attack, h1.Hit.IntersectionCenter(h1.Hurt));
        }

        if (dummyHit is { } h2 && dummyAttack is not null)
        {
            ApplyHit(Dummy, Player1, dummyAttack, h2.Hit.IntersectionCenter(h2.Hurt));
        }
    }

    private static (Box Hit, Box Hurt)? FindHit(Fighter attacker, Fighter defender)
    {
        if (attacker.LiveHitbox is not Box hitbox || attacker.CurrentAttack is null || defender.State == FighterState.Down)
        {
            return null;
        }

        foreach (Box hurtbox in defender.Hurtboxes)
        {
            if (hitbox.Intersects(hurtbox))
            {
                return (hitbox, hurtbox);
            }
        }

        return null;
    }

    /// <summary>
    /// One hit: damage after resistance, Sanity, Stagger, Poise crits and Rupture; Stagger damage
    /// plus Tremor; Sinking drains Sanity; statuses, Sins and Sanity change; Stagger and K.O. checks.
    /// </summary>
    private void ApplyHit(Fighter attacker, Fighter defender, AttackData attack, (float X, float Y) point)
    {
        bool byP1 = attacker == Player1;
        float resist = defender.Stats.Resist(attack.DamageType);

        // Counter: a hit inside the parry window is cancelled and the attacker reels.
        if (defender.State == FighterState.Counter && defender.Stats.Defense.InWindow(defender.StateFrame))
        {
            attacker.EnterClashRecoil(CombatRules.CounterRecoil, CombatRules.ClashPushback);
            defender.CounterSucceeded();
            defender.AddSanity(CombatRules.SanityOnCounter);
            HitstopRemaining = CombatRules.ClashHitstop;
            Emit(CombatEventKind.Counter, point.X, point.Y, "COUNTER!", !byP1);
            return;
        }

        // Guard: a little damage, full Stagger damage, blockstun; no statuses, Sins or Sanity.
        if (defender.State is FighterState.Guard or FighterState.Blockstun)
        {
            DefenseStats guard = defender.Stats.Defense;
            int chip = Math.Max(0, (int)MathF.Round(attack.Damage * resist * guard.GuardDamage));
            int guardStagger = (int)MathF.Round(attack.StaggerDamage * resist * guard.GuardStagger * SturdyFactor(defender));
            defender.Hp = Math.Max(0, defender.Hp - chip);
            defender.Stagger = Math.Max(0, defender.Stagger - guardStagger);
            attacker.OnAttackConnected();
            defender.TakeBlock(attack);
            HitstopRemaining = Math.Max(1, attack.Hitstop / 2);
            LastHitFrame = Frame;
            LastHitPoint = point;
            LastHitAttacker = attacker;
            Emit(CombatEventKind.Guard, point.X, point.Y, $"GUARD -{chip}", byP1);
            CheckStaggerBreak(defender, byP1);
            return;
        }

        float damage = attack.Damage * resist * CombatRules.SanityMultiplier(attacker.Sanity);

        if (attacker.Stats.Passive.Kind == PassiveKind.Desperation && attacker.Hp * 2 < attacker.Stats.MaxHp)
        {
            damage *= 1f + (attacker.Stats.Passive.Value / 100f);
        }

        // Charge: spent by moves that ask for it, for extra damage.
        if (attack.ChargeCost > 0 && attacker.Status(StatusKind.Charge).Count >= attack.ChargeCost)
        {
            ref StatusStack charge = ref attacker.Status(StatusKind.Charge);
            charge.Count -= attack.ChargeCost;
            if (charge.Count <= 0)
            {
                charge = default;
            }

            damage += attack.ChargeBonusDamage;
            Emit(CombatEventKind.Charge, attacker.X, attacker.Y - attacker.Stats.HurtboxHeight - 10f, "CHARGE", byP1);
        }

        if (attacker.IsPanicking)
        {
            damage *= CombatRules.PanicDamageMultiplier;
        }

        if (attacker.InPhase2 && attacker.Stats.Boss is { } enraged)
        {
            damage *= 1f + (enraged.Phase2DamagePercent / 100f);
        }

        bool armored = defender.HasArmorNow;
        if (armored)
        {
            damage *= defender.Stats.Boss!.ArmorDamage;
        }

        if (defender.State == FighterState.Staggered)
        {
            damage *= CombatRules.StaggeredDamageMultiplier;
        }

        // Poise: a steady crit meter instead of a crit chance.
        bool crit = false;
        int poise = attacker.ConsumeStatus(StatusKind.Poise);
        if (poise > 0)
        {
            attacker.CritMeter += poise * CombatRules.PoisePerPotency;
            if (attacker.CritMeter >= 100)
            {
                attacker.CritMeter -= 100;
                crit = true;
                damage *= CombatRules.CritMultiplier;
            }
        }

        damage += defender.ConsumeStatus(StatusKind.Rupture);
        float stagger = ((attack.StaggerDamage * resist) + defender.ConsumeStatus(StatusKind.Tremor)) * SturdyFactor(defender);
        int sinking = defender.ConsumeStatus(StatusKind.Sinking);

        int hpLoss = Math.Max(0, (int)MathF.Round(damage));
        defender.Hp = Math.Max(0, defender.Hp - hpLoss);
        defender.Stagger = Math.Max(0, defender.Stagger - (int)MathF.Round(stagger));

        foreach (StatusApply apply in attack.OnHit)
        {
            defender.AddStatus(apply);
        }

        foreach (StatusApply apply in attack.SelfOnHit)
        {
            attacker.AddStatus(apply);
        }

        PassiveStats passive = attacker.Stats.Passive;
        if (passive.Kind == PassiveKind.StatusOnHit && passive.Value > 0)
        {
            defender.AddStatus(new StatusApply(passive.Status, passive.Value, 1));
        }

        attacker.AddSin(attack.Sin);

        int composure = passive.Kind == PassiveKind.Composure ? passive.Value : 0;
        if (attacker.AddSanity(CombatRules.SanityOnHitLanded + composure))
        {
            Emit(CombatEventKind.Panic, attacker.X, attacker.Y - attacker.Stats.HurtboxHeight, "PANIC", byP1);
        }

        if (defender.AddSanity(CombatRules.SanityOnHitTaken - sinking))
        {
            Emit(CombatEventKind.Panic, defender.X, defender.Y - defender.Stats.HurtboxHeight, "PANIC", !byP1);
        }

        attacker.OnAttackConnected();
        if (armored)
        {
            defender.TakeArmoredHit();
            Emit(CombatEventKind.Armor, point.X, point.Y - 34f, "ARMOR", byP1);
        }
        else
        {
            defender.TakeHit(attack);
        }

        CheckPhase2(defender);

        HitstopRemaining = armored ? Math.Max(2, attack.Hitstop / 2) : attack.Hitstop;
        TotalHits++;
        LastHitFrame = Frame;
        LastHitPoint = point;
        LastHitAttacker = attacker;

        string label = CombatRules.ResistLabel(resist);
        Emit(CombatEventKind.Damage, point.X, point.Y, label == "Normal" ? $"-{hpLoss}" : $"-{hpLoss} {label}", byP1);
        if (crit)
        {
            Emit(CombatEventKind.Crit, point.X, point.Y - 30f, "CRIT", byP1);
        }

        CheckStaggerBreak(defender, byP1);
    }

    private void CheckStaggerBreak(Fighter defender, bool byP1)
    {
        if (defender.Stagger <= 0 && defender.State != FighterState.Staggered && defender.Hp > 0)
        {
            defender.EnterStaggered();
            Emit(CombatEventKind.Stagger, defender.X, defender.Y - defender.Stats.HurtboxHeight - 20f, "STAGGERED", !byP1);
        }
    }

    private static float PassiveClashBonus(Fighter f) =>
        (f.Stats.Passive.Kind == PassiveKind.ClashPower ? f.Stats.Passive.Value : 0f)
        + (f.InPhase2 && f.Stats.Boss is { } boss ? boss.Phase2ClashBonus : 0f);

    /// <summary>A Boss below its phase 2 line becomes enraged (once per round).</summary>
    private void CheckPhase2(Fighter f)
    {
        if (f.Stats.Boss is not { Phase2HpPercent: > 0 } boss || f.InPhase2 || f.Hp <= 0
            || f.Hp * 100 > f.Stats.MaxHp * boss.Phase2HpPercent)
        {
            return;
        }

        f.InPhase2 = true;
        Emit(CombatEventKind.Enrage, f.X, f.Y - f.Stats.HurtboxHeight - 20f, "ENRAGED", f == Player1);
    }

    private static float SturdyFactor(Fighter f) =>
        f.Stats.Passive.Kind == PassiveKind.Sturdy ? Math.Clamp(1f - (f.Stats.Passive.Value / 100f), 0f, 1f) : 1f;

    private void AnnounceEgo(Fighter fighter)
    {
        if (fighter.StartedEgo && fighter.CurrentAttack is { } ego)
        {
            string grade = ego.Grade.Length > 0 ? $"  [{ego.Grade}]" : string.Empty;
            string name = ego.Name.Equals("E.G.O", StringComparison.OrdinalIgnoreCase) ? string.Empty : "  " + ego.Name;
            Emit(CombatEventKind.Ego, fighter.X, fighter.Y - fighter.Stats.HurtboxHeight - 40f, $"E.G.O{name}{grade}", fighter == Player1);
        }
    }

    // =========================================================================================
    //  Statuses that act on their own
    // =========================================================================================

    /// <summary>Bleed: hurts its bearer each time it starts an attack.</summary>
    private void ApplyStartOfAttackStatuses(Fighter fighter)
    {
        if (!fighter.StartedAttack)
        {
            return;
        }

        int bleed = fighter.ConsumeStatus(StatusKind.Bleed);
        if (bleed > 0)
        {
            fighter.Hp = Math.Max(0, fighter.Hp - bleed);
            Emit(CombatEventKind.StatusDamage, fighter.X, fighter.Y - fighter.Stats.HurtboxHeight, $"Bleed -{bleed}", fighter == Player1);
            CheckPhase2(fighter);
        }
    }

    /// <summary>Burn: deals its potency once a second until its count runs out.</summary>
    private void TickBurn(Fighter fighter)
    {
        if (!fighter.Status(StatusKind.Burn).Active || fighter.State == FighterState.Down)
        {
            fighter.BurnTimer = 0;
            return;
        }

        if (++fighter.BurnTimer < CombatRules.BurnInterval)
        {
            return;
        }

        fighter.BurnTimer = 0;
        int burn = fighter.ConsumeStatus(StatusKind.Burn);
        fighter.Hp = Math.Max(0, fighter.Hp - burn);
        Emit(CombatEventKind.StatusDamage, fighter.X, fighter.Y - (fighter.Stats.HurtboxHeight / 2f), $"Burn -{burn}", fighter == Player1);
        CheckPhase2(fighter);
    }

    private void Emit(CombatEventKind kind, float x, float y, string text, bool fromPlayer1)
    {
        _events.Add(new CombatEvent(Frame, kind, x, y, text, fromPlayer1));
        if (_events.Count > MaxEvents)
        {
            _events.RemoveAt(0);
        }
    }
}
