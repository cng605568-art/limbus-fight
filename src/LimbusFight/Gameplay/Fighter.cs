namespace LimbusFight.Gameplay;

public enum FighterState
{
    Idle,
    WalkForward,
    WalkBackward,
    JumpSquat,
    Airborne,
    Landing,
    Attack,
    Hitstun,

    /// <summary>Stagger bar emptied: cannot act, takes extra damage (Step 5).</summary>
    Staggered,

    /// <summary>Lost (or tied) a clash: knocked back, briefly unable to act.</summary>
    ClashRecoil,

    /// <summary>Knocked out: health reached 0.</summary>
    Down,

    /// <summary>Holding the defense button with a Guard defense (Step 6).</summary>
    Guard,

    /// <summary>Hit while guarding: briefly stuck in guard.</summary>
    Blockstun,

    /// <summary>Evade defense: backstep with invincible frames.</summary>
    Evade,

    /// <summary>Counter defense: parry window.</summary>
    Counter,
}

/// <summary>
/// A test fighter: a box with a frame-accurate state machine, push/hurt/hit boxes,
/// an input buffer and simple jump physics. Positions are in stage pixels; the origin
/// (X, Y) is the centre of the fighter's feet. All speeds are per 60 Hz frame.
/// </summary>
public sealed class Fighter
{
    // Body, movement and moves come from <see cref="Stats"/> (a fighter data file); see FighterStats.

    /// <summary>How fast pushback after a hit slows down (same for every fighter).</summary>
    public const float PushbackDecay = 0.7f;

    /// <summary>A press is remembered this many frames, so it still comes out if pressed slightly early.</summary>
    public const int InputBufferFrames = 4;

    private InputButtons _held;
    private InputButtons _previousHeld;
    private readonly int[] _buffers = new int[8]; // one per button bit; only attack/defense buttons are used

    public Fighter(string name, float x, float floorY, int facing, bool isDummy, FighterStats? stats = null)
    {
        Name = name;
        Stats = stats ?? FighterStats.Default;
        X = x;
        Y = floorY;
        FloorY = floorY;
        Facing = facing >= 0 ? 1 : -1;
        IsDummy = isDummy;
        State = FighterState.Idle;
        StateFrame = 0;
        ResetCombat();
    }

    public string Name { get; }

    /// <summary>Body boxes, movement and moves of the fighter in this slot.</summary>
    public FighterStats Stats { get; private set; }

    public bool IsDummy { get; }

    public float FloorY { get; }

    public float X { get; set; }

    public float Y { get; private set; }

    public float VelocityX { get; private set; }

    public float VelocityY { get; private set; }

    /// <summary>Signed slide speed applied after an attack connects (decays to 0).</summary>
    public float PushbackVelocity { get; private set; }

    /// <summary>+1 = facing right, -1 = facing left.</summary>
    public int Facing { get; private set; }

    public FighterState State { get; private set; }

    /// <summary>1-based frame counter inside the current state (frame 1 = the frame it was entered).</summary>
    public int StateFrame { get; private set; }

    public AttackData? CurrentAttack { get; private set; }

    /// <summary>True once the current attack has hit; one hit per attack.</summary>
    public bool AttackConnected => CurrentAttack is { } a && CoinsLanded >= a.Coins;

    public int HitstunRemaining { get; private set; }

    public int HitsTaken { get; private set; }

    public int ComboCount { get; private set; }

    public bool IsAirborne => State == FighterState.Airborne;

    /// <summary>Free to walk, jump or attack this frame.</summary>
    public bool IsActionable => State is FighterState.Idle or FighterState.WalkForward or FighterState.WalkBackward;

    // =========================================================================================
    //  Per-frame update
    // =========================================================================================

    /// <summary>
    /// Records this frame's buttons and detects press edges into the input buffer.
    /// Called every frame, including hitstop frames, so presses during hitstop are not lost.
    /// </summary>
    public void ReadInput(FrameInput input, bool frozen)
    {
        _previousHeld = _held;
        _held = input.Held; // the arena decides what the dummy presses (stand still or fight back)

        InputButtons pressed = _held & ~_previousHeld;

        foreach (InputButtons button in BufferedButtons)
        {
            int i = BitIndex(button);
            if ((pressed & button) != 0)
            {
                _buffers[i] = InputBufferFrames;
            }
            else if (!frozen && _buffers[i] > 0)
            {
                _buffers[i]--;
            }
        }
    }

    private static readonly InputButtons[] BufferedButtons =
        { InputButtons.Punch, InputButtons.Skill2, InputButtons.Skill3, InputButtons.Defend, InputButtons.Ego };

