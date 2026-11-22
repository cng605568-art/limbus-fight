using System.Numerics;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.UI;
using Raylib_cs;

namespace LimbusFight;

/// <summary>
/// Step 1 entry point: a 60 FPS Raylib window that shows the Limbus Company Steam status and a
/// two-box fighting sandbox. Everything is drawn to a fixed 1280x720 canvas that is scaled
/// (letterboxed) to the window, so gameplay coordinates never depend on the window size.
/// </summary>
internal static class Program
{
    public const int VirtualWidth = 1280;
    public const int VirtualHeight = 720;
    public const int TargetFps = 60;

    /// <summary>How often the red warning screen re-reads Steam's manifest by itself.</summary>
    private const double AutoRescanSeconds = 5.0;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                return SelfTest.Run();
            }

            RunGame();
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return 1;
        }
    }

    private static void RunGame()
    {
        // Steam scan first: it is pure file/registry IO and is logged even if the window fails.
        SteamCheckResult steam = SteamChecker.Check();
        Console.WriteLine($"[LimbusFight] {AppInfo.Version} | Steam: {steam.Status} - {steam.Summary}");

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(VirtualWidth, VirtualHeight, $"{AppInfo.Name} v{AppInfo.Version} - Step 1 Debug Build");

        if (!Raylib.IsWindowReady())
        {
            throw new InvalidOperationException("Raylib could not create a window (is an OpenGL 3.3 capable GPU driver installed?).");
        }

        Raylib.SetWindowMinSize(VirtualWidth / 2, VirtualHeight / 2);
        Raylib.SetTargetFPS(TargetFps);
        Raylib.SetExitKey(KeyboardKey.Escape);

        RenderTexture2D canvas = Raylib.LoadRenderTexture(VirtualWidth, VirtualHeight);
        Raylib.SetTextureFilter(canvas.Texture, TextureFilter.Bilinear);

        var arena = new TestArena();
        var clock = new FixedStepClock(TargetFps);
        var keyboard = new KeyboardSampler();
        bool showDebug = false;
        double nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;

        while (!Raylib.WindowShouldClose())
        {
            keyboard.Poll();

            // ---- Global hotkeys (per render frame) --------------------------------------------
            if (keyboard.WasPressed(KeyboardKey.F1))
            {
                showDebug = !showDebug;
            }

            if (keyboard.WasPressed(KeyboardKey.F5))
            {
                steam = SteamChecker.Check();
                nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
            }

            if (keyboard.WasPressed(KeyboardKey.R))
            {
                arena.ResetPositions();
            }

            if (steam.ShouldBlockGameplay && Raylib.GetTime() >= nextAutoRescan)
            {
                steam = SteamChecker.Check();
                nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
            }

            // ---- Simulation: exactly 60 ticks per second ----------------------------------------
            int ticks = clock.Advance(Raylib.GetFrameTime());

            if (steam.ShouldBlockGameplay)
            {
                keyboard.Clear();
            }
            else
            {
                for (int i = 0; i < ticks; i++)
                {
                    arena.Tick(keyboard.ConsumeTick());
                }
            }

            // ---- Draw the 1280x720 canvas ------------------------------------------------------
            Raylib.BeginTextureMode(canvas);

            if (steam.ShouldBlockGameplay)
            {
                UpdateRequiredScreen.Draw(steam, VirtualWidth, VirtualHeight, nextAutoRescan - Raylib.GetTime());
            }
            else
            {
                ArenaRenderer.Draw(arena, showDebug, VirtualWidth, VirtualHeight);
                SteamStatusPanel.Draw(steam, VirtualWidth);

                if (showDebug)
                {
                    DebugHud.Draw(arena, clock, steam, VirtualWidth);
                }

                Ui.TextCentered(
                    "A / D  move     W  jump     J  punch     R  reset     F1  hitboxes + FPS     F5  re-scan Steam     ESC  quit",
                    VirtualWidth / 2, VirtualHeight - 34, 20, Theme.TextDim);
            }

            Raylib.EndTextureMode();

            // ---- Present, letterboxed to the current window size --------------------------------
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Raylib.DrawTexturePro(
                canvas.Texture,
                new Rectangle(0, 0, VirtualWidth, -VirtualHeight), // negative height: render textures are stored upside down
                LetterboxRect(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()),
                Vector2.Zero,
                0f,
                Color.White);
            Raylib.EndDrawing();
        }

        Raylib.UnloadRenderTexture(canvas);
        Raylib.CloseWindow();
    }

    /// <summary>Largest 16:9 rectangle that fits the window, centred, snapped to whole pixels.</summary>
    private static Rectangle LetterboxRect(int screenWidth, int screenHeight)
    {
        float scale = MathF.Min((float)screenWidth / VirtualWidth, (float)screenHeight / VirtualHeight);
        float w = MathF.Round(VirtualWidth * scale);
        float h = MathF.Round(VirtualHeight * scale);
        float x = MathF.Floor((screenWidth - w) * 0.5f);
        float y = MathF.Floor((screenHeight - h) * 0.5f);
        return new Rectangle(x, y, w, h);
    }
}
