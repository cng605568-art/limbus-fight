using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>
/// F2 screen: the fighter editor. Picks which fighter file is in each slot, makes new fighters,
/// and edits body boxes, movement and the punch's frame data and hitbox with a frame-by-frame
/// preview at game size (pose, hurtboxes, hitbox, pushbox). Every change is saved and goes live.
/// </summary>
public sealed class BoxEditorScreen
{
    private const int Font = 20;
    private const int PanelTop = 112;
    private const int PanelBottom = 652;
    private const int ListX = 16;
    private const int ListW = 600;
    private const int RowH = 22;
    private const int ListTop = PanelTop + 44;
    private const int VisibleRows = (PanelBottom - ListTop - 8) / RowH;

    private const int PreviewX = 628;
    private const int PreviewW = 636;
    private const int PreviewFloorY = 440;
    private const int IdleTail = 20;

    private sealed record Field(string Section, string Label, string Unit, float Step, float BigStep, bool Whole,
        Func<FighterData, float> Get, Action<FighterData, float> Set, Func<FighterData, float> Default)
    {
        /// <summary>For choice fields (type, Sin, status): how the value reads, and how many choices wrap around.</summary>
        public Func<float, string>? Format { get; init; }

        public int Choices { get; init; }
    }

    private static readonly string[] StatusChoices = { "none", "Burn", "Bleed", "Tremor", "Rupture", "Sinking", "Poise", "Charge" };

    /// <summary>The fighter's boss rules, created (switched off) when a boss field is first edited.</summary>
    private static BossData B(FighterData d) => d.Boss ??= new BossData { Enabled = false };

    private static Field Resist(string label, Func<FighterData, float> get, Action<FighterData, float> set) =>
        new("COMBAT", label, "x", 0.25f, 0.5f, false, get, set, _ => 1f) { Format = v => $"{v:0.##} {CombatRules.ResistLabel(v)}" };

    /// <summary>A status slot (the first entry of a move's on-hit list): kind, potency, count.</summary>
    private Field[] StatusFields(string label, Func<FighterData, List<StatusData>> list) => new[]
    {
        new Field("MOVE", label, string.Empty, 1, 1, true,
            d => list(d).Count == 0 ? 0 : (int)list(d)[0].Status + 1,
            (d, v) =>
            {
                List<StatusData> l = list(d);
                int choice = (((int)v % StatusChoices.Length) + StatusChoices.Length) % StatusChoices.Length;
                if (choice == 0)
                {
                    l.Clear();
                }
                else if (l.Count == 0)
                {
                    l.Add(new StatusData { Status = (StatusKind)(choice - 1), Potency = 2, Count = 3 });
                }
                else
                {
                    l[0].Status = (StatusKind)(choice - 1);
                }
            },
            _ => 0)
        { Format = v => StatusChoices[(int)v], Choices = StatusChoices.Length },
        new Field("MOVE", "   potency", string.Empty, 1, 5, true,
            d => list(d).Count == 0 ? 0 : list(d)[0].Potency,
            (d, v) => { if (list(d).Count > 0) { list(d)[0].Potency = Math.Max(1, (int)v); } },
            d => list(d).Count == 0 ? 0 : 2),
        new Field("MOVE", "   count", string.Empty, 1, 5, true,
            d => list(d).Count == 0 ? 0 : list(d)[0].Count,
            (d, v) => { if (list(d).Count > 0) { list(d)[0].Count = Math.Max(1, (int)v); } },
            d => list(d).Count == 0 ? 0 : 3),
    };

    private static readonly FighterData Defaults = new();


    private readonly Field[] _fields;
    private string _moveButton = "punch";

    private MoveData M(FighterData d) => d.Move(_moveButton);

    private MoveData DefaultMove => MoveData.Default(_moveButton);

    private static readonly string[] MoveTitles = { "SKILL 1 (J)", "SKILL 2 (K)", "SKILL 3 (L)", "E.G.O (I)" };

    private string MoveTitle => MoveTitles[Array.IndexOf(MoveData.Buttons, _moveButton)];

    private static void SetEgoSin(MoveData move, int choice)
    {
        if (choice == 0)
        {
            move.SinCost.Clear();
        }
        else if (move.SinCost.Count == 0)
        {
            move.SinCost.Add(new SinCostData { Sin = (Sin)(choice - 1), Amount = 3 });
        }
        else
        {
            move.SinCost[0].Sin = (Sin)(choice - 1);
        }
    }

