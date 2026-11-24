using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>
/// F4 screen: edit how P1 and the Dummy look. Each fighter has five clips (idle, walk, jump, punch,
/// hit) made of pictures picked in the F3 browser. Here frames are re-timed, re-ordered or removed,
/// and the look is sized and flipped against a live, game-sized preview with the hurtbox drawn on it.
/// Every change is saved immediately.
/// </summary>
public sealed class LookEditorScreen
{
    private const int Font = 20;
    private const int PanelTop = 112;
    private const int PanelBottom = 652;

    private const int ClipsX = 16;
    private const int ClipsW = 600;
    private const int ClipRowTop = PanelTop + 8;
    private const int ClipRowH = 76;
    private const int Thumb = 40;
    private const int ThumbGap = 22;
    private const int ThumbsVisible = (ClipsW - 24) / (Thumb + ThumbGap);

    private const int PreviewX = 628;
    private const int PreviewW = 636;
    private const int PreviewFloorY = 470;

    private static readonly ClipId[] AllClips =
    {
        ClipId.Idle, ClipId.Walk, ClipId.Jump, ClipId.Punch, ClipId.Skill2, ClipId.Skill3, ClipId.Ego, ClipId.Guard, ClipId.Evade, ClipId.Hit,
        ClipId.SwingFx, ClipId.Skill2Fx, ClipId.Skill3Fx, ClipId.EgoFx, ClipId.HitFx,
    };

    private const int VisibleClipRows = (PanelBottom - ClipRowTop) / ClipRowH;

    private int _clipScroll;

    private readonly LookManager _looks;
    private LookSlot _slot = LookSlot.Player1;
    private int _clipIndex;
    private int _frameIndex;
    private int _thumbScroll;
    private long _previewTick;
    private string? _status;
    private Color _statusColor = Theme.Text;
    private double _statusUntil;

    private readonly Func<LookSlot, FighterStats> _stats;

    public LookEditorScreen(LookManager looks, Func<LookSlot, FighterStats> stats)
    {
        _looks = looks;
        _stats = stats;
    }

    private ClipId Clip => AllClips[_clipIndex];

    /// <summary>The fighter shown (P1 / Dummy); F3's Ctrl+Q adds pictures to this slot.</summary>
    public LookSlot SelectedSlot => _slot;

    /// <summary>The clip selected here; F3's Ctrl+Q adds pictures to it.</summary>
    public ClipId SelectedClip => Clip;

    private LookData Look => _looks.Get(_slot);

    private List<LookFrame> Frames => Look.Frames(Clip);

    public void Update(KeyboardSampler keys)
    {
        _previewTick++;
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        if (keys.WasPressed(KeyboardKey.Tab))
        {
            _slot = _slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
            _frameIndex = 0;
        }

        if (ctrl && keys.WasPressed(KeyboardKey.Z))
        {
            SetStatus(_looks.UndoReplace(_slot) ? "Restored the look from before the auto look." : "Nothing to undo (Ctrl+Z undoes an auto look).", Theme.Text);
        }

        if (ctrl)
        {
            HandleNudge(shift);
        }
        else
        {
            HandleNavigation();
        }

        HandleEdits(keys, shift);
        _clipScroll = Math.Clamp(_clipScroll, Math.Max(0, _clipIndex - VisibleClipRows + 1), _clipIndex);
        _frameIndex = Math.Clamp(_frameIndex, 0, Math.Max(0, Frames.Count - 1));
        _thumbScroll = Math.Clamp(_thumbScroll, Math.Max(0, _frameIndex - ThumbsVisible + 1), _frameIndex);
    }

    public void Draw(int width, int height)
    {
        Raylib.ClearBackground(Theme.Background);

        Ui.Text("LOOK EDITOR", 20, 14, 30, Theme.Accent);
        Ui.Text("how each fighter looks, built from your own Limbus files", 20 + Ui.Measure("LOOK EDITOR", 30) + 18, 22, Font, Theme.TextDim);
        Ui.TextRight("F4  back to the arena", width - 20, 22, Font, Theme.TextDim);

        DrawStatusLine(width);
        DrawTabs();
        DrawClips();
        DrawPreview();

        Ui.TextCentered("Tab  P1 / Dummy    Up/Down  clip    Left/Right  frame    + / -  hold    ,  .  move frame    Del  remove",
            width / 2, height - 56, Font, Theme.TextDim);
        Ui.TextCentered("PgUp/PgDn  size   Home  auto size   F  flip   A  anchor   Ctrl+arrows  nudge   F3  add pictures   F4  back",
            width / 2, height - 30, Font, Theme.TextDim);
    }

