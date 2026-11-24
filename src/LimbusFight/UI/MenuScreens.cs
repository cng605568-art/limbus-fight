using LimbusFight.Animation;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>Menu keys for both players: P1 W/S/A/D + J or Enter, K back; P2 arrows + numpad 1 or comma, numpad 2 or period back; Esc back.</summary>
public static class MenuKeys
{
    public static bool Up(KeyboardSampler k, int player = 0) =>
        (player != 2 && k.WasPressed(KeyboardKey.W)) || (player != 1 && k.WasPressed(KeyboardKey.Up)) || k.PadPressed(GamepadButton.LeftFaceUp, player);

    public static bool Down(KeyboardSampler k, int player = 0) =>
        (player != 2 && k.WasPressed(KeyboardKey.S)) || (player != 1 && k.WasPressed(KeyboardKey.Down)) || k.PadPressed(GamepadButton.LeftFaceDown, player);

    public static bool Left(KeyboardSampler k, int player = 0) =>
        (player != 2 && k.WasPressed(KeyboardKey.A)) || (player != 1 && k.WasPressed(KeyboardKey.Left)) || k.PadPressed(GamepadButton.LeftFaceLeft, player);

    public static bool Right(KeyboardSampler k, int player = 0) =>
        (player != 2 && k.WasPressed(KeyboardKey.D)) || (player != 1 && k.WasPressed(KeyboardKey.Right)) || k.PadPressed(GamepadButton.LeftFaceRight, player);

    public static bool Confirm(KeyboardSampler k, int player = 0) =>
        (player != 2 && (k.WasPressed(KeyboardKey.J) || k.WasPressed(KeyboardKey.Enter) || k.WasPressed(KeyboardKey.Space)))
        || (player != 1 && (k.WasPressed(KeyboardKey.Kp1) || k.WasPressed(KeyboardKey.Comma) || k.WasPressed(KeyboardKey.KpEnter)))
        || k.PadPressed(GamepadButton.RightFaceDown, player) || k.PadPressed(GamepadButton.MiddleRight, player);

    public static bool Back(KeyboardSampler k, int player = 0) =>
        (player != 2 && (k.WasPressed(KeyboardKey.K) || k.WasPressed(KeyboardKey.Backspace)))
        || (player != 1 && (k.WasPressed(KeyboardKey.Kp2) || k.WasPressed(KeyboardKey.Period)))
        || (player == 0 && k.WasPressed(KeyboardKey.Escape))
        || (player == 1 && k.WasPressed(KeyboardKey.Escape))
        || k.PadPressed(GamepadButton.RightFaceRight, player);

    /// <summary>Esc, P, or Start on any controller.</summary>
    public static bool Pause(KeyboardSampler k) =>
        k.WasPressed(KeyboardKey.Escape) || k.WasPressed(KeyboardKey.P) || k.PadPressed(GamepadButton.MiddleRight);
}

/// <summary>A vertical list of choices with a highlighted row.</summary>
public sealed class MenuList
{
    public sealed record Item(string Label, string Hint = "", bool Enabled = true, string? Value = null);

    public List<Item> Items { get; } = new();

    public int Index { get; set; }

    public Item Selected => Items[Math.Clamp(Index, 0, Items.Count - 1)];

    public void Set(IEnumerable<Item> items)
    {
        string? keep = Items.Count > 0 ? Selected.Label : null;
        Items.Clear();
        Items.AddRange(items);
        int found = keep is null ? -1 : Items.FindIndex(i => i.Label == keep);
        Index = found >= 0 ? found : Math.Clamp(Index, 0, Math.Max(0, Items.Count - 1));
    }

    /// <summary>Moves the highlight; returns true when the highlighted item is chosen.</summary>
    public bool Update(KeyboardSampler k)
    {
        if (Items.Count == 0)
        {
            return false;
        }

        if (MenuKeys.Up(k))
        {
            Step(-1);
        }

        if (MenuKeys.Down(k))
        {
            Step(1);
        }

        return MenuKeys.Confirm(k) && Selected.Enabled;
    }

