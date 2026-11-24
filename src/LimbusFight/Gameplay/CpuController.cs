namespace LimbusFight.Gameplay;

/// <summary>How hard the computer fights.</summary>
public enum CpuLevel
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// The computer opponent: walks to a range its moves reach, attacks, punishes whiffs, defends on
/// reaction (slower and less often on easier levels) and uses E.G.O when it has the Sins. Uses its
/// own seeded random numbers, so a match with the same inputs and seed plays out the same.
/// Pure simulation: no Raylib types.
/// </summary>
public sealed class CpuController
{
    private uint _rng = 0x9E3779B9u;
    private int _decisionWait;
    private InputButtons _held;
    private int _heldFrames;
    private int _defendIn = -1;
    private int _defendFrames;
    private long _reactedToAttackFrame = -1;

    public CpuLevel Level { get; set; } = CpuLevel.Normal;

    /// <summary>Starts a new match: forgets plans, reseeds the random numbers.</summary>
    public void Reset(uint seed)
    {
        _rng = seed == 0 ? 0x9E3779B9u : seed;
        _decisionWait = 0;
        _held = InputButtons.None;
        _heldFrames = 0;
        _defendIn = -1;
        _defendFrames = 0;
        _reactedToAttackFrame = -1;
    }

    // ---- Tuning per level --------------------------------------------------------------------

    /// <summary>Frames between decisions (thinking speed).</summary>
    private int DecisionFrames => Level switch { CpuLevel.Easy => 16, CpuLevel.Normal => 9, _ => 4 };

    /// <summary>Frames before it reacts to an attack starting.</summary>
    private int ReactionFrames => Level switch { CpuLevel.Easy => 20, CpuLevel.Normal => 11, _ => 5 };

    /// <summary>Chance to defend against an attack it sees coming.</summary>
    private float DefendChance => Level switch { CpuLevel.Easy => 0.12f, CpuLevel.Normal => 0.35f, _ => 0.65f };

    /// <summary>Chance to attack when in range at a decision.</summary>
    private float AttackChance => Level switch { CpuLevel.Easy => 0.32f, CpuLevel.Normal => 0.5f, _ => 0.62f };

    /// <summary>Chance to punish an opponent that is stuck (whiffed, staggered, landing).</summary>
    private float PunishChance => Level switch { CpuLevel.Easy => 0.15f, CpuLevel.Normal => 0.55f, _ => 0.9f };

    private float EgoChance => Level switch { CpuLevel.Easy => 0.15f, CpuLevel.Normal => 0.4f, _ => 0.8f };

    private float JumpChance => Level switch { CpuLevel.Easy => 0.03f, CpuLevel.Normal => 0.05f, _ => 0.04f };

    // ---- Thinking ----------------------------------------------------------------------------