    // =========================================================================================
    //  Input
    // =========================================================================================

    private void HandleNavigation()
    {
        if (Repeat(KeyboardKey.Down))
        {
            _clipIndex = Math.Min(AllClips.Length - 1, _clipIndex + 1);
            _frameIndex = 0;
        }

        if (Repeat(KeyboardKey.Up))
        {
            _clipIndex = Math.Max(0, _clipIndex - 1);
            _frameIndex = 0;
        }

        if (Repeat(KeyboardKey.Right))
        {
            _frameIndex++;
        }

        if (Repeat(KeyboardKey.Left))
        {
            _frameIndex--;
        }
    }

    private void HandleNudge(bool shift)
    {
        float step = shift ? 10f : 2f;
        float dx = (Repeat(KeyboardKey.Right) ? step : 0f) - (Repeat(KeyboardKey.Left) ? step : 0f);
        float dy = (Repeat(KeyboardKey.Down) ? step : 0f) - (Repeat(KeyboardKey.Up) ? step : 0f);
        if (dx != 0f || dy != 0f)
        {
            _looks.Modify(_slot, look =>
            {
                look.OffsetX += dx;
                look.OffsetY += dy;
            });
            SetStatus($"Offset {Look.OffsetX:0}, {Look.OffsetY:0} px (moves every picture of this look).", Theme.Text);
        }
    }

    private void HandleEdits(KeyboardSampler keys, bool shift)
    {
        List<LookFrame> frames = Frames;
        bool hasFrame = _frameIndex < frames.Count;

        int holdDelta = 0;
        if (Repeat(KeyboardKey.Equal) || Repeat(KeyboardKey.KpAdd))
        {
            holdDelta = shift ? 5 : 1;
        }

        if (Repeat(KeyboardKey.Minus) || Repeat(KeyboardKey.KpSubtract))
        {
            holdDelta = shift ? -5 : -1;
        }

        if (holdDelta != 0 && hasFrame)
        {
            int index = _frameIndex;
            ClipId clip = Clip;
            _looks.Modify(_slot, look => look.EditFrames(clip)[index].Hold += holdDelta);
            SetStatus(Clip == ClipId.Punch || Clip == ClipId.Jump
                ? "Saved. Note: punch and jump frames follow the move's timing, not their hold."
                : $"Hold {Frames[_frameIndex].Hold} frames ({Frames[_frameIndex].Hold / 60.0:0.00} s).", Theme.Text);
        }

        if (hasFrame && (keys.WasPressed(KeyboardKey.Delete) || keys.WasPressed(KeyboardKey.Backspace)))
        {
            int index = _frameIndex;
            ClipId clip = Clip;
            string name = frames[index].Name;
            _looks.Modify(_slot, look => look.EditFrames(clip).RemoveAt(index));
            SetStatus($"Removed {name} from {LookData.ClipKey(Clip)}.", Theme.Text);
        }

        if (hasFrame && (keys.WasPressed(KeyboardKey.Comma) || keys.WasPressed(KeyboardKey.Period)))
        {
            int to = _frameIndex + (keys.WasPressed(KeyboardKey.Period) ? 1 : -1);
            if (to >= 0 && to < frames.Count)
            {
                int from = _frameIndex;
                ClipId clip = Clip;
                _looks.Modify(_slot, look =>
                {
                    List<LookFrame> list = look.EditFrames(clip);
                    (list[from], list[to]) = (list[to], list[from]);
                });
                _frameIndex = to;
            }
        }

        if (Repeat(KeyboardKey.PageUp) || Repeat(KeyboardKey.PageDown))
        {
            float factor = Repeat(KeyboardKey.PageUp) ? 1.05f : 1f / 1.05f;
            float scale = Math.Clamp(_looks.EffectiveScale(_slot) * factor, 0.02f, 10f);
            _looks.Modify(_slot, look => look.Scale = scale);
            SetStatus($"Size {scale:0.000}x.  Home = automatic.", Theme.Text);
        }

        if (keys.WasPressed(KeyboardKey.Home))
        {
            _looks.Modify(_slot, look => look.Scale = 0f);
            SetStatus("Automatic size: the first idle picture is drawn about as tall as the hurtbox plus a head.", Theme.Text);
        }

        if (keys.WasPressed(KeyboardKey.A))
        {
            _looks.Modify(_slot, look => look.AnchorAtPivot = !look.AnchorAtPivot);
            SetStatus(Look.AnchorAtPivot
                ? "Standing on the pictures' pivot (the + in F3). Use this if Limbus puts pivots at the feet."
                : "Standing on the pictures' bottom edge.", Theme.Text);
        }

        if (keys.WasPressed(KeyboardKey.F))
        {
            _looks.Modify(_slot, look => look.ArtFacesLeft = !look.ArtFacesLeft);
            SetStatus(Look.ArtFacesLeft ? "Pictures are treated as facing left." : "Pictures are treated as facing right.", Theme.Text);
        }
    }