    private void Step(int direction)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Index = ((Index + direction) % Items.Count + Items.Count) % Items.Count;
            if (Items[Index].Enabled)
            {
                return;
            }
        }
    }

    public void Draw(int centerX, int y, int width, int rowHeight = 46, int size = 28)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Item item = Items[i];
            int rowY = y + (i * rowHeight);
            bool on = i == Index;
            if (on)
            {
                Raylib.DrawRectangle(centerX - (width / 2), rowY - 6, width, rowHeight - 6, new Color((byte)70, (byte)24, (byte)28, (byte)230));
                Raylib.DrawRectangle(centerX - (width / 2), rowY - 6, 6, rowHeight - 6, Theme.Accent);
            }

            string label = item.Value is null ? item.Label : $"{item.Label}:  {item.Value}";
            Color color = !item.Enabled ? new Color((byte)80, (byte)80, (byte)92, (byte)255) : on ? Theme.Accent : Theme.Text;
            Ui.TextCentered(label, centerX, rowY, size, color);
        }

        if (Items.Count > 0 && Selected.Hint.Length > 0)
        {
            Ui.TextCentered(Selected.Hint, centerX, y + (Items.Count * rowHeight) + 10, 20, Theme.TextDim);
        }
    }
}

/// <summary>Background and headings shared by the menu screens.</summary>
public static class MenuArt
{
    public static readonly Color P1Color = new((byte)238, (byte)72, (byte)72, (byte)255);
    public static readonly Color P2Color = new((byte)80, (byte)150, (byte)255, (byte)255);

    public static void Background(int width, int height, double time)
    {
        Raylib.DrawRectangleGradientV(0, 0, width, height, new Color((byte)16, (byte)10, (byte)14, (byte)255), new Color((byte)6, (byte)6, (byte)10, (byte)255));

        // Slow diagonal bars, the red-and-gold of the Bus.
        for (int i = 0; i < 9; i++)
        {
            float x = (float)((((time * 18) + (i * 190)) % (width + 400)) - 300);
            Raylib.DrawRectanglePro(new Rectangle(x, -80, 26, height + 200), System.Numerics.Vector2.Zero, 18f,
                new Color((byte)120, (byte)24, (byte)30, (byte)(i % 3 == 0 ? 34 : 18)));
        }

        Raylib.DrawRectangle(0, height - 6, width, 6, new Color((byte)120, (byte)24, (byte)30, (byte)255));
    }

    public static void Heading(string text, int width, int y = 34)
    {
        Ui.TextCentered(text, width / 2, y, 40, Theme.Accent);
        Raylib.DrawRectangle((width / 2) - 160, y + 50, 320, 3, new Color((byte)120, (byte)24, (byte)30, (byte)255));
    }

    /// <summary>Dark veil over the arena for pause and results.</summary>
    public static void Veil(int width, int height, byte alpha = 170) =>
        Raylib.DrawRectangle(0, 0, width, height, new Color((byte)0, (byte)0, (byte)0, alpha));
}

/// <summary>The first screen: title, "press a key", fan-game notice.</summary>
public static class TitleScreen
{
    public static bool Update(KeyboardSampler k) => MenuKeys.Confirm(k);

    public static void Draw(int width, int height, SteamCheckResult steam, string? rosterStatus)
    {
        double t = Raylib.GetTime();
        MenuArt.Background(width, height, t);

        const string title = "LIMBUS FIGHT";
        int size = 104;
        int y = 170;
        Ui.TextCentered(title, (width / 2) + 5, y + 5, size, new Color((byte)60, (byte)8, (byte)12, (byte)255));
        Ui.TextCentered(title, width / 2, y, size, Theme.Accent);
        Raylib.DrawRectangle((width / 2) - 330, y + 118, 660, 4, new Color((byte)170, (byte)34, (byte)40, (byte)255));
        Ui.TextCentered("a Limbus Company fan fighting game", width / 2, y + 136, 26, Theme.Text);

        if ((int)(t * 2) % 2 == 0)
        {
            Ui.TextCentered("PRESS ENTER OR J", width / 2, 450, 30, Theme.Text);
        }

        Ui.TextCentered($"v{AppInfo.Version}   |   Steam: {steam.Status}", width / 2, 540, 18, Theme.TextDim);
        if (rosterStatus is not null)
        {
            Ui.TextCentered(Ui.FitEnd(rosterStatus, 18, width - 80), width / 2, 566, 18, Theme.Warn);
        }

        Ui.TextCentered("Unofficial fan game. Not made by or affiliated with Project Moon.", width / 2, height - 64, 18, Theme.TextDim);
        Ui.TextCentered("Pictures come from your own Limbus Company install and never leave your PC.", width / 2, height - 40, 18, Theme.TextDim);
    }
}