    /// <summary>The CPU's input for this frame.</summary>
    public FrameInput Think(TestArena arena, Fighter me, Fighter foe)
    {
        if (arena.Phase != RoundPhase.Fight)
        {
            _held = InputButtons.None;
            return FrameInput.None;
        }

        float distance = MathF.Abs(foe.X - me.X);
        InputButtons toward = foe.X < me.X ? InputButtons.Left : InputButtons.Right;
        InputButtons away = toward == InputButtons.Left ? InputButtons.Right : InputButtons.Left;

        // 1. Defending: keep holding a guard for the planned time.
        if (_defendFrames > 0)
        {
            _defendFrames--;
            return new FrameInput(InputButtons.Defend);
        }

        // 2. The opponent just started an attack that can reach: maybe defend after the reaction time.
        if (foe.State == FighterState.Attack && foe.CurrentAttack is { } incoming && foe.StateFrame <= 2
            && _reactedToAttackFrame != arena.Frame - foe.StateFrame
            && distance <= Reach(incoming, me) + 40f)
        {
            _reactedToAttackFrame = arena.Frame - foe.StateFrame;
            if (Roll() < DefendChance && ReactionFrames < incoming.Startup + 2)
            {
                _defendIn = ReactionFrames + Jitter(3);
            }
        }

        if (_defendIn >= 0)
        {
            if (_defendIn-- == 0 && me.IsActionable)
            {
                // Guard: hold through the attack. Evade / counter: one press at the right time.
                _defendFrames = me.Stats.Defense.Kind == DefenseKind.Guard ? 14 + Jitter(6) : 0;
                return new FrameInput(InputButtons.Defend);
            }
        }

        if (!me.IsActionable)
        {
            return FrameInput.None;
        }

        // 3. Between decisions, keep doing what was decided (walking, mostly).
        if (_decisionWait > 0)
        {
            _decisionWait--;
            if (_heldFrames > 0)
            {
                _heldFrames--;
                return new FrameInput(_held);
            }

            return FrameInput.None;
        }

        _decisionWait = DecisionFrames + Jitter(DecisionFrames / 2);
        _held = InputButtons.None;
        _heldFrames = 0;

        AttackData? best = PickMove(me, foe, distance);

        // 4. Punish an opponent that cannot act.
        bool foeStuck = foe.State is FighterState.Staggered or FighterState.ClashRecoil or FighterState.Landing or FighterState.JumpSquat
            || (foe.State == FighterState.Attack && foe.CurrentAttack is { } fa && foe.StateFrame > fa.Startup + fa.Active);
        if (foeStuck && best is not null && Roll() < PunishChance)
        {
            return Press(best);
        }

        // 5. E.G.O when the Sins are there and it reaches.
        if (me.CanUseEgo && me.Stats.Ego is { } ego && distance <= Reach(ego, foe) && Roll() < EgoChance)
        {
            return new FrameInput(InputButtons.Ego);
        }

        // 6. In range: attack (or sometimes step back to bait on higher levels).
        if (best is not null)
        {
            float r = Roll();
            if (r < AttackChance)
            {
                return Press(best);
            }

            if (Level != CpuLevel.Easy && r > 0.9f)
            {
                return Hold(away, 10 + Jitter(8));
            }

            return FrameInput.None;
        }

        // 7. Out of range: walk in, sometimes jump in.
        if (distance > 260f && Roll() < JumpChance)
        {
            return new FrameInput(InputButtons.Up | toward);
        }

        return Hold(toward, DecisionFrames + 2);
    }

    /// <summary>The move it would use at this distance (null = nothing reaches).</summary>
    private AttackData? PickMove(Fighter me, Fighter foe, float distance)
    {
        var options = new List<AttackData>(3);
        foreach (AttackData? a in new[] { me.Stats.Punch, me.Stats.Skill2, me.Stats.Skill3 })
        {
            if (a is not null && distance <= Reach(a, foe))
            {
                options.Add(a);
            }
        }

        if (options.Count == 0)
        {
            return null;
        }

        // Faster moves are picked more on Easy; Hard mixes evenly.
        int i = (int)(Roll() * options.Count);
        return options[Math.Clamp(i, 0, options.Count - 1)];
    }

    private FrameInput Press(AttackData move) => new(move.Slot switch
    {
        MoveSlot.Skill2 => InputButtons.Skill2,
        MoveSlot.Skill3 => InputButtons.Skill3,
        MoveSlot.Ego => InputButtons.Ego,
        _ => InputButtons.Punch,
    });

    private FrameInput Hold(InputButtons buttons, int frames)
    {
        _held = buttons;
        _heldFrames = frames;
        return new FrameInput(buttons);
    }

    /// <summary>How far apart (centre to centre) the fighters can be for the move to hit.</summary>
    public static float Reach(AttackData a, Fighter target) => a.HitboxOffsetX + a.HitboxWidth + (target.Stats.HurtboxWidth / 2f) - 8f;

    /// <summary>0..1, xorshift32.</summary>
    private float Roll()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFF) / (float)0x1000000;
    }

    private int Jitter(int max) => max <= 0 ? 0 : (int)(Roll() * (max + 1));
}