    private static bool Repeat(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
        _statusUntil = Raylib.GetTime() + 6;
    }

    // =========================================================================================
    //  Drawing
    // =========================================================================================

    private void DrawStatusLine(int width)
    {
        string text;
        Color color;
        if (_status is not null && Raylib.GetTime() < _statusUntil)
        {
            text = _status;
            color = _statusColor;
        }
        else if (_looks.Status(_slot) is string status)
        {
            text = (_slot == LookSlot.Player1 ? "P1: " : "Dummy: ") + status;
            color = _looks.IsLoading(_slot) ? Theme.Warn : Theme.Bad;
        }
        else
        {
            text = "Changes are saved as you make them, into the fighter's file (F2 shows which).";
            color = Theme.TextDim;
        }

        Ui.Text(Ui.FitEnd(text, Font, width - 40), 20, 52, Font, color);
    }

    private void DrawTabs()
    {
        int x = ClipsX;
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

        int frames = Look.AllFrames.Count();
        Ui.Text(frames == 0 ? "no pictures yet" : $"{frames} frame(s)", x + 10, 84, Font, Theme.TextDim);
    }

    private void DrawClips()
    {
        Raylib.DrawRectangle(ClipsX, PanelTop, ClipsW, PanelBottom - PanelTop, Theme.Panel);
        Raylib.DrawRectangleLines(ClipsX, PanelTop, ClipsW, PanelBottom - PanelTop, Theme.Accent);

        if (Look.IsEmpty)
        {
            int y = PanelTop + 20;
            foreach (string line in new[]
            {
                "No pictures yet. To dress this fighter:",
                string.Empty,
                "1. Press F3 (asset browser).",
                "2. Search a name, e.g. yisang, and open a",
                "   sactx... file (a sprite sheet).",
                "3. Pick a pose in the middle list.",
                $"4. Press Ctrl+1 idle, Ctrl+2 walk, Ctrl+3 jump,",
                "   Ctrl+4 punch, Ctrl+5 hit, Ctrl+6 / 7 effects" + (_slot == LookSlot.Dummy ? " (+Shift)." : "."),
                string.Empty,
                "Or press Ctrl+A in step 3 to fill every clip",
                "automatically, or Ctrl+Q to add to the clip",
                "selected here.",
                "Punch: 1st = wind-up, 2nd = hit, rest = recovery.",
            })
            {
                Ui.Text(line, ClipsX + 16, y, Font, line.Length > 0 && char.IsDigit(line[0]) ? Theme.Text : Theme.TextDim);
                y += 26;
            }

            return;
        }

        for (int c = _clipScroll; c < AllClips.Length && c < _clipScroll + VisibleClipRows; c++)
        {
            ClipId clip = AllClips[c];
            List<LookFrame> frames = Look.Frames(clip);
            int top = ClipRowTop + ((c - _clipScroll) * ClipRowH);
            bool selected = c == _clipIndex;

            if (selected)
            {
                Raylib.DrawRectangle(ClipsX + 2, top - 4, ClipsW - 4, ClipRowH - 4, new Color(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B, (byte)28));
            }

            string title = $"{ClipTitle(clip)}  {frames.Count} frame{(frames.Count == 1 ? string.Empty : "s")}";
            Ui.Text(title, ClipsX + 12, top, Font, selected ? Theme.Accent : Theme.Text);
            Ui.TextRight(ClipHint(clip), ClipsX + ClipsW - 12, top, Font, Theme.TextDim);

            int scroll = selected ? _thumbScroll : 0;
            for (int i = 0; i < ThumbsVisible && scroll + i < frames.Count; i++)
            {
                int index = scroll + i;
                int x = ClipsX + 12 + (i * (Thumb + ThumbGap));
                int y = top + 23;
                DrawThumb(frames[index], x, y, selected && index == _frameIndex);
                string hold = LookData.IsMove(clip) || clip == ClipId.Jump ? $"#{index + 1}" : $"{frames[index].Hold}f";
                Ui.Text(hold, x + Thumb + 3, y + Thumb - 16, 16, Theme.TextDim);
            }

            if (frames.Count == 0)
            {
                string empty = clip switch
                {
                    ClipId.Idle => "empty: other clips fall back to idle",
                    ClipId.SwingFx => "empty: no swing effect",
                    ClipId.HitFx => "empty: the yellow spark is shown",
                    ClipId.Skill2 or ClipId.Skill3 => "empty: uses Skill 1's poses",
                    ClipId.Ego => "empty: uses Skill 3, then Skill 1",
                    ClipId.Skill2Fx or ClipId.Skill3Fx or ClipId.EgoFx => "empty: uses Skill 1's effect",
                    ClipId.Evade => "empty: uses jump, then walk",
                    _ => "empty: uses idle",
                };
                Ui.Text(empty, ClipsX + 12, top + 33, Font, Theme.TextDim);
            }
            else if (scroll + ThumbsVisible < frames.Count)
            {
                Ui.Text($"+{frames.Count - scroll - ThumbsVisible}", ClipsX + ClipsW - 40, top + 33, Font, Theme.TextDim);
            }
        }
    }

