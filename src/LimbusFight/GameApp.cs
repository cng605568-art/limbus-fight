using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using LimbusFight.UI;
using Raylib_cs;

namespace LimbusFight;

/// <summary>Which screen the game is on.</summary>
public enum AppScreen
{
    Title,
    Menu,
    Controls,
    Select,
    Fight,
    Tool,
}

/// <summary>What kind of match the arena is running.</summary>
public enum GameMode
{
    /// <summary>Endless rounds against the dummy (F6 modes), fighter tools on F2 / F3 / F4.</summary>
    Training,

    /// <summary>Two players on one keyboard, best of 3.</summary>
    Versus,
}

/// <summary>Fighter tools that take over the screen.</summary>
public enum ToolScreen
{
    None,
    Fighter,
    Assets,
    Looks,
}

/// <summary>
/// The whole game: title, menus, character select, matches and the fighter tools, on a fixed
/// 1280x720 canvas letterboxed to the window. One instance per run; Program creates it.
/// </summary>
public sealed class GameApp
{
    public const int VirtualWidth = Viewport.Width;
    public const int VirtualHeight = Viewport.Height;
    public const int TargetFps = 60;

    /// <summary>How often the red warning screen re-reads Steam's manifest by itself.</summary>
    private const double AutoRescanSeconds = 5.0;

    /// <summary>Frames after the match ends before the results card appears.</summary>
    private const int ResultsDelayFrames = 100;

    private readonly TestArena _arena = new();
    private readonly FixedStepClock _clock = new(TargetFps);
    private readonly KeyboardSampler _keyboard = new();
    private readonly RosterManager _roster = new();
    private readonly LookManager _looks;
    private readonly AssetBrowserScreen _browser;
    private readonly LookEditorScreen _editor;
    private readonly BoxEditorScreen _boxEditor;
    private readonly AutoRoster _autoRoster;
    private readonly SelectScreen _select;
    private readonly MenuList _menu = new();
    private readonly PauseMenu _pause = new();
    private readonly ResultsScreen _results = new();

    private SteamCheckResult _steam;
    private AppScreen _screen = AppScreen.Title;
    private AppScreen _controlsReturn = AppScreen.Menu;
    private AppScreen _toolReturn = AppScreen.Fight;
    private ToolScreen _tool = ToolScreen.None;
    private GameMode _mode = GameMode.Training;
    private bool _paused;
    private bool _showControlsOverPause;
    private bool _resultsOpen;
    private bool _showDebug;
    private bool _quit;
    private double _nextAutoRescan;
    private int _lookCount = -1;

    public GameApp(SteamCheckResult steam)
    {
        _steam = steam;
        _looks = new LookManager(steam);
        _browser = new AssetBrowserScreen { Looks = _looks, Roster = _roster };
        _editor = new LookEditorScreen(_looks, _roster.Stats);
        _browser.SelectedClip = () => (_editor.SelectedSlot, _editor.SelectedClip);
        _boxEditor = new BoxEditorScreen(_roster, _looks);
        _autoRoster = new AutoRoster(_roster);
        _select = new SelectScreen(_roster, _looks) { Status = () => _autoRoster.Status };

        _roster.Changed += Sync;
        Sync(LookSlot.Player1);
        Sync(LookSlot.Dummy);
        BuildMenu();
        _nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
        _autoRoster.Start(steam);
    }

    /// <summary>A slot's fighter (or its numbers) changed: give the arena its stats and the look manager its look.</summary>
    private void Sync(LookSlot slot)
    {
        FighterData fighter = _roster.Get(slot);
        _looks.Assign(slot, fighter.Look, look =>
        {
            _roster.SetLook(slot, look);
            LookSlot other = slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
            if (_roster.IdOf(other) == _roster.IdOf(slot) && !ReferenceEquals(_looks.Get(other), look))
            {
                _looks.Assign(other, look, l => _roster.SetLook(other, l));
            }
        });
        _arena.SetFighter(slot == LookSlot.Dummy, _roster.Stats(slot));
    }