    private static int BitIndex(InputButtons button) => System.Numerics.BitOperations.TrailingZeroCount((int)button);

    private bool Buffered(InputButtons button) => _buffers[BitIndex(button)] > 0;

    private void ClearBuffer(InputButtons button) => _buffers[BitIndex(button)] = 0;

    /// <summary>Advances the state machine and physics by one frame. Skipped during hitstop.</summary>
    public void Update()
    {
        StateFrame++;
        StartedAttack = false;
        StartedEgo = false;

        if (PanicRemaining > 0 && --PanicRemaining == 0)
        {
            Sanity = 0; // Panic is over: back to a calm mind
        }

        // 1. Timed states expire at the start of the frame after their last frame.
        switch (State)
        {
            case FighterState.JumpSquat when StateFrame > Stats.JumpSquatFrames:
                TakeOff();
                break;

            case FighterState.Landing when StateFrame > Stats.LandingFrames:
                Enter(FighterState.Idle);
                break;

            case FighterState.Attack when CurrentAttack is not null && StateFrame > CurrentAttack.TotalFrames:
                CurrentAttack = null;
                Enter(FighterState.Idle);
                break;

            case FighterState.Hitstun when HitstunRemaining <= 0:
                ComboCount = 0;
                Enter(FighterState.Idle);
                break;

            case FighterState.Staggered when StaggerRemaining <= 0:
                Stagger = Stats.StaggerMax;
                ComboCount = 0;
                Enter(FighterState.Idle);
                break;

            case FighterState.ClashRecoil when RecoilRemaining <= 0:
                Enter(FighterState.Idle);
                break;

            case FighterState.Guard when (_held & InputButtons.Defend) == 0:
                Enter(FighterState.Idle);
                break;

            case FighterState.Blockstun when BlockstunRemaining <= 0:
                Enter((_held & InputButtons.Defend) != 0 && Stats.Defense.Kind == DefenseKind.Guard ? FighterState.Guard : FighterState.Idle);
                break;

            case FighterState.Evade or FighterState.Counter when StateFrame > Stats.Defense.TotalFrames:
                Enter(FighterState.Idle);
                break;
        }

        // 2. Neutral: walk / jump / attack.
        if (IsActionable)
        {
            HandleNeutral();
        }
        else if (State == FighterState.Evade)
        {
            // Step back through the invincible frames, then stop.
            VelocityX = StateFrame <= Stats.Defense.WindowEnd ? -Facing * Stats.Defense.EvadeSpeed : 0f;
        }
        else if (State is FighterState.JumpSquat or FighterState.Landing or FighterState.Attack or FighterState.Hitstun
                 or FighterState.Staggered or FighterState.ClashRecoil or FighterState.Down
                 or FighterState.Guard or FighterState.Blockstun or FighterState.Counter)
        {
            VelocityX = 0f;
        }

        if (State == FighterState.Hitstun)
        {
            HitstunRemaining--;
        }
        else if (State == FighterState.Staggered)
        {
            StaggerRemaining--;
        }
        else if (State == FighterState.ClashRecoil)
        {
            RecoilRemaining--;
        }
        else if (State == FighterState.Blockstun)
        {
            BlockstunRemaining--;
        }

        // 3. Physics.
        if (State == FighterState.Airborne)
        {
            VelocityY += Stats.Gravity;
        }

        X += VelocityX + PushbackVelocity;
        Y += VelocityY;

        PushbackVelocity = PushbackVelocity > 0
            ? MathF.Max(0f, PushbackVelocity - PushbackDecay)
            : MathF.Min(0f, PushbackVelocity + PushbackDecay);

        if (State == FighterState.Airborne && Y >= FloorY)
        {
            Y = FloorY;
            VelocityX = 0f;
            VelocityY = 0f;
            Enter(FighterState.Landing);
        }
    }

    /// <summary>Auto-face the opponent whenever this fighter is in neutral (standard fighting-game rule).</summary>
    public void FaceTowards(Fighter opponent)
    {
        if (!IsActionable)
        {
            return;
        }

        if (opponent.X > X + 0.5f)
        {
            Facing = 1;
        }
        else if (opponent.X < X - 0.5f)
        {
            Facing = -1;
        }
    }