    private static string ClipHint(ClipId clip) => clip switch
    {
        ClipId.Idle => "loops",
        ClipId.Walk => "loops while walking",
        ClipId.Jump => "crouch, rise, fall",
        ClipId.Punch => "wind-up, hit, recovery",
        ClipId.Hit => "hit, Staggered, K.O.",
        ClipId.SwingFx => "on Skill 1's hitbox",
        ClipId.Skill2Fx => "on Skill 2's hitbox",
        ClipId.Skill3Fx => "on Skill 3's hitbox, per coin",
        ClipId.EgoFx => "on the E.G.O hitbox, per coin",
        ClipId.Skill2 or ClipId.Skill3 or ClipId.Ego => "wind-up, hit, recovery",
        ClipId.Guard => "guard, blockstun, counter",
        ClipId.Evade => "plays once per evade",
        _ => "where the hit lands",
    };

    private static string ClipTitle(ClipId clip) => clip switch
    {
        ClipId.Punch => "SKILL 1 (J)",
        ClipId.Skill2 => "SKILL 2 (K)",
        ClipId.Skill3 => "SKILL 3 (L)",
        ClipId.Ego => "E.G.O (I)",
        ClipId.Guard => "DEFENSE (S)",
        ClipId.Evade => "EVADE",
        ClipId.SwingFx => "SKILL 1 FX",
        ClipId.Skill2Fx => "SKILL 2 FX",
        ClipId.Skill3Fx => "SKILL 3 FX",
        ClipId.EgoFx => "E.G.O FX",
        ClipId.HitFx => "HIT FX",
        _ => LookData.ClipKey(clip).ToUpperInvariant(),
    };

    /// <summary>The move whose timing / hitbox a clip follows.</summary>
    private AttackData MoveFor(ClipId clip)
    {
        FighterStats stats = _stats(_slot);
        AttackData? move = clip switch
        {
            ClipId.Skill2 or ClipId.Skill2Fx => stats.Skill2,
            ClipId.Skill3 or ClipId.Skill3Fx => stats.Skill3,
            ClipId.Ego or ClipId.EgoFx => stats.Ego,
            _ => stats.Punch,
        };
        return move ?? stats.Punch;
    }

    private void DrawThumb(LookFrame frame, int x, int y, bool selected)
    {
        Raylib.DrawRectangle(x, y, Thumb, Thumb, new Color((byte)36, (byte)36, (byte)44, (byte)255));
        if (_looks.TryGetPicture(_slot, frame, out Texture2D texture, out DecodedImage image))
        {
            float s = MathF.Min((float)Thumb / image.Width, (float)Thumb / image.Height);
            float w = image.Width * s;
            float h = image.Height * s;
            Raylib.DrawTexturePro(texture, new Rectangle(0, 0, image.Width, image.Height),
                new Rectangle(x + ((Thumb - w) / 2), y + ((Thumb - h) / 2), w, h), Vector2.Zero, 0f, Color.White);
        }
        else if (_looks.PictureError(_slot, frame) is not null)
        {
            Ui.TextCentered("!", x + (Thumb / 2), y + 20, 30, Theme.Bad);
        }
        else
        {
            Ui.TextCentered("...", x + (Thumb / 2), y + 24, Font, Theme.TextDim);
        }

        Raylib.DrawRectangleLines(x - 1, y - 1, Thumb + 2, Thumb + 2, selected ? Theme.Accent : Theme.PanelBorder);
        if (selected)
        {
            Raylib.DrawRectangleLines(x - 2, y - 2, Thumb + 4, Thumb + 4, Theme.Accent);
        }
    }