    private Field[] BuildFields() =>
    [
        new("BODY", "Hurtbox width", "px", 2, 10, true, d => d.Body.HurtboxWidth, (d, v) => d.Body.HurtboxWidth = v, _ => Defaults.Body.HurtboxWidth),
        new("BODY", "Hurtbox height", "px", 2, 10, true, d => d.Body.HurtboxHeight, (d, v) => d.Body.HurtboxHeight = v, _ => Defaults.Body.HurtboxHeight),
        new("BODY", "Pushbox width", "px", 2, 10, true, d => d.Body.PushboxWidth, (d, v) => d.Body.PushboxWidth = v, _ => Defaults.Body.PushboxWidth),
        new("BODY", "Pushbox height", "px", 2, 10, true, d => d.Body.PushboxHeight, (d, v) => d.Body.PushboxHeight = v, _ => Defaults.Body.PushboxHeight),

        new("MOVEMENT", "Walk forward speed", "px/f", 0.1f, 1, false, d => d.Movement.WalkForwardSpeed, (d, v) => d.Movement.WalkForwardSpeed = v, _ => Defaults.Movement.WalkForwardSpeed),
        new("MOVEMENT", "Walk back speed", "px/f", 0.1f, 1, false, d => d.Movement.WalkBackwardSpeed, (d, v) => d.Movement.WalkBackwardSpeed = v, _ => Defaults.Movement.WalkBackwardSpeed),
        new("MOVEMENT", "Jump launch speed", "px/f", 0.5f, 2, false, d => d.Movement.JumpLaunchSpeed, (d, v) => d.Movement.JumpLaunchSpeed = v, _ => Defaults.Movement.JumpLaunchSpeed),
        new("MOVEMENT", "Gravity", "px/f2", 0.05f, 0.25f, false, d => d.Movement.Gravity, (d, v) => d.Movement.Gravity = v, _ => Defaults.Movement.Gravity),
        new("MOVEMENT", "Jump forward speed", "px/f", 0.1f, 1, false, d => d.Movement.JumpForwardSpeed, (d, v) => d.Movement.JumpForwardSpeed = v, _ => Defaults.Movement.JumpForwardSpeed),
        new("MOVEMENT", "Jump crouch", "frames", 1, 5, true, d => d.Movement.JumpSquatFrames, (d, v) => d.Movement.JumpSquatFrames = (int)v, _ => Defaults.Movement.JumpSquatFrames),
        new("MOVEMENT", "Landing", "frames", 1, 5, true, d => d.Movement.LandingFrames, (d, v) => d.Movement.LandingFrames = (int)v, _ => Defaults.Movement.LandingFrames),

        new("COMBAT", "Max HP", string.Empty, 5, 25, true, d => d.Combat.MaxHp, (d, v) => d.Combat.MaxHp = (int)v, _ => Defaults.Combat.MaxHp),
        new("COMBAT", "Stagger bar", string.Empty, 2, 10, true, d => d.Combat.StaggerMax, (d, v) => d.Combat.StaggerMax = (int)v, _ => Defaults.Combat.StaggerMax),
        Resist("Slash resist", d => d.Combat.SlashResist, (d, v) => d.Combat.SlashResist = v),
        Resist("Pierce resist", d => d.Combat.PierceResist, (d, v) => d.Combat.PierceResist = v),
        Resist("Blunt resist", d => d.Combat.BluntResist, (d, v) => d.Combat.BluntResist = v),

        new("DEFENSE (S)", "Defense", string.Empty, 1, 1, true, d => (int)d.Defense.Kind, (d, v) => d.Defense.Kind = (DefenseKind)(((int)v % 3 + 3) % 3), _ => 0)
        { Format = v => ((DefenseKind)(int)v) switch { DefenseKind.Guard => "Guard (hold)", DefenseKind.Evade => "Evade (press)", _ => "Counter (press)" }, Choices = 3 },
        new("DEFENSE (S)", "Guard: damage taken", "%", 5, 25, true, d => d.Defense.GuardDamage * 100f, (d, v) => d.Defense.GuardDamage = v / 100f, _ => Defaults.Defense.GuardDamage * 100f),
        new("DEFENSE (S)", "Guard: stagger taken", "%", 5, 25, true, d => d.Defense.GuardStagger * 100f, (d, v) => d.Defense.GuardStagger = v / 100f, _ => Defaults.Defense.GuardStagger * 100f),
        new("DEFENSE (S)", "Evade/counter length", "frames", 1, 5, true, d => d.Defense.TotalFrames, (d, v) => d.Defense.TotalFrames = (int)v, _ => Defaults.Defense.TotalFrames),
        new("DEFENSE (S)", "Window from frame", string.Empty, 1, 5, true, d => d.Defense.WindowStart, (d, v) => d.Defense.WindowStart = (int)v, _ => Defaults.Defense.WindowStart),
        new("DEFENSE (S)", "Window to frame", string.Empty, 1, 5, true, d => d.Defense.WindowEnd, (d, v) => d.Defense.WindowEnd = (int)v, _ => Defaults.Defense.WindowEnd),
        new("DEFENSE (S)", "Evade speed", "px/f", 0.5f, 2, false, d => d.Defense.EvadeSpeed, (d, v) => d.Defense.EvadeSpeed = v, _ => Defaults.Defense.EvadeSpeed),

        new("PASSIVE", "Passive", string.Empty, 1, 1, true, d => (int)d.Passive.Kind, (d, v) => d.Passive.Kind = (PassiveKind)(((int)v % 6 + 6) % 6), _ => 0)
        { Format = v => ((PassiveKind)(int)v).ToString(), Choices = 6 },
        new("PASSIVE", "Passive value", string.Empty, 1, 5, true, d => d.Passive.Value, (d, v) => d.Passive.Value = (int)v, _ => 0),
        new("PASSIVE", "Passive status", string.Empty, 1, 1, true, d => (int)d.Passive.Status, (d, v) => d.Passive.Status = (StatusKind)(((int)v % 7 + 7) % 7), _ => 0)
        { Format = v => ((StatusKind)(int)v).ToString(), Choices = 7 },

        new("BOSS", "Boss", string.Empty, 1, 1, true, d => d.IsBoss ? 1 : 0, (d, v) => B(d).Enabled = ((int)v % 2 + 2) % 2 == 1, _ => 0)
        { Format = v => v >= 1 ? "yes (armor, size, phase 2)" : "no", Choices = 2 },
        new("BOSS", "Super armor", string.Empty, 1, 1, true, d => (int)(d.Boss?.Armor ?? ArmorKind.Attacks), (d, v) => B(d).Armor = (ArmorKind)(((int)v % 3 + 3) % 3), _ => 1)
        { Format = v => ((ArmorKind)(int)v) switch { ArmorKind.None => "none", ArmorKind.Attacks => "while attacking", _ => "always" }, Choices = 3 },
        new("BOSS", "Damage through armor", "%", 5, 25, true, d => (d.Boss?.ArmorDamage ?? 1f) * 100f, (d, v) => B(d).ArmorDamage = v / 100f, _ => 100),
        new("BOSS", "Look size", "%", 5, 25, true, d => (d.Boss?.DrawScale ?? 1.4f) * 100f, (d, v) => B(d).DrawScale = v / 100f, _ => 140),
        new("BOSS", "Phase 2 below health", "%", 5, 10, true, d => d.Boss?.Phase2HpPercent ?? 50, (d, v) => B(d).Phase2HpPercent = (int)v, _ => 50),
        new("BOSS", "Phase 2 extra damage", "%", 5, 25, true, d => d.Boss?.Phase2DamagePercent ?? 20, (d, v) => B(d).Phase2DamagePercent = (int)v, _ => 20),
        new("BOSS", "Phase 2 clash power", string.Empty, 1, 5, false, d => d.Boss?.Phase2ClashBonus ?? 3, (d, v) => B(d).Phase2ClashBonus = v, _ => 3),

        new("MOVE", "Move is on", string.Empty, 1, 1, true, d => M(d).Enabled ? 1 : 0, (d, v) => M(d).Enabled = _moveButton == "punch" || ((int)v % 2 + 2) % 2 == 1, _ => 1)
        { Format = v => v >= 1 ? "yes" : "no (button does nothing)", Choices = 2 },
        new("MOVE", "Coins (hits)", string.Empty, 1, 1, true, d => M(d).Coins, (d, v) => M(d).Coins = (int)v, _ => DefaultMove.Coins),
        new("MOVE", "Frames between coins", "frames", 1, 2, true, d => M(d).CoinInterval, (d, v) => M(d).CoinInterval = (int)v, _ => DefaultMove.CoinInterval),
        new("MOVE", "Damage", string.Empty, 1, 5, true, d => M(d).Damage, (d, v) => M(d).Damage = (int)v, _ => DefaultMove.Damage),
        new("MOVE", "Stagger damage", string.Empty, 1, 5, true, d => M(d).StaggerDamage, (d, v) => M(d).StaggerDamage = (int)v, _ => DefaultMove.StaggerDamage),
        new("MOVE", "Clash power", string.Empty, 1, 5, false, d => M(d).ClashPower, (d, v) => M(d).ClashPower = v, _ => DefaultMove.ClashPower),
        new("MOVE", "Damage type", string.Empty, 1, 1, true, d => (int)M(d).DamageType, (d, v) => M(d).DamageType = (DamageType)(((int)v % 3 + 3) % 3), _ => (int)DefaultMove.DamageType)
        { Format = v => ((DamageType)(int)v).ToString(), Choices = 3 },
        new("MOVE", "Sin", string.Empty, 1, 1, true, d => (int)M(d).Sin, (d, v) => M(d).Sin = (Sin)(((int)v % 7 + 7) % 7), _ => (int)DefaultMove.Sin)
        { Format = v => ((Sin)(int)v).ToString(), Choices = 7 },
        new("MOVE", "E.G.O cost: Sin", string.Empty, 1, 1, true, d => M(d).SinCost.Count == 0 ? 0 : (int)M(d).SinCost[0].Sin + 1,
            (d, v) => SetEgoSin(M(d), (((int)v % 8) + 8) % 8), _ => DefaultMove.SinCost.Count == 0 ? 0 : (int)DefaultMove.SinCost[0].Sin + 1)
        { Format = v => (int)v == 0 ? "free" : ((Sin)((int)v - 1)).ToString(), Choices = 8 },
        new("MOVE", "E.G.O cost: amount", string.Empty, 1, 2, true, d => M(d).SinCost.Count == 0 ? 0 : M(d).SinCost[0].Amount,
            (d, v) => { if (M(d).SinCost.Count > 0) { M(d).SinCost[0].Amount = Math.Max(1, (int)v); } }, _ => DefaultMove.SinCost.Count == 0 ? 0 : DefaultMove.SinCost[0].Amount),
        new("MOVE", "Charge spent", string.Empty, 1, 5, true, d => M(d).ChargeCost, (d, v) => M(d).ChargeCost = (int)v, _ => 0),
        new("MOVE", "Damage per Charge use", string.Empty, 1, 5, true, d => M(d).ChargeBonusDamage, (d, v) => M(d).ChargeBonusDamage = (int)v, _ => 0),
        .. StatusFields("On hit, opponent gets", d => M(d).OnHit),
        .. StatusFields("On hit, puncher gains", d => M(d).SelfOnHit),
        new("MOVE", "Startup", "frames", 1, 5, true, d => M(d).Startup, (d, v) => M(d).Startup = (int)v, _ => DefaultMove.Startup),
        new("MOVE", "Active", "frames", 1, 5, true, d => M(d).Active, (d, v) => M(d).Active = (int)v, _ => DefaultMove.Active),
        new("MOVE", "Recovery", "frames", 1, 5, true, d => M(d).Recovery, (d, v) => M(d).Recovery = (int)v, _ => DefaultMove.Recovery),
        new("MOVE", "Hitstun", "frames", 1, 5, true, d => M(d).Hitstun, (d, v) => M(d).Hitstun = (int)v, _ => DefaultMove.Hitstun),
        new("MOVE", "Hitstop", "frames", 1, 5, true, d => M(d).Hitstop, (d, v) => M(d).Hitstop = (int)v, _ => DefaultMove.Hitstop),
        new("MOVE", "Pushback", "px/f", 0.5f, 2, false, d => M(d).Pushback, (d, v) => M(d).Pushback = v, _ => DefaultMove.Pushback),
        new("MOVE", "Hitbox forward", "px", 2, 10, true, d => M(d).Hitbox.X, (d, v) => M(d).Hitbox.X = v, _ => DefaultMove.Hitbox.X),
        new("MOVE", "Hitbox top above feet", "px", 2, 10, true, d => -M(d).Hitbox.Y, (d, v) => M(d).Hitbox.Y = -v, _ => -DefaultMove.Hitbox.Y),
        new("MOVE", "Hitbox width", "px", 2, 10, true, d => M(d).Hitbox.Width, (d, v) => M(d).Hitbox.Width = v, _ => DefaultMove.Hitbox.Width),
        new("MOVE", "Hitbox height", "px", 2, 10, true, d => M(d).Hitbox.Height, (d, v) => M(d).Hitbox.Height = v, _ => DefaultMove.Hitbox.Height),
        new("MOVE", "Arm hurtbox width", "px", 2, 10, true, d => M(d).ArmHurtboxWidth, (d, v) => M(d).ArmHurtboxWidth = v, _ => DefaultMove.ArmHurtboxWidth),
    ];