/// <summary>The controls card (menu and pause).</summary>
public static class ControlsScreen
{
    public static void Draw(int width, int height, bool overArena)
    {
        if (overArena)
        {
            MenuArt.Veil(width, height, 215);
        }
        else
        {
            MenuArt.Background(width, height, Raylib.GetTime());
        }

        MenuArt.Heading("CONTROLS", width);
        int left = (width / 2) - 470;
        int right = (width / 2) + 40;
        int y = 130;
        Ui.Text("PLAYER 1", left, y, 28, MenuArt.P1Color);
        Ui.Text("PLAYER 2", right, y, 28, MenuArt.P2Color);
        (string What, string P1, string P2)[] rows =
        {
            ("Move", "A / D", "Left / Right"),
            ("Jump", "W", "Up"),
            ("Skill 1", "J", "Numpad 1  or  ,"),
            ("Skill 2", "K", "Numpad 2  or  ."),
            ("Skill 3", "L", "Numpad 3  or  /"),
            ("Defend (guard / evade / counter)", "S", "Numpad 0  or  Right Shift"),
            ("E.G.O (costs Sins)", "I", "Numpad Enter  or  '"),
        };

        for (int i = 0; i < rows.Length; i++)
        {
            int rowY = y + 50 + (i * 40);
            Ui.Text(rows[i].What, left, rowY, 20, Theme.TextDim);
            Ui.Text(rows[i].P1, left + 390, rowY, 22, Theme.Text);
            Ui.Text(rows[i].P2, right + 120, rowY, 22, Theme.Text);
        }

        int padY = y + 50 + (rows.Length * 40) + 8;
        Ui.TextCentered("Controller: d-pad / left stick move, A Skill 1, X Skill 2, Y Skill 3, B or LB defend, RB / RT E.G.O, Start pause",
            width / 2, padY, 20, Theme.Text);
        int notesY = padY + 40;
        Ui.TextCentered("Esc: pause   |   Menus: W/S or arrows to move, J / Enter to choose, K / Esc to go back", width / 2, notesY, 20, Theme.TextDim);
        Ui.TextCentered("Training only: R restart, F1 boxes, F2 fighter editor, F3 asset browser, F4 look editor, F6 dummy", width / 2, notesY + 28, 20, Theme.TextDim);
        Ui.TextCentered("Clash: attack into an attack. Hits fill Sanity and Sins; full Sins pay for E.G.O.", width / 2, notesY + 56, 20, Theme.TextDim);
        Ui.TextCentered("Press K or Esc to go back", width / 2, height - 50, 22, Theme.Accent);
    }
}

/// <summary>
/// Character select: a grid of every fighter file, built-in Sinners first. In versus both players
/// pick at once (P1 red cursor, P2 blue); in training and against the CPU, Player 1 picks both.
/// Hovering a fighter for a moment puts it in the slot so its look starts loading for the preview.
/// </summary>
public sealed class SelectScreen
{
    private const int HoverDelayFrames = 10;
    private const int StartDelayFrames = 45;

    private readonly RosterManager _roster;
    private readonly LookManager _looks;
    private readonly int[] _cursor = new int[2];
    private readonly bool[] _locked = new bool[2];
    private readonly int[] _hoverFrames = new int[2];
    private List<(string Id, FighterData Data)> _all = new();
    private bool _twoPlayers;
    private int _stage; // one-player picking: 0 = own fighter, 1 = opponent
    private int _readyFrames;
    private string _title = "CHOOSE YOUR SINNER";
    private string _opponentLabel = "OPPONENT";

    public SelectScreen(RosterManager roster, LookManager looks)
    {
        _roster = roster;
        _looks = looks;
    }

    public enum Result
    {
        None,
        Back,
        Start,
    }

    /// <summary>Shown under the grid (auto roster progress).</summary>
    public Func<string?>? Status { get; set; }

    /// <summary>When true, the opponent is not picked here (arcade picks it).</summary>
    public bool OwnFighterOnly { get; set; }