    private void DrawPreview()
    {
        Raylib.DrawRectangle(PreviewX, PanelTop, PreviewW, PanelBottom - PanelTop, Theme.Panel);
        Raylib.DrawRectangleLines(PreviewX, PanelTop, PreviewW, PanelBottom - PanelTop, Theme.PanelBorder);
        Ui.Text("Preview at game size", PreviewX + 12, PanelTop + 8, Font, Theme.Accent);
        Ui.TextRight(LookData.IsEffect(Clip) ? "red = punch hitbox" : "blue = hurtbox", PreviewX + PreviewW - 12, PanelTop + 8, Font, LookData.IsEffect(Clip) ? Theme.Hitbox : Theme.Hurtbox);

        float feetX = PreviewX + (PreviewW / 2f);
        float feetY = PreviewFloorY;

        Raylib.BeginScissorMode(PreviewX + 1, PanelTop + 34, PreviewW - 2, PreviewFloorY + 30 - PanelTop - 34);
        Raylib.DrawRectangleGradientV(PreviewX + 1, PanelTop + 34, PreviewW - 2, PreviewFloorY - PanelTop - 34, Theme.Background, Theme.BackgroundHorizon);
        Raylib.DrawRectangle(PreviewX + 1, PreviewFloorY, PreviewW - 2, 30, Theme.Floor);
        Raylib.DrawLineEx(new Vector2(PreviewX, PreviewFloorY), new Vector2(PreviewX + PreviewW, PreviewFloorY), 3f, Theme.Accent);

        List<LookFrame> frames = Frames;
        bool drew;
        if (LookData.IsEffect(Clip))
        {
            drew = DrawEffectPreview(frames, feetX, feetY);
        }
        else
        {
            int shown = PreviewFrame(frames);
            drew = shown >= 0 && _looks.DrawPicture(_slot, frames[shown], feetX, feetY, 1, Color.White);
        }

        FighterStats stats = _stats(_slot);
        var hurt = new Rectangle(feetX - (stats.HurtboxWidth / 2f), feetY - stats.HurtboxHeight, stats.HurtboxWidth, stats.HurtboxHeight);
        Raylib.DrawRectangleLinesEx(hurt, 2f, new Color(Theme.Hurtbox.R, Theme.Hurtbox.G, Theme.Hurtbox.B, (byte)200));
        Raylib.DrawLine((int)feetX - 9, (int)feetY, (int)feetX + 9, (int)feetY, Color.White);
        Raylib.DrawLine((int)feetX, (int)feetY - 9, (int)feetX, (int)feetY + 9, Color.White);
        Raylib.EndScissorMode();

        if (!drew)
        {
            string note = frames.Count == 0 ? "This clip is empty." : _looks.IsLoading(_slot) ? "Loading..." : "Picture not loaded (see the status line).";
            Ui.TextCentered(note, (int)feetX, PanelTop + 120, Font, Theme.TextDim);
        }

        int infoY = PreviewFloorY + 40;
        if (_frameIndex < frames.Count)
        {
            LookFrame frame = frames[_frameIndex];
            Ui.Text(Ui.FitMiddle($"#{_frameIndex + 1}  {frame.Name}", Font, PreviewW - 24), PreviewX + 12, infoY, Font, Theme.Text);
            infoY += 22;
            string where = frame.Sheet is not null ? "sheet " + frame.Sheet : (frame.Root == LookFrame.RootCache ? "bundle " : "file ") + frame.File;
            Ui.Text(Ui.FitMiddle(where, Font, PreviewW - 24), PreviewX + 12, infoY, Font, Theme.TextDim);
            infoY += 22;
            if (frame.Note is string note)
            {
                Ui.Text(Ui.FitEnd(note, Font, PreviewW - 24), PreviewX + 12, infoY, Font, note.StartsWith("guessed", StringComparison.Ordinal) ? Theme.Warn : Theme.Good);
                infoY += 22;
            }

            if (_looks.PictureError(_slot, frame) is string error)
            {
                Ui.Text(Ui.FitEnd(error, Font, PreviewW - 24), PreviewX + 12, infoY, Font, Theme.Bad);
                infoY += 22;
            }
        }

        string size = Look.Scale > 0 ? $"size {Look.Scale:0.000}x" : $"size {_looks.EffectiveScale(_slot):0.000}x (auto)";
        string facing = (Look.ArtFacesLeft ? "faces left" : "faces right") + (Look.AnchorAtPivot ? ", stands on pivot" : ", stands on bottom");
        string offset = Look.OffsetX != 0 || Look.OffsetY != 0 ? $"   offset {Look.OffsetX:0},{Look.OffsetY:0}" : string.Empty;
        Ui.Text(Ui.FitEnd($"{size}   {facing}{offset}", Font, PreviewW - 24), PreviewX + 12, infoY, Font, Theme.TextDim);
    }