    private readonly RosterManager _roster;
    private readonly LookManager _looks;
    private LookSlot _slot = LookSlot.Player1;
    private int _row;
    private int _scroll;
    private int _frame;
    private bool _playing = true;
    private long _tick;
    private bool _renaming;
    private string _nameDraft = string.Empty;
    private string? _status;
    private Color _statusColor = Theme.Text;
    private double _statusUntil;

    public BoxEditorScreen(RosterManager roster, LookManager looks)
    {
        _roster = roster;
        _looks = looks;
        _fields = BuildFields();
    }

    private FighterData Fighter => _roster.Get(_slot);

    /// <summary>Rows: 0 = the fighter's name, 1.. = fields.</summary>
    private int RowCount => _fields.Length + 1;

    public void Update(KeyboardSampler keys)
    {
        _tick++;
        if (_renaming)
        {
            UpdateRename(keys);
            return;
        }

        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        if (keys.WasPressed(KeyboardKey.Tab))
        {
            _slot = _slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
        }

        if (keys.WasPressed(KeyboardKey.LeftBracket) || keys.WasPressed(KeyboardKey.RightBracket))
        {
            _roster.Cycle(_slot, keys.WasPressed(KeyboardKey.RightBracket) ? 1 : -1);
            SetStatus($"{SlotName} is now {Fighter.Name} ({_roster.IdOf(_slot)}.json).", Theme.Good);
        }

        if (keys.WasPressed(KeyboardKey.Q) || keys.WasPressed(KeyboardKey.E))
        {
            int i = Array.IndexOf(MoveData.Buttons, _moveButton) + (keys.WasPressed(KeyboardKey.E) ? 1 : -1);
            _moveButton = MoveData.Buttons[(i + MoveData.Buttons.Length) % MoveData.Buttons.Length];
            _frame = 0;
        }

        if (ctrl && keys.WasPressed(KeyboardKey.N))
        {
            string id = _roster.CreateCopy(_slot);
            SetStatus($"New fighter {Fighter.Name} ({id}.json), a copy of the old one. Enter on its name renames it.", Theme.Good);
        }

        if (Repeat(KeyboardKey.Down))
        {
            _row = Math.Min(RowCount - 1, _row + 1);
        }

        if (Repeat(KeyboardKey.Up))
        {
            _row = Math.Max(0, _row - 1);
        }

        if (_row == 0 && (keys.WasPressed(KeyboardKey.Enter) || keys.WasPressed(KeyboardKey.KpEnter)))
        {
            _renaming = true;
            _nameDraft = Fighter.Name;
            while (Raylib.GetCharPressed() != 0)
            {
                // drop characters typed before renaming started
            }
        }

        if (_row > 0)
        {
            Field field = _fields[_row - 1];
            int dir = (Repeat(KeyboardKey.Right) ? 1 : 0) - (Repeat(KeyboardKey.Left) ? 1 : 0);
            if (dir != 0)
            {
                float step = shift ? field.BigStep : field.Step;
                float value = field.Choices > 0
                    ? (((int)field.Get(Fighter) + dir) % field.Choices + field.Choices) % field.Choices
                    : MathF.Round((field.Get(Fighter) + (dir * step)) / field.Step) * field.Step;
                _roster.Edit(_slot, d => field.Set(d, field.Whole ? MathF.Round(value) : value));
            }

            if (keys.WasPressed(KeyboardKey.Delete) || keys.WasPressed(KeyboardKey.Backspace))
            {
                _roster.Edit(_slot, d => field.Set(d, field.Default(d)));
                SetStatus($"{field.Label} back to the training box value.", Theme.Text);
            }
        }

        // Frame scrubber: 0 = standing, 1.. = the punch's frames.
        MoveData shown = M(Fighter);
        int total = shown.Startup + shown.Active + shown.Recovery;
        if (keys.WasPressed(KeyboardKey.Space))
        {
            _playing = !_playing;
        }

        if (Repeat(KeyboardKey.Period))
        {
            _playing = false;
            _frame = Math.Min(total, _frame + 1);
        }

        if (Repeat(KeyboardKey.Comma))
        {
            _playing = false;
            _frame = Math.Max(0, _frame - 1);
        }

        if (_playing && _tick % 2 == 0)
        {
            // Half speed, so each frame can be seen; a pause after each punch.
            _frame = (_frame + 1) % (total + 1 + IdleTail);
        }

        // Section headings take rows too: keep the selected field visible with room for them.
        int fieldRows = VisibleRows - 4;
        _scroll = Math.Clamp(_scroll, Math.Max(0, _row - fieldRows), Math.Max(0, _row - 1));
    }

