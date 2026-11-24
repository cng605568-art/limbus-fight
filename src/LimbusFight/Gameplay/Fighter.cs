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
    private int _punchBuffer;

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
    public bool AttackConnected { get; private set; }

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
        _held = IsDummy ? InputButtons.None : input.Held;

        InputButtons pressed = _held & ~_previousHeld;

        if ((pressed & InputButtons.Punch) != 0)
        {
            _punchBuffer = InputBufferFrames;
        }
        else if (!frozen && _punchBuffer > 0)
        {
            _punchBuffer--;
        }
    }

    /// <summary>Advances the state machine and physics by one frame. Skipped during hitstop.</summary>
    public void Update()
    {
        StateFrame++;

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
        }

        // 2. Neutral: walk / jump / attack.
        if (IsActionable)
        {
            HandleNeutral();
        }
        else if (State is FighterState.JumpSquat or FighterState.Landing or FighterState.Attack or FighterState.Hitstun)
        {
            VelocityX = 0f;
        }

        if (State == FighterState.Hitstun)
        {
            HitstunRemaining--;
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
        AttackConnected = false;
        HitstunRemaining = 0;
        HitsTaken = 0;
        ComboCount = 0;
        _punchBuffer = 0;
        State = FighterState.Idle;
        StateFrame = 0;
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

        AttackConnected = true;
        PushbackVelocity = -Facing * CurrentAttack.Pushback;
    }

    /// <summary>Called by the arena when this fighter is hit.</summary>
    public void TakeHit(AttackData attack)
    {
        bool comboing = State == FighterState.Hitstun;
        ComboCount = comboing ? ComboCount + 1 : 1;
        HitsTaken++;
        HitstunRemaining = attack.Hitstun;
        CurrentAttack = null;
        VelocityX = 0f;
        Enter(FighterState.Hitstun);
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

    /// <summary>The hitbox that can still hit this frame (red in debug view), else null.</summary>
    public Box? LiveHitbox => AttackConnected ? null : ActiveFrameHitbox;

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
        FighterState.JumpSquat => $"JumpSquat {StateFrame}/{Stats.JumpSquatFrames}",
        FighterState.Landing => $"Landing {StateFrame}/{Stats.LandingFrames}",
        _ => $"{State} f{StateFrame}",
    };

    // =========================================================================================
    //  Internals
    // =========================================================================================

    private void HandleNeutral()
    {
        if (_punchBuffer > 0)
        {
            _punchBuffer = 0;
            CurrentAttack = Stats.Punch;
            AttackConnected = false;
            VelocityX = 0f;
            Enter(FighterState.Attack);
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