    /// <summary>Returns the fighter to a fresh idle state at the given position.</summary>
    public void ResetTo(float x, int facing)
    {
        X = x;
        Y = FloorY;
        VelocityX = 0f;
        VelocityY = 0f;
        PushbackVelocity = 0f;
        Facing = facing >= 0 ? 1 : -1;
        CurrentAttack = null;
        CoinsLanded = 0;
        HitstunRemaining = 0;
        HitsTaken = 0;
        ComboCount = 0;
        Array.Clear(_buffers);
        State = FighterState.Idle;
        StateFrame = 0;
        ResetCombat();
    }

    /// <summary>Full health and Stagger bar, calm Sanity, no Sins or statuses (start of a round).</summary>
    public void ResetCombat()
    {
        Hp = Stats.MaxHp;
        Stagger = Stats.StaggerMax;
        Sanity = 0;
        PanicRemaining = 0;
        CritMeter = 0;
        BurnTimer = 0;
        StaggerRemaining = 0;
        RecoilRemaining = 0;
        Array.Clear(Sins);
        Array.Clear(Statuses);
    }

    /// <summary>Swaps in another fighter's stats (a different data file, or an edit) and returns to idle in place.</summary>
    public void SetStats(FighterStats stats)
    {
        Stats = stats;
        ResetTo(X, Facing);
    }

    /// <summary>Called by the arena when this fighter's attack hits.</summary>
    public void OnAttackConnected()
    {
        if (CurrentAttack is null)
        {
            return;
        }

        CoinsLanded++;
        PushbackVelocity = -Facing * CurrentAttack.Pushback;
    }

    /// <summary>Called by the arena when this fighter is hit.</summary>
    public void TakeHit(AttackData attack)
    {
        bool comboing = State is FighterState.Hitstun or FighterState.Staggered;
        ComboCount = comboing ? ComboCount + 1 : 1;
        HitsTaken++;
        CurrentAttack = null;
        VelocityX = 0f;

        // A Staggered (or knocked out) fighter stays that way; hits do not restart hitstun.
        if (State is FighterState.Staggered or FighterState.Down)
        {
            return;
        }

        HitstunRemaining = attack.Hitstun;
        Enter(FighterState.Hitstun);
    }

    // =========================================================================================
    //  Combat state (Step 5). The rules that change it live in TestArena / CombatRules.
    // =========================================================================================

    public int Hp { get; set; }

    /// <summary>What is left of the Stagger bar (0 = Staggered).</summary>
    public int Stagger { get; set; }

    /// <summary>Sanity, -45 (Panic) to +45.</summary>
    public int Sanity { get; private set; }

    public int PanicRemaining { get; private set; }

    public bool IsPanicking => PanicRemaining > 0;

    /// <summary>Sin resources, indexed by <see cref="Gameplay.Sin"/>.</summary>
    public int[] Sins { get; } = new int[7];

    /// <summary>Status stacks, indexed by <see cref="StatusKind"/>.</summary>
    public StatusStack[] Statuses { get; } = new StatusStack[7];

    /// <summary>Poise's crit meter (deterministic stand-in for Limbus's crit chance).</summary>
    public int CritMeter { get; set; }

    /// <summary>Frames since Burn last dealt damage.</summary>
    public int BurnTimer { get; set; }

    public int StaggerRemaining { get; private set; }

    public int RecoilRemaining { get; private set; }

    /// <summary>True on the frame an attack began (Bleed hurts then).</summary>
    public bool StartedAttack { get; private set; }

    public ref StatusStack Status(StatusKind kind) => ref Statuses[(int)kind];

    public void AddStatus(StatusApply apply)
    {
        ref StatusStack stack = ref Status(apply.Kind);
        stack.Potency = Math.Min(CombatRules.StatusCap, stack.Potency + apply.Potency);
        stack.Count = Math.Min(CombatRules.StatusCap, stack.Count + apply.Count);
    }

    /// <summary>Uses up one count of a status; the stack is gone when its count reaches 0.</summary>
    public int ConsumeStatus(StatusKind kind)
    {
        ref StatusStack stack = ref Status(kind);
        if (!stack.Active)
        {
            return 0;
        }

        int potency = stack.Potency;
        if (--stack.Count <= 0)
        {
            stack = default;
        }

        return potency;
    }

    /// <summary>Changes Sanity. Returns true if this pushed the fighter into Panic.</summary>
    public bool AddSanity(int delta)
    {
        if (IsPanicking)
        {
            return false;
        }

        Sanity = Math.Clamp(Sanity + delta, CombatRules.SanityMin, CombatRules.SanityMax);
        if (Sanity <= CombatRules.SanityMin)
        {
            PanicRemaining = CombatRules.PanicFrames;
            return true;
        }

        return false;
    }