    public void Draw(int width, int height)
    {
        Raylib.ClearBackground(Theme.Background);
        Ui.Text("FIGHTER EDITOR", 20, 14, 30, Theme.Accent);
        Ui.Text("moves, frame data and boxes, saved per fighter", 20 + Ui.Measure("FIGHTER EDITOR", 30) + 18, 22, Font, Theme.TextDim);
        Ui.TextRight("F2  back to the arena", width - 20, 22, Font, Theme.TextDim);

        DrawStatus(width);
        DrawTabs();
        DrawFields();
        DrawPreview();

        Ui.TextCentered("Tab  P1/Dummy   [ ]  fighter   Q/E  move   Ctrl+N  new fighter   Up/Down  pick   Left/Right  change (Shift: more)",
            width / 2, height - 56, Font, Theme.TextDim);
        Ui.TextCentered("Del  reset value    Enter on the name  rename    Space  play / pause    ,  .  step frames    F4  looks    F2  back",
            width / 2, height - 30, Font, Theme.TextDim);
    }

    // =========================================================================================

    private string SlotName => _slot == LookSlot.Player1 ? "P1" : "The Dummy";

    private void UpdateRename(KeyboardSampler keys)
    {
        int c;
        while ((c = Raylib.GetCharPressed()) != 0)
        {
            if (c is >= 32 and < 127 && _nameDraft.Length < 40)
            {
                _nameDraft += (char)c;
            }
        }

        if (_nameDraft.Length > 0 && Repeat(KeyboardKey.Backspace))
        {
            _nameDraft = _nameDraft[..^1];
        }

        if (keys.WasPressed(KeyboardKey.Enter) || keys.WasPressed(KeyboardKey.KpEnter))
        {
            _renaming = false;
            if (_nameDraft.Trim().Length > 0)
            {
                _roster.Rename(_slot, _nameDraft);
                SetStatus($"Renamed to {Fighter.Name}. (The file name stays {_roster.IdOf(_slot)}.json.)", Theme.Good);
            }
        }
    }