    public void Open(bool twoPlayers, string title, string opponentLabel)
    {
        _all = _roster.ListForSelect();
        _twoPlayers = twoPlayers;
        _title = title;
        _opponentLabel = opponentLabel;
        _cursor[0] = Math.Max(0, _all.FindIndex(t => t.Id == _roster.IdOf(LookSlot.Player1)));
        _cursor[1] = Math.Max(0, _all.FindIndex(t => t.Id == _roster.IdOf(LookSlot.Dummy)));
        if (twoPlayers && _all.Count > 1)
        {
            // Versus starts on two different Sinners, not on the training dummy.
            if (_all[_cursor[0]].Data.Sinner is null)
            {
                _cursor[0] = 0;
            }

            if (_all[_cursor[1]].Data.Sinner is null || _cursor[1] == _cursor[0])
            {
                _cursor[1] = _cursor[0] == 0 ? 1 : 0;
            }
        }
        _locked[0] = _locked[1] = false;
        _hoverFrames[0] = _hoverFrames[1] = 0;
        _stage = 0;
        _readyFrames = 0;
    }

    /// <summary>Re-reads the fighter list (a fighter got a look or was added) without moving the cursors.</summary>
    public void Refresh()
    {
        string a = Id(0);
        string b = Id(1);
        _all = _roster.ListForSelect();
        _cursor[0] = Math.Max(0, _all.FindIndex(t => t.Id == a));
        _cursor[1] = Math.Max(0, _all.FindIndex(t => t.Id == b));
    }

    private string Id(int side) => _all.Count == 0 ? string.Empty : _all[Math.Clamp(_cursor[side], 0, _all.Count - 1)].Id;

    private int Columns => _all.Count <= 18 ? 6 : 8;

    public Result Update(KeyboardSampler k)
    {
        if (_all.Count == 0)
        {
            return MenuKeys.Back(k) ? Result.Back : Result.None;
        }

        if (_twoPlayers)
        {
            for (int side = 0; side < 2; side++)
            {
                int player = side + 1;
                if (!_locked[side])
                {
                    Move(side, k, player);
                    if (MenuKeys.Confirm(k, player))
                    {
                        Hover(side, force: true);
                        _locked[side] = true;
                    }
                    else if (MenuKeys.Back(k, player) && !_locked[0] && !_locked[1])
                    {
                        return Result.Back;
                    }
                }
                else if (MenuKeys.Back(k, player))
                {
                    _locked[side] = false;
                    _readyFrames = 0;
                }
            }
        }
        else
        {
            int side = _stage;
            if (!_locked[side])
            {
                Move(side, k, 0);
                if (MenuKeys.Confirm(k))
                {
                    Hover(side, force: true);
                    _locked[side] = true;
                    if (side == 0 && !OwnFighterOnly)
                    {
                        _stage = 1;
                    }
                }
                else if (MenuKeys.Back(k))
                {
                    if (side == 0)
                    {
                        return Result.Back;
                    }

                    _stage = 0;
                    _locked[0] = false;
                }
            }
            else if (MenuKeys.Back(k))
            {
                _locked[side] = false;
                _readyFrames = 0;
            }
        }

        for (int side = 0; side < 2; side++)
        {
            Hover(side, force: false);
        }

        bool ready = _locked[0] && (_locked[1] || OwnFighterOnly);
        _readyFrames = ready ? _readyFrames + 1 : 0;
        return ready && (_readyFrames >= StartDelayFrames || (_readyFrames > 8 && MenuKeys.Confirm(k))) ? Result.Start : Result.None;
    }

    private void Move(int side, KeyboardSampler k, int player)
    {
        int cols = Columns;
        int count = _all.Count;
        int c = _cursor[side];
        if (MenuKeys.Left(k, player))
        {
            c = (c - 1 + count) % count;
        }

        if (MenuKeys.Right(k, player))
        {
            c = (c + 1) % count;
        }

        if (MenuKeys.Up(k, player))
        {
            c = c - cols >= 0 ? c - cols : c;
        }

        if (MenuKeys.Down(k, player))
        {
            c = c + cols < count ? c + cols : c;
        }

        if (c != _cursor[side])
        {
            _cursor[side] = c;
            _hoverFrames[side] = 0;
        }
    }

    /// <summary>After the cursor rests on a fighter, it goes into the slot (so the preview shows its look).</summary>
    private void Hover(int side, bool force)
    {
        if (OwnFighterOnly && side == 1)
        {
            return;
        }

        _hoverFrames[side]++;
        LookSlot slot = side == 0 ? LookSlot.Player1 : LookSlot.Dummy;
        string id = Id(side);
        if ((force || _hoverFrames[side] == HoverDelayFrames) && _roster.IdOf(slot) != id)
        {
            _roster.Use(slot, id);
        }
    }