    public void AddSin(Sin sin)
    {
        if (!IsPanicking)
        {
            Sins[(int)sin] = Math.Min(CombatRules.SinCap, Sins[(int)sin] + 1);
        }
    }

    public void EnterStaggered()
    {
        CurrentAttack = null;
        VelocityX = 0f;
        StaggerRemaining = CombatRules.StaggerFrames;
        Enter(FighterState.Staggered);
    }

    /// <summary>Clash lost or tied: the attack is cancelled and the fighter slides back.</summary>
    public void EnterClashRecoil(int frames, float push)
    {
        CurrentAttack = null;
        VelocityX = 0f;
        PushbackVelocity = -Facing * push;
        RecoilRemaining = frames;
        Enter(FighterState.ClashRecoil);
    }

    public void EnterDown()
    {
        CurrentAttack = null;
        VelocityX = 0f;
        Enter(FighterState.Down);
    }

    // =========================================================================================
    //  Boxes
    // =========================================================================================

    /// <summary>Solid body used for fighter-vs-fighter collision (yellow in debug view).</summary>
    public Box Pushbox => new(X - (Stats.PushboxWidth * 0.5f), Y - Stats.PushboxHeight, Stats.PushboxWidth, Stats.PushboxHeight);

    /// <summary>Main body hurtbox (blue in debug view).</summary>
    public Box BodyHurtbox => new(X - (Stats.HurtboxWidth * 0.5f), Y - Stats.HurtboxHeight, Stats.HurtboxWidth, Stats.HurtboxHeight);

    /// <summary>Extended-arm hurtbox while punching: from the last startup frame through recovery.</summary>
    public Box? LimbHurtbox
    {
        get
        {
            if (State != FighterState.Attack || CurrentAttack is not { } attack || StateFrame < attack.Startup)
            {
                return null;
            }

            return ForwardBox(attack.HitboxOffsetX, attack.LimbHurtboxWidth, attack.HitboxOffsetY + 2f, attack.HitboxHeight - 4f);
        }
    }

    /// <summary>All hurtboxes this frame.</summary>
    public IEnumerable<Box> Hurtboxes
    {
        get
        {
            if (IsInvincible)
            {
                yield break;
            }

            yield return BodyHurtbox;

            if (LimbHurtbox is Box limb)
            {
                yield return limb;
            }
        }
    }

    /// <summary>Hitbox geometry during active frames (even if it already connected), else null.</summary>
    public Box? ActiveFrameHitbox
    {
        get
        {
            if (State != FighterState.Attack || CurrentAttack is not { } attack || !attack.IsActiveFrame(StateFrame))
            {
                return null;
            }

            return ForwardBox(attack.HitboxOffsetX, attack.HitboxWidth, attack.HitboxOffsetY, attack.HitboxHeight);
        }
    }

    /// <summary>
    /// The hitbox that can still hit this frame (red in debug view), else null. Each coin is one hit:
    /// the next coin can land <see cref="AttackData.CoinInterval"/> frames after the previous one.
    /// </summary>
    public Box? LiveHitbox
    {
        get
        {
            if (CurrentAttack is not { } attack || CoinsLanded >= attack.Coins)
            {
                return null;
            }

            int nextCoinFrame = attack.FirstActiveFrame + (CoinsLanded * attack.CoinInterval);
            return StateFrame >= nextCoinFrame ? ActiveFrameHitbox : null;
        }
    }

    /// <summary>Hits landed by the current attack (one per coin).</summary>
    public int CoinsLanded { get; private set; }

    /// <summary>Evading through the invincible frames: nothing can hit this fighter.</summary>
    public bool IsInvincible => State == FighterState.Evade && Stats.Defense.InWindow(StateFrame);

    /// <summary>True on the frame an E.G.O began (for the E.G.O banner).</summary>
    public bool StartedEgo { get; private set; }

    /// <summary>Whether the E.G.O can be used now: it exists, its Sin cost is met, and no Panic.</summary>
    public bool CanUseEgo => Stats.Ego is { } ego && !IsPanicking && ego.SinCost.All(c => Sins[(int)c.Sin] >= c.Amount);

    public int BlockstunRemaining { get; private set; }

    /// <summary>Guarded a hit: blockstun instead of hitstun.</summary>
    public void TakeBlock(AttackData attack)
    {
        CurrentAttack = null;
        VelocityX = 0f;
        BlockstunRemaining = Math.Max(1, attack.Hitstun / 2);
        Enter(FighterState.Blockstun);
    }

    /// <summary>Counter landed: back to neutral at once.</summary>
    public void CounterSucceeded() => Enter(FighterState.Idle);