    private static bool Repeat(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
        _statusUntil = Raylib.GetTime() + 6;
    }

    private void DrawStatus(int width)
    {
        string text;
        Color color = Theme.TextDim;
        if (_status is not null && Raylib.GetTime() < _statusUntil)
        {
            text = _status;
            color = _statusColor;
        }
        else if (_roster.Problem is string problem)
        {
            text = problem;
            color = Theme.Bad;
        }
        else
        {
            text = "Every change is saved to %LOCALAPPDATA%\\LimbusFight\\fighters and is live in the arena.";
        }

        Ui.Text(Ui.FitEnd(text, Font, width - 40), 20, 52, Font, color);
    }

    private void DrawTabs()
    {
        int x = ListX;
        foreach (LookSlot slot in new[] { LookSlot.Player1, LookSlot.Dummy })
        {
            string label = slot == LookSlot.Player1 ? "P1" : "DUMMY";
            int w = Ui.Measure(label, Font) + 28;
            bool on = slot == _slot;
            Raylib.DrawRectangle(x, 80, w, 26, on ? new Color(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B, (byte)70) : Theme.Panel);
            Raylib.DrawRectangleLines(x, 80, w, 26, on ? Theme.Accent : Theme.PanelBorder);
            Ui.Text(label, x + 14, 84, Font, on ? Theme.Text : Theme.TextDim);
            x += w + 8;
        }

        List<FighterEntry> all = _roster.List();
        int index = all.FindIndex(e => e.Id == _roster.IdOf(_slot)) + 1;
        Ui.Text(Ui.FitEnd($"fighter {index} of {all.Count}   file {_roster.IdOf(_slot)}.json", Font, ListW - (x - ListX) - 10), x + 10, 84, Font, Theme.TextDim);
    }

