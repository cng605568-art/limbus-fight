namespace LimbusFight.Gameplay;

/// <summary>
/// Everything that makes one fighter play differently: body boxes, movement and moves. Built from a
/// fighter data file (Roster/FighterData) so a new fighter needs no new code. The defaults are the
/// Step 1 training box, so behaviour is unchanged unless a file says otherwise.
/// </summary>
public sealed record FighterStats
{
    public string Name { get; init; } = "Training Box";

    // ---- Body geometry (stage pixels) ------------------------------------------------------
    public float HurtboxWidth { get; init; } = 84f;

    public float HurtboxHeight { get; init; } = 182f;

    public float PushboxWidth { get; init; } = 70f;

    public float PushboxHeight { get; init; } = 170f;

    // ---- Movement (per 60 Hz frame) --------------------------------------------------------
    public float WalkForwardSpeed { get; init; } = 4.0f;

    public float WalkBackwardSpeed { get; init; } = 3.2f;

    public float JumpLaunchSpeed { get; init; } = 20f;

    public float Gravity { get; init; } = 1.0f;

    public float JumpHorizontalSpeed { get; init; } = 4.5f;

    public int JumpSquatFrames { get; init; } = 4;

    public int LandingFrames { get; init; } = 3;

    // ---- Moves -----------------------------------------------------------------------------
    /// <summary>The move on the punch button (J).</summary>
    public AttackData Punch { get; init; } = AttackData.TestPunch;

    public static FighterStats Default { get; } = new();
}