    /// <summary>0..1 arm extension for drawing the punch.</summary>
    public float ArmExtension
    {
        get
        {
            if (State != FighterState.Attack || CurrentAttack is not { } attack)
            {
                return 0f;
            }

            if (StateFrame <= attack.Startup)
            {
                return 0.6f * StateFrame / attack.Startup;
            }

            if (StateFrame <= attack.LastActiveFrame)
            {
                return 1f;
            }

            int recoveryFrame = StateFrame - attack.LastActiveFrame;
            return MathF.Max(0f, 1f - ((float)recoveryFrame / attack.Recovery));
        }
    }

    /// <summary>Short text for the debug HUD, e.g. "Attack 7/17 ACTIVE".</summary>
    public string StateDescription => State switch
    {
        FighterState.Attack when CurrentAttack is not null =>
            $"{CurrentAttack.Name} {StateFrame}/{CurrentAttack.TotalFrames} {CurrentAttack.PhaseAt(StateFrame)}",
        FighterState.Hitstun => $"Hitstun ({HitstunRemaining} left)",
        FighterState.Staggered => $"STAGGERED ({StaggerRemaining} left)",
        FighterState.ClashRecoil => $"Clash recoil ({RecoilRemaining} left)",
        FighterState.JumpSquat => $"JumpSquat {StateFrame}/{Stats.JumpSquatFrames}",
        FighterState.Landing => $"Landing {StateFrame}/{Stats.LandingFrames}",
        _ => $"{State} f{StateFrame}",
    };

    // =========================================================================================
    //  Internals
    // =========================================================================================

    private void HandleNeutral()
    {
        // Strongest first: E.G.O, Skill 3, Skill 2, Skill 1.
        if (Buffered(InputButtons.Ego) && CanUseEgo)
        {
            ClearBuffer(InputButtons.Ego);
            foreach (SinCost cost in Stats.Ego!.SinCost)
            {
                Sins[(int)cost.Sin] -= cost.Amount;
            }

            StartedEgo = true;
            StartAttack(Stats.Ego);
            return;
        }

        foreach ((InputButtons button, AttackData? move) in new[] { (InputButtons.Skill3, Stats.Skill3), (InputButtons.Skill2, Stats.Skill2), (InputButtons.Punch, (AttackData?)Stats.Punch) })
        {
            if (Buffered(button) && move is not null)
            {
                ClearBuffer(button);
                StartAttack(move);
                return;
            }
        }

        // Defense: Guard while held; Evade / Counter on press.
        if (Stats.Defense.Kind == DefenseKind.Guard && (_held & InputButtons.Defend) != 0)
        {
            VelocityX = 0f;
            Enter(FighterState.Guard);
            return;
        }

        if (Stats.Defense.Kind != DefenseKind.Guard && Buffered(InputButtons.Defend))
        {
            ClearBuffer(InputButtons.Defend);
            VelocityX = 0f;
            Enter(Stats.Defense.Kind == DefenseKind.Evade ? FighterState.Evade : FighterState.Counter);
            return;
        }

        if ((_held & InputButtons.Up) != 0)
        {
            VelocityX = 0f;
            Enter(FighterState.JumpSquat);
            return;
        }

        int horizontal = new FrameInput(_held).Horizontal;
        int relative = horizontal * Facing;

        FighterState walkState = relative > 0 ? FighterState.WalkForward
            : relative < 0 ? FighterState.WalkBackward
            : FighterState.Idle;

        if (walkState != State)
        {
            Enter(walkState);
        }

        VelocityX = relative > 0 ? horizontal * Stats.WalkForwardSpeed
            : relative < 0 ? horizontal * Stats.WalkBackwardSpeed
            : 0f;
    }

    private void StartAttack(AttackData move)
    {
        CurrentAttack = move;
        CoinsLanded = 0;
        VelocityX = 0f;
        StartedAttack = true;
        Enter(FighterState.Attack);
    }

    private void TakeOff()
    {
        int horizontal = new FrameInput(_held).Horizontal;
        VelocityX = horizontal * Stats.JumpHorizontalSpeed;
        VelocityY = -Stats.JumpLaunchSpeed;
        Enter(FighterState.Airborne);
    }

    private void Enter(FighterState state)
    {
        State = state;
        StateFrame = 1;
    }

    private Box ForwardBox(float offsetX, float width, float offsetY, float height)
    {
        float left = Facing > 0 ? X + offsetX : X - offsetX - width;
        return new Box(left, Y + offsetY, width, height);
    }
}