    private void DrawFields()
    {
        Raylib.DrawRectangle(ListX, PanelTop, ListW, PanelBottom - PanelTop, Theme.Panel);
        Raylib.DrawRectangleLines(ListX, PanelTop, ListW, PanelBottom - PanelTop, Theme.Accent);

        // Row 0: the name.
        bool nameSelected = _row == 0;
        if (nameSelected)
        {
            Raylib.DrawRectangle(ListX + 2, PanelTop + 6, ListW - 4, 30, new Color(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B, (byte)40));
        }

        string name = _renaming ? _nameDraft + ((_tick / 20) % 2 == 0 ? "_" : " ") : Fighter.Name;
        Ui.Text(Ui.FitEnd(name, 26, ListW - 220), ListX + 12, PanelTop + 9, 26, _renaming ? Theme.Warn : Theme.Text);
        Ui.TextRight(_renaming ? "type, Enter to keep" : nameSelected ? "Enter to rename" : string.Empty, ListX + ListW - 12, PanelTop + 13, Font, Theme.TextDim);

        string? lastSection = null;
        int y = ListTop;
        for (int i = 0; i < _fields.Length; i++)
        {
            int row = i + 1;
            if (row < _scroll + 1 || y > PanelBottom - RowH)
            {
                continue;
            }

            Field field = _fields[i];
            if (field.Section != lastSection)
            {
                lastSection = field.Section;
                string heading = field.Section == "MOVE" ? $"MOVE: {MoveTitle}    Q / E  other moves" : field.Section;
                Ui.Text(heading, ListX + 12, y + 2, 18, Theme.Accent);
                y += RowH;
                if (y > PanelBottom - RowH)
                {
                    break;
                }
            }

            bool selected = row == _row;
            if (selected)
            {
                Raylib.DrawRectangle(ListX + 2, y, ListW - 4, RowH, new Color(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B, (byte)40));
            }

            float value = field.Get(Fighter);
            bool changed = MathF.Abs(value - field.Default(Fighter)) > 0.0001f;
            string text = field.Format is not null ? field.Format(value) : field.Whole ? $"{value:0}" : $"{value:0.##}";
            Ui.Text(field.Label, ListX + 24, y + 1, Font, selected ? Theme.Text : Theme.TextDim);
            Ui.TextRight($"{(selected ? "< " : string.Empty)}{text}{(selected ? " >" : string.Empty)}  {field.Unit}", ListX + ListW - 12, y + 1, Font, changed ? Theme.Warn : Theme.Text);
            y += RowH;
        }
    }