    public void Draw(int width, int height)
    {
        MenuArt.Background(width, height, Raylib.GetTime());
        MenuArt.Heading(_title, width, 20);

        int cols = Columns;
        int cardW = cols == 6 ? 184 : 138;
        const int cardH = 62;
        const int gap = 8;
        int gridW = (cols * cardW) + ((cols - 1) * gap);
        int x0 = (width - gridW) / 2;
        const int y0 = 92;
        int maxRows = 5;
        int rows = (_all.Count + cols - 1) / cols;
        int firstRow = Math.Clamp((_cursor[0] / cols) - (maxRows - 1), 0, Math.Max(0, rows - maxRows));

        for (int i = firstRow * cols; i < _all.Count && i < (firstRow + maxRows) * cols; i++)
        {
            int col = i % cols;
            int row = (i / cols) - firstRow;
            int x = x0 + (col * (cardW + gap));
            int y = y0 + (row * (cardH + gap));
            DrawCard(_all[i].Data, x, y, cardW, cardH, i == _cursor[0], i == _cursor[1] && (_twoPlayers || _stage == 1 || _locked[1]));
        }

        int previewTop = y0 + (Math.Min(rows, maxRows) * (cardH + gap)) + 6;
        DrawSide(0, LookSlot.Player1, 40, previewTop, width, height);
        if (!OwnFighterOnly)
        {
            DrawSide(1, LookSlot.Dummy, width - 40, previewTop, width, height);
        }

        string help = _twoPlayers
            ? "P1: W A S D + J pick, K undo      P2: arrows + Numpad 1 / , pick, Numpad 2 / . undo      Esc back"
            : _stage == 0 ? "W A S D or arrows move, J / Enter pick, K / Esc back" : $"Now pick the {_opponentLabel.ToLowerInvariant()}.  K / Esc goes back";
        Ui.TextCentered(help, width / 2, height - 30, 18, Theme.TextDim);

        if (Status?.Invoke() is string status)
        {
            Ui.TextCentered(Ui.FitEnd(status, 18, width - 100), width / 2, height - 54, 18, Theme.Warn);
        }

        if (_locked[0] && (_locked[1] || OwnFighterOnly))
        {
            Ui.TextCentered("READY!", width / 2, height - 150, 48, Theme.Accent);
        }
    }

    private static void DrawCard(FighterData data, int x, int y, int w, int h, bool p1, bool p2)
    {
        Raylib.DrawRectangle(x, y, w, h, Theme.Panel);
        uint rgb = FighterData.ParseRgb(data.Color);
        Color strip = rgb == 0 ? Theme.PanelBorder : new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
        Raylib.DrawRectangle(x, y, 8, h, strip);
        Ui.Text(Ui.FitEnd(data.Name, 20, w - 20), x + 14, y + 10, 20, Theme.Text);
        string sub = data.Identity ?? (data.Sinner is null ? "custom fighter" : string.Empty);
        Ui.Text(Ui.FitEnd(sub, 14, w - 20), x + 14, y + 36, 14, data.IsBoss ? new Color((byte)230, (byte)90, (byte)80, (byte)255) : Theme.TextDim);
        if (!data.Look.IsEmpty)
        {
            Raylib.DrawCircle(x + w - 12, y + 12, 4, Theme.Good);
        }

        if (p1 && p2)
        {
            Raylib.DrawRectangleLinesEx(new Rectangle(x - 3, y - 3, w + 6, h + 6), 3, MenuArt.P1Color);
            Raylib.DrawRectangleLinesEx(new Rectangle(x + 1, y + 1, w - 2, h - 2), 3, MenuArt.P2Color);
        }
        else if (p1 || p2)
        {
            Raylib.DrawRectangleLinesEx(new Rectangle(x - 3, y - 3, w + 6, h + 6), 3, p1 ? MenuArt.P1Color : MenuArt.P2Color);
        }

        if (p1)
        {
            Ui.Text("1P", x + w - 30, y + h - 20, 16, MenuArt.P1Color);
        }

        if (p2)
        {
            Ui.Text("2P", x + w - (p1 ? 56 : 30), y + h - 20, 16, MenuArt.P2Color);
        }
    }

