namespace LimbusFight.Gameplay;

/// <summary>What the training dummy does.</summary>
public enum DummyMode
{
    /// <summary>Stands still and takes hits (Steps 1-4 behaviour).</summary>
    Stand,

    /// <summary>Walks up and punches back, so clashes, trades and K.O.s on P1 can happen.</summary>
    Fight,
}

public enum RoundPhase
{
    Fight,

    /// <summary>Someone was knocked out or time ran out; the next round starts shortly.</summary>
    Over,
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

    /// <summary>Advances the match by exactly one 60 Hz frame.</summary>
    public void Tick(FrameInput player1Input)
    {
        Frame++;
        PhaseFrames++;

        if (Phase == RoundPhase.Over)
        {
            player1Input = FrameInput.None;
            if (PhaseFrames >= CombatRules.RoundEndFrames)
            {
                StartRound(RoundNumber + 1);
                return;
            }
        }

        bool frozen = HitstopRemaining > 0;
        Player1.ReadInput(player1Input, frozen);
        Dummy.ReadInput(DummyInput(), frozen);

        if (frozen)
        {
            HitstopRemaining--;
            return;
        }

        Player1.Update();
        Dummy.Update();

        ApplyStartOfAttackStatuses(Player1);
        ApplyStartOfAttackStatuses(Dummy);
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

        CheckRoundEnd();
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
        RoundTimer = CombatRules.RoundFrames;
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

        if (--RoundTimer <= 0)
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

        string who = winner > 0 ? "P1 WINS" : winner < 0 ? "DUMMY WINS" : "DRAW";
        RoundResult = $"{how}  {who}";
    }

    // =========================================================================================
    //  The fighting dummy
    // =========================================================================================

    /// <summary>Stand: presses nothing. Fight: walks into range and punches on a steady beat.</summary>
    private FrameInput DummyInput()
    {
        if (DummyMode == DummyMode.Stand || Phase != RoundPhase.Fight || !Dummy.IsActionable)
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

        return Frame % DummyAttackInterval == 0 ? new FrameInput(InputButtons.Punch) : FrameInput.None;
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

        float p1Power = CombatRules.ClashPower(pa, Player1.Sanity);
        float dummyPower = CombatRules.ClashPower(da, Dummy.Sanity);
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
        Emit(CombatEventKind.Clash, x, y, $"CLASH! {(p1Wins ? "P1" : "DUMMY")} wins {MathF.Max(p1Power, dummyPower):0.#}-{MathF.Min(p1Power, dummyPower):0.#}", p1Wins);

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
        float damage = attack.Damage * resist * CombatRules.SanityMultiplier(attacker.Sanity);

        if (attacker.IsPanicking)
        {
            damage *= CombatRules.PanicDamageMultiplier;
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
        float stagger = (attack.StaggerDamage * resist) + defender.ConsumeStatus(StatusKind.Tremor);
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

        attacker.AddSin(attack.Sin);

        if (attacker.AddSanity(CombatRules.SanityOnHitLanded))
        {
            Emit(CombatEventKind.Panic, attacker.X, attacker.Y - attacker.Stats.HurtboxHeight, "PANIC", byP1);
        }

        if (defender.AddSanity(CombatRules.SanityOnHitTaken - sinking))
        {
            Emit(CombatEventKind.Panic, defender.X, defender.Y - defender.Stats.HurtboxHeight, "PANIC", !byP1);
        }

        attacker.OnAttackConnected();
        defender.TakeHit(attack);

        HitstopRemaining = attack.Hitstop;
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

        if (defender.Stagger <= 0 && defender.State != FighterState.Staggered && defender.Hp > 0)
        {
            defender.EnterStaggered();
            Emit(CombatEventKind.Stagger, defender.X, defender.Y - defender.Stats.HurtboxHeight - 20f, "STAGGERED", !byP1);
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