    private void DrawPreview()
    {
        Raylib.DrawRectangle(PreviewX, PanelTop, PreviewW, PanelBottom - PanelTop, Theme.Panel);
        Raylib.DrawRectangleLines(PreviewX, PanelTop, PreviewW, PanelBottom - PanelTop, Theme.PanelBorder);
        Ui.Text("Preview at game size", PreviewX + 12, PanelTop + 8, Font, Theme.Accent);
        Ui.TextRight(_playing ? "playing (half speed)" : "paused", PreviewX + PreviewW - 12, PanelTop + 8, Font, Theme.TextDim);

        FighterStats stats = Fighter.ToStats();
        AttackData punch = M(Fighter).ToAttack();
        int total = punch.TotalFrames;
        int frame = Math.Min(_frame, total); // frames past the end show standing (the pause between punches)
        bool attacking = frame >= 1 && _frame <= total;

        float feetX = PreviewX + 220f;
        float feetY = PreviewFloorY;

        Raylib.BeginScissorMode(PreviewX + 1, PanelTop + 34, PreviewW - 2, PreviewFloorY + 24 - PanelTop - 34);
        Raylib.DrawRectangleGradientV(PreviewX + 1, PanelTop + 34, PreviewW - 2, PreviewFloorY - PanelTop - 34, Theme.Background, Theme.BackgroundHorizon);
        Raylib.DrawRectangle(PreviewX + 1, PreviewFloorY, PreviewW - 2, 24, Theme.Floor);
        Raylib.DrawLineEx(new Vector2(PreviewX, PreviewFloorY), new Vector2(PreviewX + PreviewW, PreviewFloorY), 3f, Theme.Accent);

        // Pose: the punch frame the game would show, or standing.
        LookData look = Fighter.Look;
        List<LookFrame> movePose = MovePose(look, punch.Slot);
        List<LookFrame> pose = attacking && movePose.Count > 0 ? movePose : look.Frames(ClipId.Idle);
        int poseIndex = attacking && movePose.Count > 0 ? ClipPicker.PunchFrame(pose.Count, punch, frame) : 0;
        bool drewPose = pose.Count > 0 && _looks.DrawPicture(_slot, pose[poseIndex], feetX, feetY, 1, Color.White);
        if (!drewPose)
        {
            var body = new Rectangle(feetX - 38, feetY - 176, 76, 176);
            Raylib.DrawRectangleRec(body, _slot == LookSlot.Player1 ? Theme.Player1Body : Theme.DummyBody);
        }

        // Swing effect, as in a match.
        List<LookFrame> swing = EffectTiming.SwingFrames(look, punch.Slot);
        int sinceActive = frame - punch.FirstActiveFrame;
        int coin = sinceActive < 0 ? 0 : Math.Min(punch.Coins - 1, sinceActive / Math.Max(1, punch.CoinInterval));
        if (attacking && EffectTiming.At(swing, sinceActive - (coin * punch.CoinInterval)) is (int fx, float alpha))
        {
            (float sx, float sy) = (feetX + punch.HitboxOffsetX + (punch.HitboxWidth / 2f), feetY + punch.HitboxOffsetY + (punch.HitboxHeight / 2f));
            _looks.DrawEffect(_slot, swing[fx], sx, sy, 1, alpha);
        }

        // Boxes: pushbox (yellow), hurtboxes (blue), hitbox (red) on active frames.
        Raylib.DrawRectangleLinesEx(new Rectangle(feetX - (stats.PushboxWidth / 2f), feetY - stats.PushboxHeight, stats.PushboxWidth, stats.PushboxHeight), 1f, Theme.Pushbox);
        DrawBox(new Rectangle(feetX - (stats.HurtboxWidth / 2f), feetY - stats.HurtboxHeight, stats.HurtboxWidth, stats.HurtboxHeight), Theme.Hurtbox, 50);
        if (attacking && frame >= punch.Startup)
        {
            DrawBox(new Rectangle(feetX + punch.HitboxOffsetX, feetY + punch.HitboxOffsetY + 2f, punch.LimbHurtboxWidth, punch.HitboxHeight - 4f), Theme.Hurtbox, 50);
        }

        var hitbox = new Rectangle(feetX + punch.HitboxOffsetX, feetY + punch.HitboxOffsetY, punch.HitboxWidth, punch.HitboxHeight);
        if (attacking && punch.IsActiveFrame(frame))
        {
            DrawBox(hitbox, Theme.Hitbox, 110);
        }
        else
        {
            Raylib.DrawRectangleLinesEx(hitbox, 1f, new Color(Theme.Hitbox.R, Theme.Hitbox.G, Theme.Hitbox.B, (byte)70));
        }

        Raylib.DrawLine((int)feetX - 9, (int)feetY, (int)feetX + 9, (int)feetY, Color.White);
        Raylib.DrawLine((int)feetX, (int)feetY - 9, (int)feetX, (int)feetY + 9, Color.White);
        Raylib.EndScissorMode();

        DrawTimeline(punch, attacking ? frame : 0);
    }

