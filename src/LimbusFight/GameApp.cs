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
    Options,
    Controls,
    Select,
    Intro,
    Fight,
    ArcadeClear,
    Tool,
}

/// <summary>What kind of match the arena is running.</summary>
public enum GameMode
{
    /// <summary>Endless rounds against the dummy (stand, fight, defend or CPU), with the fighter tools.</summary>
    Training,

    /// <summary>Two players on one keyboard.</summary>
    Versus,

    /// <summary>Player 1 against the CPU, one match.</summary>
    VersusCpu,

    /// <summary>Player 1 against a row of CPU opponents.</summary>
    Arcade,

    /// <summary>Player 1 against every Boss in a row, one round each.</summary>
    BossRush,
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

    private static readonly float[] WindowScales = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };

    private readonly TestArena _arena = new();
    private readonly FixedStepClock _clock = new(TargetFps);
    private readonly KeyboardSampler _keyboard = new();
    private readonly RosterManager _roster = new();
    private readonly GameSettings _settings = GameSettings.Load();
    private readonly LookManager _looks;
    private readonly AssetBrowserScreen _browser;
    private readonly LookEditorScreen _editor;
    private readonly BoxEditorScreen _boxEditor;
    private readonly AutoRoster _autoRoster;
    private readonly SelectScreen _select;
    private readonly MenuList _menu = new();
    private readonly MenuList _options = new();
    private readonly PauseMenu _pause = new();
    private readonly ResultsScreen _results = new();

    private SteamCheckResult _steam;
    private AppScreen _screen = AppScreen.Title;
    private AppScreen _controlsReturn = AppScreen.Menu;
    private AppScreen _toolReturn = AppScreen.Fight;
    private ToolScreen _tool = ToolScreen.None;
    private GameMode _mode = GameMode.Training;
    private DummyMode _trainingDummy = DummyMode.Stand;
    private CpuLevel _trainingCpu = CpuLevel.Normal;
    private ArcadeLadder? _ladder;
    private bool _paused;
    private bool _showControlsOverPause;
    private bool _resultsOpen;
    private bool _showDebug;
    private bool _quit;
    private int _introFrames;
    private long _appFrames;
    private uint _matchCount;
    private double _nextAutoRescan;
    private int _lookCount = -1;
    private string? _notice;

    public GameApp(SteamCheckResult steam)
    {
        _steam = steam;
        _looks = new LookManager(steam);
        _browser = new AssetBrowserScreen { Looks = _looks, Roster = _roster };
        _editor = new LookEditorScreen(_looks, _roster.Stats);
        _browser.SelectedClip = () => (_editor.SelectedSlot, _editor.SelectedClip);
        _boxEditor = new BoxEditorScreen(_roster, _looks);
        _keyboard.Pad1Player = _settings.Pad1Player;
        _autoRoster = new AutoRoster(_roster);
        _select = new SelectScreen(_roster, _looks) { Status = () => _autoRoster.Status };

        _roster.Changed += Sync;
        Sync(LookSlot.Player1);
        Sync(LookSlot.Dummy);
        BuildMenu();
        ApplyDisplay();
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
            new MenuList.Item("Arcade", "Beat a row of CPU opponents with one fighter."),
            new MenuList.Item("Versus", "Two players on one keyboard."),
            new MenuList.Item("Versus CPU", "One match against the computer."),
            new MenuList.Item("Boss Rush", "Every Boss in a row, one round each."),
            new MenuList.Item("Training", "Practise against the dummy. Esc for training options."),
            new MenuList.Item("Options", "CPU level, rounds, round time, fullscreen, window size."),
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
            _appFrames++;
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

        if (_keyboard.WasPressed(KeyboardKey.F11)
            || (_keyboard.WasPressed(KeyboardKey.Enter) && (Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt))))
        {
            _settings.Fullscreen = !_settings.Fullscreen;
            ApplyDisplay();
            SaveSettings();
            _keyboard.Clear();
            return;
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

            case AppScreen.Options:
                _keyboard.Clear();
                UpdateOptions();
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

            case AppScreen.Intro:
                _keyboard.Clear();
                _introFrames += ticks;
                if (_introFrames >= VersusIntro.Frames || (_introFrames > 15 && MenuKeys.Confirm(_keyboard)))
                {
                    _screen = AppScreen.Fight;
                    _keyboard.Clear();
                }

                break;

            case AppScreen.ArcadeClear:
                _keyboard.Clear();
                _introFrames += ticks;
                if ((_introFrames > 60 && MenuKeys.Confirm(_keyboard)) || MenuKeys.Back(_keyboard))
                {
                    _screen = AppScreen.Menu;
                }

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
            case "Arcade":
                OpenSelect(GameMode.Arcade);
                break;
            case "Versus":
                OpenSelect(GameMode.Versus);
                break;
            case "Versus CPU":
                OpenSelect(GameMode.VersusCpu);
                break;
            case "Boss Rush":
                OpenSelect(GameMode.BossRush);
                break;
            case "Training":
                OpenSelect(GameMode.Training);
                break;
            case "Options":
                BuildOptions();
                _options.Index = 0;
                _screen = AppScreen.Options;
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

    // ---- Options ----------------------------------------------------------------------------

    private void BuildOptions()
    {
        string rounds = _settings.RoundsToWin switch { 1 => "1 round", 2 => "best of 3", _ => "best of 5" };
        string time = _settings.RoundSeconds == 0 ? "no timer" : $"{_settings.RoundSeconds} seconds";
        _options.Set(new[]
        {
            new MenuList.Item("CPU level", "How hard the computer fights in Arcade and Versus CPU.", Value: GameSettings.Describe(_settings.CpuLevel)),
            new MenuList.Item("Rounds", "Rounds needed to win a match.", Value: rounds),
            new MenuList.Item("Round time", "Length of a round (Training never has a timer).", Value: time),
            new MenuList.Item("Fullscreen", "F11 or Alt+Enter also switches.", Value: _settings.Fullscreen ? "on" : "off"),
            new MenuList.Item("Window size", "Window size when not fullscreen.", Value: $"{_settings.WindowScale * 100:0}%"),
            new MenuList.Item("Controller 1 plays as", $"In Versus; controller 2 plays the other side. Controllers found: {_keyboard.PadCount}.",
                Value: _settings.Pad1Player == 1 ? "Player 1" : "Player 2"),
            new MenuList.Item("Back"),
        });
    }

    private void UpdateOptions()
    {
        int step = MenuKeys.Left(_keyboard) ? -1 : MenuKeys.Right(_keyboard) ? 1 : 0;
        bool confirm = _options.Update(_keyboard);
        if (MenuKeys.Back(_keyboard) || (confirm && _options.Selected.Label == "Back"))
        {
            SaveSettings();
            _screen = AppScreen.Menu;
            return;
        }

        if (confirm)
        {
            step = 1;
        }

        if (step == 0)
        {
            return;
        }

        switch (_options.Selected.Label)
        {
            case "CPU level":
                _settings.CpuLevel = (CpuLevel)Wrap((int)_settings.CpuLevel + step, 3);
                break;
            case "Rounds":
                _settings.RoundsToWin = Wrap(_settings.RoundsToWin - 1 + step, 3) + 1;
                break;
            case "Round time":
                int i = Array.IndexOf(GameSettings.RoundSecondChoices, _settings.RoundSeconds);
                _settings.RoundSeconds = GameSettings.RoundSecondChoices[Wrap(i + step, GameSettings.RoundSecondChoices.Length)];
                break;
            case "Fullscreen":
                _settings.Fullscreen = !_settings.Fullscreen;
                ApplyDisplay();
                break;
            case "Controller 1 plays as":
                _settings.Pad1Player = 3 - _settings.Pad1Player;
                _keyboard.Pad1Player = _settings.Pad1Player;
                break;
            case "Window size":
                int w = Array.IndexOf(WindowScales, _settings.WindowScale);
                _settings.WindowScale = WindowScales[Wrap((w < 0 ? 2 : w) + step, WindowScales.Length)];
                ApplyDisplay();
                break;
        }

        BuildOptions();
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;

    private void SaveSettings() => _notice = _settings.Save();

    /// <summary>Fullscreen (borderless, desktop size) or a window of the chosen size, centred.</summary>
    private void ApplyDisplay()
    {
        try
        {
            bool borderless = Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);
            if (_settings.Fullscreen != borderless)
            {
                Raylib.ToggleBorderlessWindowed();
            }

            if (!_settings.Fullscreen)
            {
                int monitor = Raylib.GetCurrentMonitor();
                int mw = Raylib.GetMonitorWidth(monitor);
                int mh = Raylib.GetMonitorHeight(monitor);
                int w = (int)(VirtualWidth * _settings.WindowScale);
                int h = (int)(VirtualHeight * _settings.WindowScale);
                if (mw > 0 && mh > 0 && (w > mw - 40 || h > mh - 80))
                {
                    float fit = Math.Min((mw - 40f) / VirtualWidth, (mh - 80f) / VirtualHeight);
                    w = (int)(VirtualWidth * fit);
                    h = (int)(VirtualHeight * fit);
                }

                if (Raylib.GetScreenWidth() != w || Raylib.GetScreenHeight() != h)
                {
                    Raylib.SetWindowSize(w, h);
                    if (mw > 0 && mh > 0)
                    {
                        Raylib.SetWindowPosition(Math.Max(0, (mw - w) / 2), Math.Max(30, (mh - h) / 2));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _notice = "Could not change the window: " + ex.Message;
        }
    }

    // ---- Character select and match start ---------------------------------------------------

    private void OpenSelect(GameMode mode)
    {
        _mode = mode;
        _keyboard.SinglePlayer = mode != GameMode.Versus;
        _select.OwnFighterOnly = mode is GameMode.Arcade or GameMode.BossRush;
        switch (mode)
        {
            case GameMode.Versus:
                _select.Open(twoPlayers: true, "VERSUS", "PLAYER 2");
                break;
            case GameMode.VersusCpu:
                _select.Open(twoPlayers: false, "VERSUS CPU", "CPU");
                break;
            case GameMode.Arcade:
                _select.Open(twoPlayers: false, "ARCADE", "CPU");
                break;
            case GameMode.BossRush:
                _select.Open(twoPlayers: false, "BOSS RUSH", "CPU");
                break;
            default:
                _select.Open(twoPlayers: false, "TRAINING", "DUMMY");
                break;
        }

        _paused = false;
        _resultsOpen = false;
        _screen = AppScreen.Select;
    }

    private void UpdateSelect()
    {
        switch (_select.Update(_keyboard))
        {
            case SelectScreen.Result.Back:
                _screen = AppScreen.Menu;
                break;
            case SelectScreen.Result.Start when IsLadder:
                string me = _roster.IdOf(LookSlot.Player1);
                _ladder = _mode == GameMode.BossRush
                    ? ArcadeLadder.BuildBossRush(me, _roster.ListForSelect(), _settings.CpuLevel)
                    : ArcadeLadder.Build(me, _roster.ListForSelect(), NextSeed(), _settings.CpuLevel);
                if (_ladder.IsEmpty)
                {
                    _notice = "There are no Bosses to fight (they may have been deleted from the fighters folder).";
                    _screen = AppScreen.Menu;
                    break;
                }

                BeginArcadeStage();
                break;
            case SelectScreen.Result.Start:
                StartMatch(intro: _mode != GameMode.Training);
                break;
        }
    }

    private bool IsLadder => _mode is GameMode.Arcade or GameMode.BossRush;

    private uint NextSeed() => unchecked((uint)Environment.TickCount * 2654435761u) ^ ++_matchCount;

    private void BeginArcadeStage()
    {
        if (_ladder is null || _ladder.IsEmpty)
        {
            _screen = AppScreen.Menu;
            return;
        }

        if (_roster.IdOf(LookSlot.Dummy) != _ladder.Current)
        {
            _roster.Use(LookSlot.Dummy, _ladder.Current);
        }

        StartMatch(intro: true);
    }

    private void StartMatch(bool intro)
    {
        _keyboard.SinglePlayer = _mode != GameMode.Versus;
        _arena.Player1.Name = "P1";
        _arena.Seed = NextSeed();
        switch (_mode)
        {
            case GameMode.Versus:
                _arena.Dummy.Name = "P2";
                _arena.DummyMode = DummyMode.Stand;
                _arena.RoundsToWin = _settings.RoundsToWin;
                _arena.RoundLengthFrames = _settings.RoundFrames;
                break;
            case GameMode.VersusCpu:
            case GameMode.Arcade:
            case GameMode.BossRush:
                _arena.Dummy.Name = _arena.Dummy.Stats.IsBoss ? "BOSS" : "CPU";
                _arena.DummyMode = DummyMode.Cpu;
                _arena.Cpu.Level = IsLadder && _ladder is not null ? _ladder.Level : _settings.CpuLevel;
                _arena.RoundsToWin = _mode == GameMode.BossRush ? 1 : _settings.RoundsToWin;
                _arena.RoundLengthFrames = _settings.RoundFrames;
                break;
            default:
                _arena.Dummy.Name = "DUMMY";
                _arena.DummyMode = _trainingDummy;
                _arena.Cpu.Level = _trainingCpu;
                _arena.RoundsToWin = 0;
                _arena.RoundLengthFrames = 0;
                break;
        }

        bool training = _mode == GameMode.Training;
        if (!training)
        {
            _arena.InfiniteHealth = false;
            _arena.FreeEgo = false;
        }

        _arena.NewMatch();
        _paused = false;
        _resultsOpen = false;
        _showControlsOverPause = false;
        _introFrames = 0;
        _screen = intro ? AppScreen.Intro : AppScreen.Fight;
        _keyboard.Clear();
    }

    // ---- Fight ------------------------------------------------------------------------------

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
            UpdatePause();
            return;
        }

        if (_resultsOpen)
        {
            _keyboard.Clear();
            UpdateResults();

            // The arena keeps ticking behind the card so the fighters stay animated.
            for (int i = 0; i < ticks && _screen == AppScreen.Fight && _resultsOpen; i++)
            {
                _arena.Tick(FrameInput.None, FrameInput.None);
            }

            return;
        }

        if (MenuKeys.Pause(_keyboard))
        {
            BuildPause();
            _pause.List.Index = 0;
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
                SetTrainingDummy((DummyMode)Wrap((int)_trainingDummy + 1, 4));
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
            OpenResults();
        }
    }

    private void SetTrainingDummy(DummyMode mode)
    {
        _trainingDummy = mode;
        _arena.DummyMode = mode;
    }

    private void BuildPause()
    {
        var items = new List<MenuList.Item> { new("Resume") };
        if (_mode == GameMode.Training)
        {
            string dummy = _trainingDummy switch
            {
                DummyMode.Stand => "stands still",
                DummyMode.Fight => "fights back",
                DummyMode.Defend => "defends",
                _ => "CPU",
            };
            items.Add(new MenuList.Item("Dummy", "Left / Right to change. F6 does the same in the fight.", Value: dummy));
            items.Add(new MenuList.Item("CPU level", "How the dummy fights when set to CPU.", Value: GameSettings.Describe(_trainingCpu)));
            items.Add(new MenuList.Item("Infinite health", "Health and Stagger refill after a second; no K.O.", Value: _arena.InfiniteHealth ? "on" : "off"));
            items.Add(new MenuList.Item("Free E.G.O", "Both fighters always have the Sins for E.G.O.", Value: _arena.FreeEgo ? "on" : "off"));
            items.Add(new MenuList.Item("Hitboxes", "Same as F1.", Value: _showDebug ? "on" : "off"));
            items.Add(new MenuList.Item("Restart round"));
        }
        else
        {
            items.Add(new MenuList.Item("Restart match"));
        }

        items.Add(new MenuList.Item(IsLadder ? "Give up" : "Character select"));
        items.Add(new MenuList.Item("Controls"));
        items.Add(new MenuList.Item("Main menu"));
        items.Add(new MenuList.Item("Quit game"));
        _pause.List.Set(items);
    }

    private void UpdatePause()
    {
        int step = _pause.Adjust(_keyboard);
        string? choice = _pause.Update(_keyboard);
        if (step == 0 && choice is not null && _pause.List.Selected.Value is not null && choice != "Resume")
        {
            step = 1; // Enter on an option changes it too
        }

        if (step != 0)
        {
            switch (_pause.List.Selected.Label)
            {
                case "Dummy":
                    SetTrainingDummy((DummyMode)Wrap((int)_trainingDummy + step, 4));
                    break;
                case "CPU level":
                    _trainingCpu = (CpuLevel)Wrap((int)_trainingCpu + step, 3);
                    _arena.Cpu.Level = _trainingCpu;
                    break;
                case "Infinite health":
                    _arena.InfiniteHealth = !_arena.InfiniteHealth;
                    break;
                case "Free E.G.O":
                    _arena.FreeEgo = !_arena.FreeEgo;
                    break;
                case "Hitboxes":
                    _showDebug = !_showDebug;
                    break;
            }

            BuildPause();
            return;
        }

        switch (choice)
        {
            case "Resume":
                _paused = false;
                break;
            case "Restart round":
                _paused = false;
                _arena.ResetPositions();
                break;
            case "Restart match":
                _paused = false;
                _ladder?.Retry();
                StartMatch(intro: false);
                break;
            case "Character select":
                OpenSelect(_mode);
                break;
            case "Give up":
                _ladder = null;
                _screen = AppScreen.Menu;
                break;
            case "Controls":
                _showControlsOverPause = true;
                break;
            case "Main menu":
                _screen = AppScreen.Menu;
                break;
            case "Quit game":
                _quit = true;
                break;
        }
    }

    private void OpenResults()
    {
        _resultsOpen = true;
        if (IsLadder && _ladder is not null)
        {
            if (_arena.MatchWinner > 0)
            {
                if (_ladder.IsLast)
                {
                    _resultsOpen = false;
                    _screen = AppScreen.ArcadeClear;
                    _introFrames = 0;
                    return;
                }

                _results.Open("Next fight", "Main menu");
            }
            else
            {
                _results.Open("Continue", "Give up");
            }

            return;
        }

        _results.Open("Rematch", "Character select", "Main menu");
    }

    private void UpdateResults()
    {
        switch (_results.Update(_keyboard))
        {
            case "Next fight":
                _ladder?.Advance();
                BeginArcadeStage();
                break;
            case "Continue":
                _ladder?.Retry();
                StartMatch(intro: false);
                break;
            case "Rematch":
                StartMatch(intro: false);
                break;
            case "Character select":
                OpenSelect(_mode);
                break;
            case "Give up":
            case "Main menu":
                _ladder = null;
                _screen = AppScreen.Menu;
                break;
        }
    }

    // ---- Tools ------------------------------------------------------------------------------

    private void OpenTool(ToolScreen tool, AppScreen returnTo)
    {
        if (returnTo == AppScreen.Menu)
        {
            // Tools from the menu edit the training slots.
            _mode = GameMode.Training;
            _arena.Player1.Name = "P1";
            _arena.Dummy.Name = "DUMMY";
            _arena.DummyMode = _trainingDummy;
            _arena.RoundsToWin = 0;
            _arena.RoundLengthFrames = 0;
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
            _keyboard.Clear();
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
                MenuArt.Heading("LIMBUS FIGHT", VirtualWidth, 40);
                _menu.Draw(VirtualWidth / 2, 130, 460, 42, 26);
                DrawFooter("W/S choose, J / Enter go, Esc back");
                break;

            case AppScreen.Options:
                MenuArt.Background(VirtualWidth, VirtualHeight, Raylib.GetTime());
                MenuArt.Heading("OPTIONS", VirtualWidth, 60);
                _options.Draw(VirtualWidth / 2, 190, 640);
                DrawFooter("W/S choose, A/D or Left/Right change, Esc saves and goes back");
                break;

            case AppScreen.Controls:
                ControlsScreen.Draw(VirtualWidth, VirtualHeight, overArena: false);
                break;

            case AppScreen.Select:
                _select.Draw(VirtualWidth, VirtualHeight);
                break;

            case AppScreen.Intro:
                VersusIntro.Draw(VirtualWidth, VirtualHeight, _introFrames, _roster.Get(LookSlot.Player1), _roster.Get(LookSlot.Dummy),
                    IsLadder ? _ladder?.StageLabel : _mode == GameMode.VersusCpu ? $"CPU: {GameSettings.Describe(_arena.Cpu.Level)}" : null,
                    IsLadder ? $"CPU {GameSettings.Describe(_arena.Cpu.Level)}" : null);
                break;

            case AppScreen.ArcadeClear:
                DrawArcadeClear();
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

    private void DrawFooter(string help)
    {
        string? status = _notice ?? _autoRoster.Status ?? _roster.Problem;
        if (status is not null)
        {
            Ui.TextCentered(Ui.FitEnd(status, 18, VirtualWidth - 80), VirtualWidth / 2, VirtualHeight - 60, 18, Theme.Warn);
        }

        Ui.TextCentered($"v{AppInfo.Version}   {help}", VirtualWidth / 2, VirtualHeight - 34, 18, Theme.TextDim);
    }

    private void DrawArcadeClear()
    {
        MenuArt.Background(VirtualWidth, VirtualHeight, Raylib.GetTime());
        FighterData p1 = _roster.Get(LookSlot.Player1);
        string title = _ladder?.IsBossRush == true ? "BOSS RUSH CLEAR" : "ARCADE CLEAR";
        Ui.TextCentered(title, (VirtualWidth / 2) + 5, 165, 88, new Color((byte)60, (byte)8, (byte)12, (byte)255));
        Ui.TextCentered(title, VirtualWidth / 2, 160, 88, Theme.Accent);
        Ui.TextCentered(p1.Name.ToUpperInvariant(), VirtualWidth / 2, 290, 48, Theme.Text);
        if (_ladder is not null)
        {
            Ui.TextCentered($"{_ladder.Opponents.Count} opponents   |   CPU {GameSettings.Describe(_ladder.BaseLevel)}   |   continues used: {_ladder.Continues}",
                VirtualWidth / 2, 370, 24, Theme.TextDim);
        }

        Ui.TextCentered("The Bus moves on. Press J / Enter for the menu.", VirtualWidth / 2, VirtualHeight - 120, 22, Theme.Text);
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
                "A/D move  W jump  J K L skills  S defend  I E.G.O  R restart  F1 boxes  F2 fighter  F3 assets  F4 looks  F6 dummy  Esc options",
                VirtualWidth / 2, VirtualHeight - 34, 18, Theme.TextDim);
        }
        else
        {
            string left = IsLadder && _ladder is not null ? $"{_ladder.StageLabel}   " : string.Empty;
            string cpu = _arena.DummyMode == DummyMode.Cpu ? $"CPU {GameSettings.Describe(_arena.Cpu.Level)}   " : string.Empty;
            Ui.TextCentered($"{left}{cpu}Esc pause", VirtualWidth / 2, VirtualHeight - 30, 18, Theme.TextDim);
        }

        if (_resultsOpen)
        {
            int winner = _arena.MatchWinner;
            string name = winner > 0 ? _roster.Get(LookSlot.Player1).Name : winner < 0 ? _roster.Get(LookSlot.Dummy).Name : string.Empty;
            string side = winner > 0 ? _arena.Player1.Name : _arena.Dummy.Name;
            string headline = winner == 0 ? "DRAW" : $"{name.ToUpperInvariant()} WINS";
            string? sub = winner == 0 ? null : $"{side} takes the match";
            if (IsLadder && _ladder is not null)
            {
                sub = winner > 0 ? $"{_ladder.StageLabel} cleared" : "Try again?";
            }

            _results.Draw(_arena, VirtualWidth, VirtualHeight, headline, sub);
        }

        if (_paused)
        {
            _pause.Draw(VirtualWidth, VirtualHeight, _mode == GameMode.Training ? "TRAINING" : "PAUSED");
        }

        if (_showControlsOverPause)
        {
            ControlsScreen.Draw(VirtualWidth, VirtualHeight, overArena: true);
        }
    }

    /// <summary>One line above the controls: dummy mode, look loading progress or problems, or how to set looks up.</summary>
    private void DrawLookStatus()
    {
        const int y = 652;
        string dummyMode = _arena.DummyMode switch
        {
            DummyMode.Stand => "Dummy: stands still (F6: fight back)",
            DummyMode.Fight => "Dummy: fights back (F6: defend)",
            DummyMode.Defend => "Dummy: defends (F6: CPU)",
            _ => $"Dummy: CPU {GameSettings.Describe(_arena.Cpu.Level)} (F6: stand still)",
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
