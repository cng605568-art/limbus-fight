namespace LimbusFight.Gameplay;

/// <summary>
/// Step 1 sandbox match: Player 1 vs a static training dummy on a flat stage.
/// Owns the frame counter, hitstop, collision and hit detection. Pure simulation:
/// no rendering, no Raylib, no wall-clock time.
/// </summary>
public sealed class TestArena
{
    public const float StageWidth = 1280f;
    public const float FloorY = 640f;
    public const float WallLeft = 40f;
    public const float WallRight = 1240f;

    public const float Player1StartX = 460f;
    public const float DummyStartX = 820f;

    public TestArena()
    {
        Player1 = new Fighter("P1", Player1StartX, FloorY, facing: 1, isDummy: false);
        Dummy = new Fighter("DUMMY", DummyStartX, FloorY, facing: -1, isDummy: true);
    }

    public Fighter Player1 { get; }

    public Fighter Dummy { get; }

    /// <summary>Simulation frames since the arena was created (keeps counting during hitstop).</summary>
    public long Frame { get; private set; }

    /// <summary>Remaining freeze frames from the last hit.</summary>
    public int HitstopRemaining { get; private set; }

    public int TotalHits { get; private set; }

    /// <summary>Frame number of the most recent hit (-1 = none yet).</summary>
    public long LastHitFrame { get; private set; } = -1;

    /// <summary>Stage position of the most recent hit spark.</summary>
    public (float X, float Y) LastHitPoint { get; private set; }

    /// <summary>Advances the match by exactly one 60 Hz frame.</summary>
    public void Tick(FrameInput player1Input)
    {
        Frame++;

        bool frozen = HitstopRemaining > 0;
        Player1.ReadInput(player1Input, frozen);
        Dummy.ReadInput(FrameInput.None, frozen);

        if (frozen)
        {
            HitstopRemaining--;
            return;
        }

        Player1.Update();
        Dummy.Update();

        ResolvePushboxes();
        ClampToStage(Player1);
        ClampToStage(Dummy);

        Player1.FaceTowards(Dummy);
        Dummy.FaceTowards(Player1);

        ResolveHit(attacker: Player1, defender: Dummy);
    }

    /// <summary>Puts both fighters back at their start positions (the frame counter keeps running).</summary>
    public void ResetPositions()
    {
        Player1.ResetTo(Player1StartX, facing: 1);
        Dummy.ResetTo(DummyStartX, facing: -1);
        HitstopRemaining = 0;
        TotalHits = 0;
        LastHitFrame = -1;
    }

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

        // The dummy is static: Player 1 absorbs the whole correction...
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
        float half = Fighter.PushboxWidth * 0.5f;
        fighter.X = Math.Clamp(fighter.X, WallLeft + half, WallRight - half);
    }

    private void ResolveHit(Fighter attacker, Fighter defender)
    {
        if (attacker.LiveHitbox is not Box hitbox || attacker.CurrentAttack is not { } attack)
        {
            return;
        }

        foreach (Box hurtbox in defender.Hurtboxes)
        {
            if (!hitbox.Intersects(hurtbox))
            {
                continue;
            }

            attacker.OnAttackConnected();
            defender.TakeHit(attack);

            HitstopRemaining = attack.Hitstop;
            TotalHits++;
            LastHitFrame = Frame;
            LastHitPoint = hitbox.IntersectionCenter(hurtbox);
            return;
        }
    }
}
