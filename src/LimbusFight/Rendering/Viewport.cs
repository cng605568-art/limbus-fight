using System.Numerics;
using Raylib_cs;

namespace LimbusFight.Rendering;

/// <summary>
/// Everything is drawn on a fixed 1280x720 canvas that is scaled (letterboxed) into the window.
/// This converts between window pixels and canvas pixels.
/// </summary>
public static class Viewport
{
    public const int Width = 1280;
    public const int Height = 720;

    /// <summary>Largest 16:9 rectangle that fits the window, centred, snapped to whole pixels.</summary>
    public static Rectangle LetterboxRect(int screenWidth, int screenHeight)
    {
        float scale = MathF.Min((float)screenWidth / Width, (float)screenHeight / Height);
        float w = MathF.Round(Width * scale);
        float h = MathF.Round(Height * scale);
        float x = MathF.Floor((screenWidth - w) * 0.5f);
        float y = MathF.Floor((screenHeight - h) * 0.5f);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>The mouse position in canvas coordinates (may be outside 0..1280 / 0..720 over the black bars).</summary>
    public static Vector2 MouseOnCanvas()
    {
        Rectangle box = LetterboxRect(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        Vector2 mouse = Raylib.GetMousePosition();

        if (box.Width <= 0 || box.Height <= 0)
        {
            return new Vector2(-1, -1);
        }

        return new Vector2((mouse.X - box.X) * Width / box.Width, (mouse.Y - box.Y) * Height / box.Height);
    }
}
