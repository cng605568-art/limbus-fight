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

/// <summary>One move: frame data plus its hitbox. Step 4 has one, on the punch button.</summary>
public sealed class MoveData
{
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

    public AttackData ToAttack() => new()
    {
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

    public List<MoveData> Moves { get; set; } = new() { new MoveData() };

    /// <summary>How it looks: clips of Limbus sprite names (see Animation/LookData).</summary>
    public LookData Look { get; set; } = new();

    /// <summary>The move on the punch button (created if missing).</summary>
    [JsonIgnore]
    public MoveData Punch
    {
        get
        {
            MoveData? move = Moves.FirstOrDefault(m => m.Button == "punch");
            if (move is null)
            {
                move = new MoveData();
                Moves.Add(move);
            }

            return move;
        }
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
        Punch = Punch.ToAttack(),
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
        }

        _ = Punch; // make sure the punch button has a move
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
