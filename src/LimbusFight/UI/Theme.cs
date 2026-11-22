using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>Colour palette for the debug build (dark UI with Limbus-style gold accents).</summary>
public static class Theme
{
    public static readonly Color Background = new(12, 12, 18, 255);
    public static readonly Color BackgroundHorizon = new(30, 24, 32, 255);
    public static readonly Color Panel = new(20, 20, 28, 240);
    public static readonly Color PanelBorder = new(78, 64, 48, 255);
    public static readonly Color Accent = new(222, 174, 84, 255);

    public static readonly Color Text = new(228, 228, 234, 255);
    public static readonly Color TextDim = new(138, 138, 154, 255);

    public static readonly Color Good = new(92, 212, 122, 255);
    public static readonly Color Warn = new(242, 182, 62, 255);
    public static readonly Color Bad = new(238, 72, 72, 255);

    public static readonly Color Floor = new(28, 26, 34, 255);
    public static readonly Color FloorMarks = new(54, 50, 62, 255);
    public static readonly Color Wall = new(60, 52, 44, 255);
    public static readonly Color Shadow = new(0, 0, 0, 110);

    public static readonly Color Player1Body = new(178, 44, 54, 255);
    public static readonly Color Player1Trim = new(240, 196, 120, 255);
    public static readonly Color DummyBody = new(94, 98, 116, 255);
    public static readonly Color DummyTrim = new(170, 176, 196, 255);
    public static readonly Color HitFlash = new(255, 244, 236, 255);

    // Debug boxes: blue = hurtbox, red = active hitbox, yellow = pushbox.
    public static readonly Color Hurtbox = new(56, 140, 255, 255);
    public static readonly Color Hitbox = new(255, 48, 48, 255);
    public static readonly Color HitboxSpent = new(130, 40, 40, 255);
    public static readonly Color Pushbox = new(250, 218, 64, 255);

    // Warning screen.
    public static readonly Color AlertBackground = new(44, 6, 10, 255);
    public static readonly Color AlertPanel = new(70, 12, 18, 255);
    public static readonly Color AlertBorder = new(200, 40, 48, 255);
    public static readonly Color AlertText = new(255, 214, 214, 255);
}