    private void BuildMenu()
    {
        _menu.Set(new[]
        {
            new MenuList.Item("Versus", "Two players on one keyboard, best of 3."),
            new MenuList.Item("Training", "Practise against the dummy. F6 makes it fight back or defend."),
            new MenuList.Item("Controls", "Keys for both players."),
            new MenuList.Item("Fighter editor", "F2: numbers, moves, hitboxes of every fighter."),
            new MenuList.Item("Asset browser", "F3: your Limbus files; Ctrl+A builds a look."),
            new MenuList.Item("Look editor", "F4: which sprites each move uses."),
            new MenuList.Item("Quit", "Close Limbus Fight."),
        });
    }

    public void Run()
    {
        RenderTexture2D canvas = Raylib.LoadRenderTexture(VirtualWidth, VirtualHeight);
        Raylib.SetTextureFilter(canvas.Texture, TextureFilter.Bilinear);

        while (!Raylib.WindowShouldClose() && !_quit)
        {
            _keyboard.Poll();
            _looks.Update();
            _autoRoster.Update();
            if (_autoRoster.FoundCount != _lookCount)
            {
                _lookCount = _autoRoster.FoundCount;
                _select.Refresh();
            }

            Update();

            Raylib.BeginTextureMode(canvas);
            Draw();
            Raylib.EndTextureMode();

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

        _autoRoster.Dispose();
        _browser.Dispose();
        _looks.Dispose();
        Raylib.UnloadRenderTexture(canvas);
    }

    // =========================================================================================
    //  Update
    // =========================================================================================

    private void Update()
    {
        if (_keyboard.WasPressed(KeyboardKey.F5))
        {
            _steam = SteamChecker.Check();
            _nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
            _looks.Retry(_steam);
            if (_tool == ToolScreen.Assets)
            {
                _browser.Rescan(_steam);
            }
        }

        int ticks = _clock.Advance(Raylib.GetFrameTime());
        switch (_screen)
        {
            case AppScreen.Title:
                _keyboard.Clear();
                if (TitleScreen.Update(_keyboard))
                {
                    _screen = AppScreen.Menu;
                }

                break;

            case AppScreen.Menu:
                _keyboard.Clear();
                UpdateMenu();
                break;

            case AppScreen.Controls:
                _keyboard.Clear();
                if (MenuKeys.Back(_keyboard) || MenuKeys.Confirm(_keyboard))
                {
                    _screen = _controlsReturn;
                }

                break;

            case AppScreen.Select:
                _keyboard.Clear();
                UpdateSelect();
                break;

            case AppScreen.Tool:
                _keyboard.Clear();
                UpdateTool();
                break;

            case AppScreen.Fight:
                UpdateFight(ticks);
                break;
        }
    }

    private void UpdateMenu()
    {
        if (_keyboard.WasPressed(KeyboardKey.Escape))
        {
            _screen = AppScreen.Title;
            return;
        }

        if (!_menu.Update(_keyboard))
        {
            return;
        }

        switch (_menu.Selected.Label)
        {
            case "Versus":
                OpenSelect(GameMode.Versus);
                break;
            case "Training":
                OpenSelect(GameMode.Training);
                break;
            case "Controls":
                _controlsReturn = AppScreen.Menu;
                _screen = AppScreen.Controls;
                break;
            case "Fighter editor":
                OpenTool(ToolScreen.Fighter, AppScreen.Menu);
                break;
            case "Asset browser":
                OpenTool(ToolScreen.Assets, AppScreen.Menu);
                break;
            case "Look editor":
                OpenTool(ToolScreen.Looks, AppScreen.Menu);
                break;
            case "Quit":
                _quit = true;
                break;
        }
    }

    private void OpenSelect(GameMode mode)
    {
        _mode = mode;
        switch (mode)
        {
            case GameMode.Versus:
                _select.Open(twoPlayers: true, "VERSUS", "PLAYER 2");
                break;
            default:
                _select.Open(twoPlayers: false, "TRAINING", "DUMMY");
                break;
        }

        _screen = AppScreen.Select;
    }

    private void UpdateSelect()
    {
        switch (_select.Update(_keyboard))
        {
            case SelectScreen.Result.Back:
                _screen = AppScreen.Menu;
                break;
            case SelectScreen.Result.Start:
                StartMatch();
                break;
        }
    }

    private void StartMatch()
    {
        _arena.Player1.Name = "P1";
        switch (_mode)
        {
            case GameMode.Versus:
                _arena.Dummy.Name = "P2";
                _arena.RoundsToWin = 2;
                _arena.DummyMode = DummyMode.Stand;
                break;
            default:
                _arena.Dummy.Name = "DUMMY";
                _arena.RoundsToWin = 0;
                break;
        }

        _arena.NewMatch();
        _paused = false;
        _resultsOpen = false;
        _showControlsOverPause = false;
        _screen = AppScreen.Fight;
        _keyboard.Clear();
    }

    private void UpdateFight(int ticks)
    {
        if (_steam.ShouldBlockGameplay)
        {
            _keyboard.Clear();
            if (Raylib.GetTime() >= _nextAutoRescan)
            {
                _steam = SteamChecker.Check();
                _nextAutoRescan = Raylib.GetTime() + AutoRescanSeconds;
            }

            if (_keyboard.WasPressed(KeyboardKey.Escape))
            {
                _screen = AppScreen.Menu;
            }

            return;
        }

        if (_showControlsOverPause)
        {
            _keyboard.Clear();
            if (MenuKeys.Back(_keyboard) || MenuKeys.Confirm(_keyboard))
            {
                _showControlsOverPause = false;
            }

            return;
        }

        if (_paused)
        {
            _keyboard.Clear();
            switch (_pause.Update(_keyboard))
            {
                case PauseMenu.Choice.Resume:
                    _paused = false;
                    break;
                case PauseMenu.Choice.Restart:
                    _paused = false;
                    if (_mode == GameMode.Training)
                    {
                        _arena.ResetPositions();
                    }
                    else
                    {
                        _arena.NewMatch();
                    }

                    break;
                case PauseMenu.Choice.Select:
                    OpenSelect(_mode);
                    break;
                case PauseMenu.Choice.Controls:
                    _showControlsOverPause = true;
                    break;
                case PauseMenu.Choice.Menu:
                    _screen = AppScreen.Menu;
                    break;
                case PauseMenu.Choice.Quit:
                    _quit = true;
                    break;
            }

            return;
        }

        if (_resultsOpen)
        {
            _keyboard.Clear();
            switch (_results.Update(_keyboard))
            {
                case ResultsScreen.Choice.Rematch:
                    StartMatch();
                    break;
                case ResultsScreen.Choice.Select:
                    OpenSelect(_mode);
                    break;
                case ResultsScreen.Choice.Menu:
                    _screen = AppScreen.Menu;
                    break;
            }

            // The arena keeps ticking behind the card so the winner's pose stays animated.
            for (int i = 0; i < ticks; i++)
            {
                _arena.Tick(FrameInput.None, FrameInput.None);
            }

            return;
        }

        if (_keyboard.WasPressed(KeyboardKey.Escape) || _keyboard.WasPressed(KeyboardKey.P))
        {
            _pause.Open(_mode == GameMode.Training);
            _paused = true;
            _keyboard.Clear();
            return;
        }

        if (_mode == GameMode.Training)
        {
            if (_keyboard.WasPressed(KeyboardKey.F2))
            {
                OpenTool(ToolScreen.Fighter, AppScreen.Fight);
                return;
            }

            if (_keyboard.WasPressed(KeyboardKey.F3))
            {
                OpenTool(ToolScreen.Assets, AppScreen.Fight);
                return;
            }

            if (_keyboard.WasPressed(KeyboardKey.F4))
            {
                OpenTool(ToolScreen.Looks, AppScreen.Fight);
                return;
            }

            if (_keyboard.WasPressed(KeyboardKey.F1))
            {
                _showDebug = !_showDebug;
            }

            if (_keyboard.WasPressed(KeyboardKey.R))
            {
                _arena.ResetPositions();
            }

            if (_keyboard.WasPressed(KeyboardKey.F6))
            {
                _arena.DummyMode = _arena.DummyMode switch
                {
                    DummyMode.Stand => DummyMode.Fight,
                    DummyMode.Fight => DummyMode.Defend,
                    _ => DummyMode.Stand,
                };
            }
        }

        for (int i = 0; i < ticks; i++)
        {
            FrameInput p1 = _keyboard.ConsumeTick();
            if (_mode == GameMode.Versus)
            {
                _arena.Tick(p1, _keyboard.ConsumeTickPlayer2());
            }
            else
            {
                _arena.Tick(p1);
            }
        }

        if (_arena.Phase == RoundPhase.MatchOver && _arena.PhaseFrames >= ResultsDelayFrames)
        {
            _results.Open();
            _resultsOpen = true;
        }
    }

    private void OpenTool(ToolScreen tool, AppScreen returnTo)
    {
        if (returnTo == AppScreen.Menu)
        {
            // Tools from the menu edit the training slots.
            _mode = GameMode.Training;
            _arena.Player1.Name = "P1";
            _arena.Dummy.Name = "DUMMY";
            _arena.RoundsToWin = 0;
        }

        _tool = tool;
        _toolReturn = returnTo;
        _screen = AppScreen.Tool;
        if (tool == ToolScreen.Assets)
        {
            _browser.Open(_steam);
        }
    }

    private void UpdateTool()
    {
        ToolScreen? switchTo = _keyboard.WasPressed(KeyboardKey.F2) ? ToolScreen.Fighter
            : _keyboard.WasPressed(KeyboardKey.F3) ? ToolScreen.Assets
            : _keyboard.WasPressed(KeyboardKey.F4) ? ToolScreen.Looks
            : null;

        if (switchTo == _tool || _keyboard.WasPressed(KeyboardKey.Escape))
        {
            _tool = ToolScreen.None;
            _screen = _toolReturn;
            if (_toolReturn == AppScreen.Fight)
            {
                _keyboard.Clear();
            }

            return;
        }

        if (switchTo is ToolScreen next)
        {
            _tool = next;
            if (next == ToolScreen.Assets)
            {
                _browser.Open(_steam);
            }
        }

        switch (_tool)
        {
            case ToolScreen.Fighter:
                _boxEditor.Update(_keyboard);
                break;
            case ToolScreen.Assets:
                _browser.Update(_keyboard);
                break;
            case ToolScreen.Looks:
                _editor.Update(_keyboard);
                break;
        }
    }

    // =========================================================================================
    //  Draw
    // =========================================================================================

    private void Draw()
    {
        switch (_screen)
        {
            case AppScreen.Title:
                TitleScreen.Draw(VirtualWidth, VirtualHeight, _steam, _autoRoster.Status);
                break;

            case AppScreen.Menu:
                MenuArt.Background(VirtualWidth, VirtualHeight, Raylib.GetTime());
                MenuArt.Heading("LIMBUS FIGHT", VirtualWidth, 60);
                _menu.Draw(VirtualWidth / 2, 170, 460);
                DrawFooter();
                break;

            case AppScreen.Controls:
                ControlsScreen.Draw(VirtualWidth, VirtualHeight, overArena: false);
                break;

            case AppScreen.Select:
                _select.Draw(VirtualWidth, VirtualHeight);
                break;

            case AppScreen.Tool:
                switch (_tool)
                {
                    case ToolScreen.Fighter:
                        _boxEditor.Draw(VirtualWidth, VirtualHeight);
                        break;
                    case ToolScreen.Assets:
                        _browser.Draw(VirtualWidth, VirtualHeight);
                        break;
                    default:
                        _editor.Draw(VirtualWidth, VirtualHeight);
                        break;
                }

                break;

            case AppScreen.Fight:
                DrawFight();
                break;
        }
    }

    private void DrawFooter()
    {
        string? status = _autoRoster.Status ?? _roster.Problem;
        if (status is not null)
        {
            Ui.TextCentered(Ui.FitEnd(status, 18, VirtualWidth - 80), VirtualWidth / 2, VirtualHeight - 60, 18, Theme.Warn);
        }

        Ui.TextCentered($"v{AppInfo.Version}   W/S choose, J / Enter go, Esc back", VirtualWidth / 2, VirtualHeight - 34, 18, Theme.TextDim);
    }

    private void DrawFight()
    {
        if (_steam.ShouldBlockGameplay)
        {
            UpdateRequiredScreen.Draw(_steam, VirtualWidth, VirtualHeight, _nextAutoRescan - Raylib.GetTime());
            return;
        }

        ArenaRenderer.Draw(_arena, _showDebug && _mode == GameMode.Training, VirtualWidth, VirtualHeight, _looks);
        MatchHud.DrawPopups(_arena, VirtualWidth);
        MatchHud.Draw(_arena, VirtualWidth);

        if (_mode == GameMode.Training)
        {
            SteamStatusPanel.DrawCompact(_steam, VirtualWidth);
            if (_showDebug)
            {
                DebugHud.Draw(_arena, _clock, _steam, VirtualWidth);
            }

            DrawLookStatus();
            Ui.TextCentered(
                "A/D move  W jump  J K L skills  S defend  I E.G.O  R restart  F1 boxes  F2 fighter  F3 assets  F4 looks  F6 dummy  Esc pause",
                VirtualWidth / 2, VirtualHeight - 34, 18, Theme.TextDim);
        }
        else
        {
            Ui.TextCentered("Esc pause", VirtualWidth / 2, VirtualHeight - 30, 18, Theme.TextDim);
        }

        if (_resultsOpen)
        {
            int winner = _arena.MatchWinner;
            string name = winner > 0 ? _roster.Get(LookSlot.Player1).Name : winner < 0 ? _roster.Get(LookSlot.Dummy).Name : string.Empty;
            string side = winner > 0 ? _arena.Player1.Name : _arena.Dummy.Name;
            string headline = winner == 0 ? "DRAW" : $"{name.ToUpperInvariant()} WINS";
            _results.Draw(_arena, VirtualWidth, VirtualHeight, headline, winner == 0 ? null : $"{side} takes the match");
        }

        if (_paused)
        {
            _pause.Draw(VirtualWidth, VirtualHeight);
        }

        if (_showControlsOverPause)
        {
            ControlsScreen.Draw(VirtualWidth, VirtualHeight, overArena: true);
        }
    }

    /// <summary>One line above the controls: look loading progress or problems, or how to set looks up.</summary>
    private void DrawLookStatus()
    {
        const int y = 652;
        string dummyMode = _arena.DummyMode switch
        {
            DummyMode.Stand => "Dummy: stands still (F6: fight back)",
            DummyMode.Fight => "Dummy: fights back (F6: defend)",
            _ => "Dummy: defends (F6: stand still)",
        };
        Ui.Text(dummyMode, 20, y, 18, _arena.DummyMode == DummyMode.Stand ? Theme.TextDim : Theme.Warn);
        string? p1 = _looks.Status(LookSlot.Player1);
        string? dummy = _looks.Status(LookSlot.Dummy);

        if (p1 is not null || dummy is not null)
        {
            string text = p1 is not null ? "P1 look: " + p1 : "Dummy look: " + dummy;
            Color color = _looks.IsLoading(LookSlot.Player1) || _looks.IsLoading(LookSlot.Dummy) ? Theme.Warn : Theme.Bad;
            Ui.TextRight(Ui.FitEnd(text, 18, (VirtualWidth / 2) - 40), VirtualWidth - 20, y, 18, color);
        }
        else if (_looks.Get(LookSlot.Player1).IsEmpty && _looks.Get(LookSlot.Dummy).IsEmpty)
        {
            Ui.TextRight("F3, open a Limbus sprite file, Ctrl+A: P1 gets a look", VirtualWidth - 20, y, 18, Theme.Accent);
        }
    }
}
