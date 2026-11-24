using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using LimbusFight.UI;
using Raylib_cs;

namespace LimbusFight;

/// <summary>
/// Entry point: a 60 FPS Raylib window with the Limbus Company Steam status, the fighting sandbox
/// (Step 1), the F3 asset browser (Step 2) and fighter looks made of the player's own Limbus
/// sprites with the F4 look editor (Step 3), and fighters as data files with the F2 editor (Step 4). Everything is drawn to a fixed 1280x720
/// canvas that is scaled (letterboxed) to the window, so coordinates never depend on window size.
/// </summary>
internal static class Program
{
    public const int VirtualWidth = Viewport.Width;
    public const int VirtualHeight = Viewport.Height;
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

            if (args.Any(a => a.Equals("--scan-assets", StringComparison.OrdinalIgnoreCase)))
            {
                return AssetScanCli.Run();
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
        Raylib.InitWindow(VirtualWidth, VirtualHeight, $"{AppInfo.Name} v{AppInfo.Version} - Debug Build");

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
        var roster = new RosterManager();
        var looks = new LookManager(steam);
        var browser = new AssetBrowserScreen { Looks = looks, Roster = roster };
        var editor = new LookEditorScreen(looks, roster.Stats);
        var boxEditor = new BoxEditorScreen(roster, looks);

        // A slot's fighter (or its numbers) changed: give the arena its stats and the look manager its look.
        void Sync(LookSlot slot)
        {
            FighterData fighter = roster.Get(slot);
            looks.Assign(slot, fighter.Look, look =>
            {
                roster.SetLook(slot, look);
                LookSlot other = slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
                if (roster.IdOf(other) == roster.IdOf(slot) && !ReferenceEquals(looks.Get(other), look))
                {
                    looks.Assign(other, look, l => roster.SetLook(other, l));
                }
            });
            arena.SetFighter(slot == LookSlot.Dummy, roster.Stats(slot));
        }

        roster.Changed += Sync;
        Sync(LookSlot.Player1);
        Sync(LookSlot.Dummy);

        bool showDebug = false;
        bool showBrowser = false;
        bool showEditor = false;
        bool showBoxEditor = false;
        double nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;

        while (!Raylib.WindowShouldClose())
        {
            keyboard.Poll();

            // ---- Global hotkeys (per render frame) --------------------------------------------
            if (keyboard.WasPressed(KeyboardKey.F2))
            {
                showBoxEditor = !showBoxEditor;
                showBrowser = false;
                showEditor = false;
            }

            if (keyboard.WasPressed(KeyboardKey.F3))
            {
                showBrowser = !showBrowser;
                showEditor = false;
                showBoxEditor = false;
                if (showBrowser)
                {
                    browser.Open(steam);
                }
            }

            if (keyboard.WasPressed(KeyboardKey.F4))
            {
                showEditor = !showEditor;
                showBrowser = false;
                showBoxEditor = false;
            }

            if (keyboard.WasPressed(KeyboardKey.F5))
            {
                steam = SteamChecker.Check();
                nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
                looks.Retry(steam);
                if (showBrowser)
                {
                    browser.Rescan(steam);
                }
            }

            bool inArena = !showBrowser && !showEditor && !showBoxEditor;
            if (inArena && keyboard.WasPressed(KeyboardKey.F1))
            {
                showDebug = !showDebug;
            }

            if (inArena && keyboard.WasPressed(KeyboardKey.R))
            {
                arena.ResetPositions();
            }

            if (inArena && keyboard.WasPressed(KeyboardKey.F6))
            {
                arena.DummyMode = arena.DummyMode == DummyMode.Stand ? DummyMode.Fight : DummyMode.Stand;
            }

            looks.Update();

            if (steam.ShouldBlockGameplay && inArena && Raylib.GetTime() >= nextAutoRescan)
            {
                steam = SteamChecker.Check();
                nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
            }

            // ---- Simulation: exactly 60 ticks per second ----------------------------------------
            int ticks = clock.Advance(Raylib.GetFrameTime());

            if (showBrowser)
            {
                keyboard.Clear();
                browser.Update(keyboard);
            }
            else if (showEditor)
            {
                keyboard.Clear();
                editor.Update(keyboard);
            }
            else if (showBoxEditor)
            {
                keyboard.Clear();
                boxEditor.Update(keyboard);
            }
            else if (steam.ShouldBlockGameplay)
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

            if (showBrowser)
            {
                browser.Draw(VirtualWidth, VirtualHeight);
            }
            else if (showEditor)
            {
                editor.Draw(VirtualWidth, VirtualHeight);
            }
            else if (showBoxEditor)
            {
                boxEditor.Draw(VirtualWidth, VirtualHeight);
            }
            else if (steam.ShouldBlockGameplay)
            {
                UpdateRequiredScreen.Draw(steam, VirtualWidth, VirtualHeight, nextAutoRescan - Raylib.GetTime());
            }
            else
            {
                ArenaRenderer.Draw(arena, showDebug, VirtualWidth, VirtualHeight, looks);
                MatchHud.DrawPopups(arena, VirtualWidth);
                SteamStatusPanel.DrawCompact(steam, VirtualWidth);
                MatchHud.Draw(arena, VirtualWidth);

                if (showDebug)
                {
                    DebugHud.Draw(arena, clock, steam, VirtualWidth);
                }

                DrawLookStatus(looks, arena);
                Ui.TextCentered(
                    "A/D move  W jump  J punch  R restart round  F1 boxes  F2 fighter  F3 assets  F4 looks  F6 dummy  ESC quit",
                    VirtualWidth / 2, VirtualHeight - 34, 20, Theme.TextDim);
            }

            Raylib.EndTextureMode();

            // ---- Present, letterboxed to the current window size --------------------------------
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Raylib.DrawTexturePro(
                canvas.Texture,
                new Rectangle(0, 0, VirtualWidth, -VirtualHeight), // negative height: render textures are stored upside down
                Viewport.LetterboxRect(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()),
                Vector2.Zero,
                0f,
                Color.White);
            Raylib.EndDrawing();
        }

        browser.Dispose();
        looks.Dispose();
        Raylib.UnloadRenderTexture(canvas);
        Raylib.CloseWindow();
    }

    /// <summary>One line above the controls: look loading progress or problems, or how to set looks up.</summary>
    private static void DrawLookStatus(LookManager looks, TestArena arena)
    {
        const int y = 652;
        string dummyMode = arena.DummyMode == DummyMode.Stand ? "Dummy: stands still (F6: fight back)" : "Dummy: fights back (F6: stand still)";
        Ui.Text(dummyMode, 20, y, 18, arena.DummyMode == DummyMode.Stand ? Theme.TextDim : Theme.Warn);
        string? p1 = looks.Status(LookSlot.Player1);
        string? dummy = looks.Status(LookSlot.Dummy);

        if (p1 is not null || dummy is not null)
        {
            string text = p1 is not null ? "P1 look: " + p1 : "Dummy look: " + dummy;
            Color color = looks.IsLoading(LookSlot.Player1) || looks.IsLoading(LookSlot.Dummy) ? Theme.Warn : Theme.Bad;
            Ui.TextRight(Ui.FitEnd(text, 18, (VirtualWidth / 2) - 40), VirtualWidth - 20, y, 18, color);
        }
        else if (looks.Get(LookSlot.Player1).IsEmpty && looks.Get(LookSlot.Dummy).IsEmpty)
        {
            Ui.TextRight("F3, open a Limbus sprite file, Ctrl+A: P1 gets a look", VirtualWidth - 20, y, 18, Theme.Accent);
        }
    }
}