    /// <summary>
    /// Effect preview: the idle pose with the effect where it appears in a match (swing: on the punch's
    /// hitbox; hit: just in front, where the punch would land), replaying every second.
    /// </summary>
    private bool DrawEffectPreview(List<LookFrame> frames, float feetX, float feetY)
    {
        if (Look.Frames(ClipId.Idle).FirstOrDefault() is LookFrame idle)
        {
            _looks.DrawPicture(_slot, idle, feetX, feetY, 1, new Color((byte)255, (byte)255, (byte)255, (byte)150));
        }

        if (frames.Count == 0)
        {
            return false;
        }

        AttackData punch = MoveFor(Clip);
        float x = feetX + punch.HitboxOffsetX + (punch.HitboxWidth / 2f);
        float y = feetY + punch.HitboxOffsetY + (punch.HitboxHeight / 2f);
        if (Clip == ClipId.HitFx)
        {
            x = feetX + punch.HitboxOffsetX + punch.HitboxWidth;
        }

        bool still = Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.Right);
        int total = frames.Sum(f => Math.Max(1, f.Hold));
        (int Frame, float Alpha)? at = still ? (Math.Min(_frameIndex, frames.Count - 1), 1f) : EffectTiming.At(frames, (int)(_previewTick % (total + 30)));

        var box = new Rectangle(feetX + punch.HitboxOffsetX, feetY + punch.HitboxOffsetY, punch.HitboxWidth, punch.HitboxHeight);
        Raylib.DrawRectangleLinesEx(box, 1f, new Color(Theme.Hitbox.R, Theme.Hitbox.G, Theme.Hitbox.B, (byte)150));

        return at is not (int index, float alpha) || _looks.DrawEffect(_slot, frames[index], x, y, 1, alpha);
    }

    /// <summary>Which frame the preview shows now: the selected one while editing, else the clip playing as in a match.</summary>
    private int PreviewFrame(List<LookFrame> frames)
    {
        if (frames.Count == 0)
        {
            return -1;
        }

        // Show the selected frame still while keys are held; animate otherwise.
        if (Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.Equal) || Raylib.IsKeyDown(KeyboardKey.Minus))
        {
            return Math.Min(_frameIndex, frames.Count - 1);
        }

        int t = (int)(_previewTick % 100000);
        switch (Clip)
        {
            case ClipId.Punch or ClipId.Skill2 or ClipId.Skill3 or ClipId.Ego:
            {
                AttackData punch = MoveFor(Clip);
                int stateFrame = (t % (punch.TotalFrames + 24)) + 1;
                return stateFrame > punch.TotalFrames ? 0 : ClipPicker.PunchFrame(frames.Count, punch, stateFrame);
            }

            case ClipId.Jump:
            {
                int phase = t % 60;
                return phase < 10
                    ? ClipPicker.JumpFrame(frames.Count, FighterState.JumpSquat, 0f)
                    : ClipPicker.JumpFrame(frames.Count, FighterState.Airborne, phase < 35 ? -1f : 1f);
            }

            case ClipId.Hit:
            {
                int total = frames.Sum(f => Math.Max(1, f.Hold));
                return ClipPicker.Clamped(frames, (t % (total + 30)) + 1);
            }

            default:
                return ClipPicker.Looping(frames, t + 1);
        }
    }
}