    /// <summary>One player's big preview: the fighter standing, its name, numbers and moves.</summary>
    private void DrawSide(int side, LookSlot slot, int edgeX, int top, int width, int height)
    {
        bool left = side == 0;
        FighterData data = _roster.Get(slot);
        bool showing = _all.Count > 0 && _all[Math.Clamp(_cursor[side], 0, _all.Count - 1)].Id == _roster.IdOf(slot);
        FighterData shown = showing ? data : _all[Math.Clamp(_cursor[side], 0, _all.Count - 1)].Data;
        Color tag = left ? MenuArt.P1Color : MenuArt.P2Color;
        string who = left ? "PLAYER 1" : _twoPlayers ? "PLAYER 2" : _opponentLabel;
        bool active = _twoPlayers || (_stage == side) || _locked[side];

        int floorY = height - 74;
        int figureX = left ? edgeX + 150 : edgeX - 150;
        Raylib.DrawEllipse(figureX, floorY, 70, 12, Theme.Shadow);
        bool drawn = showing && DrawIdle(slot, figureX, floorY, left ? 1 : -1);
        if (!drawn)
        {
            uint rgb = FighterData.ParseRgb(shown.Color);
            Color body = rgb == 0 ? (left ? Theme.Player1Body : Theme.DummyBody) : new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
            int bw = (int)(shown.Body.HurtboxWidth * 0.9f);
            int bh = (int)(shown.Body.HurtboxHeight * 0.9f);
            Raylib.DrawRectangle(figureX - (bw / 2), floorY - bh, bw, bh, active ? body : Raylib.Fade(body, 0.4f));
            Raylib.DrawRectangleLinesEx(new Rectangle(figureX - (bw / 2), floorY - bh, bw, bh), 2, tag);
            if (showing && _looks.IsLoading(slot))
            {
                Ui.TextCentered("loading look...", figureX, floorY + 18, 16, Theme.Warn);
            }
        }

        int textX = left ? edgeX + 300 : edgeX - 300;
        int y = top + 8;
        const int maxTextW = 290;
        void Line(string text, int size, Color color)
        {
            text = Ui.FitEnd(text, size, maxTextW);
            if (left)
            {
                Ui.Text(text, textX, y, size, color);
            }
            else
            {
                Ui.TextRight(text, textX, y, size, color);
            }

            y += size + 6;
        }

        Line(who + (_locked[side] ? "  - LOCKED IN" : string.Empty), 20, active ? tag : Theme.TextDim);
        Line(shown.Name, 34, Theme.Text);
        if (shown.Identity is string identity)
        {
            Line(identity, 18, Theme.TextDim);
        }

        Line($"HP {shown.Combat.MaxHp}   Stagger {shown.Combat.StaggerMax}   Speed {shown.Movement.WalkForwardSpeed:0.#}", 18, Theme.Text);
        Line($"Defend: {shown.Defense.Kind}", 16, Theme.TextDim);
        Line($"Passive: {shown.Passive.ToStats().Describe()}", 16, Theme.TextDim);
        foreach (string button in MoveData.Buttons)
        {
            MoveData move = shown.Move(button);
            if (!move.Enabled)
            {
                continue;
            }

            string key = button switch { "punch" => "S1", "skill2" => "S2", "skill3" => "S3", _ => "EGO" };
            string extra = button == "ego" && move.SinCost.Count > 0
                ? $"  [{move.Grade ?? "E.G.O"}, {string.Join(" ", move.SinCost.Select(c => $"{c.Amount} {c.Sin}"))}]"
                : $"  {move.DamageType} x{move.Coins}";
            Line($"{key}  {move.Name}{extra}", 16, Theme.Text);
        }
    }

    /// <summary>Loops the idle clip in the preview. False if the look has no idle picture ready.</summary>
    private bool DrawIdle(LookSlot slot, int x, int floorY, int facing)
    {
        LookData look = _looks.Get(slot);
        List<LookFrame> frames = look.Frames(ClipId.Idle);
        if (frames.Count == 0)
        {
            frames = look.PoseFrames.Take(1).ToList();
        }

        if (frames.Count == 0)
        {
            return false;
        }

        int total = Math.Max(1, frames.Sum(f => Math.Max(1, f.Hold)));
        int t = (int)(Raylib.GetTime() * 60) % total;
        LookFrame frame = frames[^1];
        foreach (LookFrame f in frames)
        {
            t -= Math.Max(1, f.Hold);
            if (t < 0)
            {
                frame = f;
                break;
            }
        }

        return _looks.DrawPicture(slot, frame, x, floorY, facing, Color.White, 1.1f);
    }
}