    /// <summary>A move's poses: its own clip, else the next one down (E.G.O → Skill 3 → Skill 1).</summary>
    private static List<LookFrame> MovePose(LookData look, MoveSlot slot)
    {
        ClipId[] chain = slot switch
        {
            MoveSlot.Skill2 => new[] { ClipId.Skill2, ClipId.Punch },
            MoveSlot.Skill3 => new[] { ClipId.Skill3, ClipId.Punch },
            MoveSlot.Ego => new[] { ClipId.Ego, ClipId.Skill3, ClipId.Punch },
            _ => new[] { ClipId.Punch },
        };

        foreach (ClipId clip in chain)
        {
            if (look.Frames(clip).Count > 0)
            {
                return look.Frames(clip);
            }
        }

        return new List<LookFrame>();
    }

    private void DrawTimeline(AttackData punch, int frame)
    {
        int total = punch.TotalFrames;
        int x0 = PreviewX + 12;
        int y0 = PreviewFloorY + 36;
        int cell = Math.Clamp((PreviewW - 24) / Math.Max(1, total), 4, 24);

        for (int f = 1; f <= total; f++)
        {
            Color c = f <= punch.Startup ? Theme.Good : punch.IsActiveFrame(f) ? Theme.Hitbox : Theme.Hurtbox;
            int x = x0 + ((f - 1) * cell);
            Raylib.DrawRectangle(x, y0, cell - 1, 18, new Color(c.R, c.G, c.B, (byte)(f == frame ? 255 : 120)));
            if (f == frame)
            {
                Raylib.DrawRectangleLines(x - 1, y0 - 2, cell + 1, 22, Color.White);
            }
        }

        string phase = frame == 0 ? "standing" : punch.PhaseAt(frame).ToLowerInvariant();
        Ui.Text($"frame {frame} / {total}   {phase}", x0, y0 + 26, Font, Theme.Text);
        Ui.Text($"startup {punch.Startup}  active {punch.Active}  recovery {punch.Recovery}", x0, y0 + 50, Font, Theme.TextDim);

        int onHit = FrameAdvantageOnHit(punch);
        Ui.Text($"on hit {(onHit >= 0 ? "+" : string.Empty)}{onHit} (hitting on its first active frame)", x0, y0 + 74, Font, onHit >= 0 ? Theme.Good : Theme.Bad);
        Ui.Text("green = startup   red = active (hitbox out)   blue = recovery", x0, y0 + 98, 18, Theme.TextDim);
    }

    /// <summary>
    /// Frames the attacker can act before the opponent after a hit on the first active frame:
    /// the defender is free on the (hitstun + 1)th frame after hitstop, the attacker after its
    /// remaining active frames plus recovery. Positive = attacker's turn.
    /// </summary>
    public static int FrameAdvantageOnHit(AttackData attack) => attack.Hitstun + 1 - (attack.Active + attack.Recovery);

    private static void DrawBox(Rectangle r, Color c, byte fill)
    {
        Raylib.DrawRectangleRec(r, new Color(c.R, c.G, c.B, fill));
        Raylib.DrawRectangleLinesEx(r, 2f, c);
    }
}
