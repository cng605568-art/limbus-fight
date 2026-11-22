namespace LimbusFight.Gameplay;

/// <summary>
/// Axis-aligned box in stage pixels (Y grows downward, like the screen).
/// Engine-agnostic on purpose: gameplay never depends on Raylib types, which keeps the
/// simulation deterministic and testable (and ready for rollback netcode later).
/// </summary>
public readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Left => X;

    public float Right => X + Width;

    public float Top => Y;

    public float Bottom => Y + Height;

    public float CenterX => X + (Width * 0.5f);

    public float CenterY => Y + (Height * 0.5f);

    /// <summary>True when the boxes overlap with positive area (touching edges do not count).</summary>
    public bool Intersects(Box other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    /// <summary>Horizontal penetration depth (only meaningful when <see cref="Intersects"/> is true).</summary>
    public float OverlapX(Box other) => MathF.Min(Right, other.Right) - MathF.Max(Left, other.Left);

    /// <summary>Centre of the overlapping region of two intersecting boxes.</summary>
    public (float X, float Y) IntersectionCenter(Box other)
    {
        float left = MathF.Max(Left, other.Left);
        float right = MathF.Min(Right, other.Right);
        float top = MathF.Max(Top, other.Top);
        float bottom = MathF.Min(Bottom, other.Bottom);
        return ((left + right) * 0.5f, (top + bottom) * 0.5f);
    }
}