/// <summary>Pause menu over a match. The game fills in the items (they differ per mode).</summary>
public sealed class PauseMenu
{
    public MenuList List { get; } = new();

    /// <summary>The chosen item's label, "Resume" for Esc / back, or null.</summary>
    public string? Update(KeyboardSampler k)
    {
        if (MenuKeys.Back(k))
        {
            return "Resume";
        }

        return List.Update(k) ? List.Selected.Label : null;
    }

    /// <summary>-1 / +1 when Left / Right is pressed on an item with a value (an option), else 0.</summary>
    public int Adjust(KeyboardSampler k) => List.Items.Count > 0 && List.Selected.Value is not null
        ? (MenuKeys.Left(k) ? -1 : MenuKeys.Right(k) ? 1 : 0)
        : 0;

    public void Draw(int width, int height, string title = "PAUSED")
    {
        MenuArt.Veil(width, height);
        int rows = List.Items.Count;
        int top = Math.Max(150, 330 - (rows * 21));
        MenuArt.Heading(title, width, top - 90);
        List.Draw(width / 2, top, 520, rows > 7 ? 40 : 46, rows > 7 ? 24 : 28);
    }
}

/// <summary>Card after a match: a headline and a short list of what to do next.</summary>
public sealed class ResultsScreen
{
    private readonly MenuList _list = new();

    public void Open(params string[] choices)
    {
        _list.Set(choices.Select(c => new MenuList.Item(c)));
        _list.Index = 0;
    }

    /// <summary>The chosen label, or null.</summary>
    public string? Update(KeyboardSampler k) => _list.Update(k) ? _list.Selected.Label : null;

    public void Draw(TestArena arena, int width, int height, string headline, string? subline = null)
    {
        MenuArt.Veil(width, height, 185);
        Ui.TextCentered(headline, (width / 2) + 4, 154, 72, new Color((byte)60, (byte)8, (byte)12, (byte)255));
        Ui.TextCentered(headline, width / 2, 150, 72, Theme.Accent);
        Ui.TextCentered($"{arena.Player1Wins} - {arena.DummyWins}", width / 2, 240, 40, Theme.Text);
        if (subline is not null)
        {
            Ui.TextCentered(subline, width / 2, 292, 22, Theme.TextDim);
        }

        _list.Draw(width / 2, 350, 420);
    }
}

/// <summary>"STAGE 2 / 5   YI SANG  VS  FAUST" before an arcade fight (and a versus match).</summary>
public static class VersusIntro
{
    public const int Frames = 110;

    public static void Draw(int width, int height, int frame, FighterData left, FighterData right, string? stage, string? rightTag)
    {
        MenuArt.Background(width, height, Raylib.GetTime());
        float t = Math.Clamp(frame / 20f, 0f, 1f);
        int slide = (int)((1f - t) * 500);

        Color ColorOf(FighterData d, Color fallback)
        {
            uint rgb = FighterData.ParseRgb(d.Color);
            return rgb == 0 ? fallback : new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
        }

        Raylib.DrawRectangle(-slide, 230, width / 2, 170, Raylib.Fade(ColorOf(left, MenuArt.P1Color), 0.35f));
        Raylib.DrawRectangle((width / 2) + slide, 330, width / 2, 170, Raylib.Fade(ColorOf(right, MenuArt.P2Color), 0.35f));
        Ui.Text(left.Name.ToUpperInvariant(), 70 - slide, 270, 64, Theme.Text);
        Ui.Text(left.Identity ?? string.Empty, 74 - slide, 340, 22, Theme.TextDim);
        Ui.TextRight(right.Name.ToUpperInvariant(), width - 70 + slide, 370, 64, Theme.Text);
        Ui.TextRight(rightTag ?? right.Identity ?? string.Empty, width - 74 + slide, 440, 22, Theme.TextDim);
        if (frame > 14)
        {
            Ui.TextCentered("VS", (width / 2) + 4, 324, 110, new Color((byte)60, (byte)8, (byte)12, (byte)255));
            Ui.TextCentered("VS", width / 2, 320, 110, Theme.Accent);
        }

        if (stage is not null)
        {
            Ui.TextCentered(stage, width / 2, 120, 36, Theme.Accent);
        }

        Ui.TextCentered("J / Enter to skip", width / 2, height - 40, 18, Theme.TextDim);
    }
}
