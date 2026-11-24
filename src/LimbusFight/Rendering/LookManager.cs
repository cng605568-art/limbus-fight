using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using Raylib_cs;

namespace LimbusFight.Rendering;

/// <summary>
/// Owns both fighters' looks: the saved look files, the pictures loaded from the player's own
/// Limbus Company files (on a background thread) and their GPU textures (main thread only).
/// Edits are saved at once and only new pictures are loaded.
/// </summary>
public sealed class LookManager : IDisposable
{
    private sealed class Slot
    {
        public LookData Data = new();
        public readonly Dictionary<string, LoadedPicture> Pictures = new();
        public readonly Dictionary<string, Texture2D> Textures = new();
        public Task<Dictionary<string, LoadedPicture>>? LoadTask;
        public bool ReloadWanted;
        public bool RetryFailed;
        public volatile int LoadDone;
        public volatile int LoadTotal;
        public string? Problem;
        public LookData? Undo;
    }

    private readonly Dictionary<LookSlot, Slot> _slots = new()
    {
        [LookSlot.Player1] = new Slot(),
        [LookSlot.Dummy] = new Slot(),
    };

    private readonly UnityAssetReader _reader = new();
    private readonly object _readerGate = new();
    private SteamCheckResult _steam;

    public LookManager(SteamCheckResult steam)
    {
        _steam = steam;
        foreach ((LookSlot slot, Slot state) in _slots)
        {
            state.Data = LookStore.Load(slot, out string? problem);
            state.Problem = problem;
            state.ReloadWanted = !state.Data.IsEmpty;
        }
    }

    public LookData Get(LookSlot slot) => _slots[slot].Data;

    /// <summary>True while pictures for this slot are being loaded.</summary>
    public bool IsLoading(LookSlot slot) => _slots[slot].LoadTask is not null;

    /// <summary>One line for the arena / editor: loading progress or the first problem, else null.</summary>
    public string? Status(LookSlot slot)
    {
        Slot s = _slots[slot];
        if (s.LoadTask is not null)
        {
            return $"loading pictures {s.LoadDone} / {Math.Max(s.LoadTotal, 1)}...";
        }

        if (s.Problem is not null)
        {
            return s.Problem;
        }

        int failed = s.Data.AllFrames.Select(f => f.Key).Distinct().Count(k => s.Pictures.TryGetValue(k, out LoadedPicture? p) && p.Image is null);
        return failed > 0 ? $"{failed} picture(s) could not be loaded: {FirstError(s)}" : null;
    }

    /// <summary>Applies an edit, saves the look file and loads any pictures the edit added.</summary>
    public void Modify(LookSlot slot, Action<LookData> edit)
    {
        Slot s = _slots[slot];
        edit(s.Data);
        s.Data.Sanitise();
        s.Problem = LookStore.Save(slot, s.Data);
        s.ReloadWanted = true;
    }

    /// <summary>Replaces a slot's whole look (auto look), keeping the previous one for <see cref="UndoReplace"/>.</summary>
    public void Replace(LookSlot slot, LookData look)
    {
        Slot s = _slots[slot];
        s.Undo = LookStore.Deserialize(LookStore.Serialize(s.Data));
        look.Sanitise();
        s.Data = look;
        s.Problem = LookStore.Save(slot, look);
        s.ReloadWanted = true;
    }

    /// <summary>Brings back the look from before the last <see cref="Replace"/>. Returns false if there is none.</summary>
    public bool UndoReplace(LookSlot slot)
    {
        Slot s = _slots[slot];
        if (s.Undo is null)
        {
            return false;
        }

        s.Data = s.Undo;
        s.Undo = null;
        s.Problem = LookStore.Save(slot, s.Data);
        s.ReloadWanted = true;
        return true;
    }

    /// <summary>Re-reads Steam info and retries pictures that failed (F5).</summary>
    public void Retry(SteamCheckResult steam)
    {
        _steam = steam;
        foreach (Slot s in _slots.Values)
        {
            s.RetryFailed = true;
            s.ReloadWanted = true;
            s.Problem = null;
        }
    }

    /// <summary>Main thread, once per render frame: finishes loads, uploads textures, starts new loads.</summary>
    public void Update()
    {
        foreach ((LookSlot slot, Slot s) in _slots)
        {
            if (s.LoadTask is { IsCompleted: true } task)
            {
                s.LoadTask = null;
                if (task.IsFaulted)
                {
                    s.Problem = task.Exception?.GetBaseException().Message ?? "Could not load pictures.";
                }
                else
                {
                    foreach ((string key, LoadedPicture picture) in task.Result)
                    {
                        AddPicture(s, key, picture);
                    }
                }
            }

            if (s.ReloadWanted && s.LoadTask is null)
            {
                StartLoad(slot, s);
            }
        }
    }

    /// <summary>The loaded picture for a frame (texture + size + pivot), if it is ready.</summary>
    public bool TryGetPicture(LookSlot slot, LookFrame frame, out Texture2D texture, out DecodedImage image)
    {
        Slot s = _slots[slot];
        texture = default;
        image = null!;
        if (s.Pictures.TryGetValue(frame.Key, out LoadedPicture? picture) && picture.Image is DecodedImage loaded
            && s.Textures.TryGetValue(frame.Key, out texture))
        {
            image = loaded;
            return true;
        }

        return false;
    }

    /// <summary>Why a frame's picture is missing (null if it loaded or is still loading).</summary>
    public string? PictureError(LookSlot slot, LookFrame frame) =>
        _slots[slot].Pictures.TryGetValue(frame.Key, out LoadedPicture? p) ? p.Error : null;

    /// <summary>Scale actually used for drawing: the saved one, or automatic from the first idle frame.</summary>
    public float EffectiveScale(LookSlot slot)
    {
        LookData data = _slots[slot].Data;
        if (data.Scale > 0f)
        {
            return data.Scale;
        }

        LookFrame? reference = data.Frames(ClipId.Idle).FirstOrDefault() ?? data.PoseFrames.FirstOrDefault() ?? data.AllFrames.FirstOrDefault();
        if (reference is not null && TryGetPicture(slot, reference, out _, out DecodedImage image) && image.Height > 0)
        {
            return Math.Clamp(LookData.AutoHeight / image.Height, 0.05f, 4f);
        }

        return 0.5f;
    }

    /// <summary>
    /// Draws the fighter with its look. Returns false when the look has nothing ready to show,
    /// so the caller can draw the test box instead.
    /// </summary>
    public bool DrawFighter(LookSlot slot, Fighter fighter, float shake, bool flash)
    {
        LookData data = _slots[slot].Data;
        if (ClipPicker.Pick(fighter, data) is not (ClipId clip, int index))
        {
            return false;
        }

        Color tint = flash ? new Color((byte)255, (byte)160, (byte)160, (byte)255) : Color.White;
        LookFrame frame = data.Frames(clip)[index];
        if (DrawPicture(slot, frame, fighter.X + shake, fighter.Y, fighter.Facing, tint))
        {
            return true;
        }

        // That picture is still loading or failed: show any pose of the look that is ready.
        foreach (LookFrame other in data.Frames(ClipId.Idle).Concat(data.PoseFrames))
        {
            if (DrawPicture(slot, other, fighter.X + shake, fighter.Y, fighter.Facing, tint))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Draws one frame standing on (feetX, feetY), facing +1 (right) or -1 (left), exactly as in a match.</summary>
    public bool DrawPicture(LookSlot slot, LookFrame frame, float feetX, float feetY, int facing, Color tint, float extraScale = 1f)
    {
        if (!TryGetPicture(slot, frame, out Texture2D texture, out DecodedImage image))
        {
            return false;
        }

        LookData data = _slots[slot].Data;
        float scale = EffectiveScale(slot) * extraScale;
        (float px, float pivotY) = image.Pivot ?? (image.Width / 2f, image.Height);
        float py = data.AnchorAtPivot ? pivotY : image.Height;
        bool mirror = (facing < 0) != data.ArtFacesLeft;

        float w = image.Width * scale;
        float h = image.Height * scale;
        float offsetX = data.OffsetX * extraScale * (facing < 0 ? -1 : 1);
        float left = mirror ? feetX - ((image.Width - px) * scale) : feetX - (px * scale);
        float top = feetY - (py * scale) + (data.OffsetY * extraScale);

        var source = new Rectangle(0, 0, mirror ? -image.Width : image.Width, image.Height);
        Raylib.DrawTexturePro(texture, source, new Rectangle(left + offsetX, top, w, h), Vector2.Zero, 0f, tint);
        return true;
    }

    /// <summary>
    /// Draws an effect frame with its pivot on (centerX, centerY), at the look's size, mirrored when
    /// facing left. Returns false if the picture is not ready.
    /// </summary>
    public bool DrawEffect(LookSlot slot, LookFrame frame, float centerX, float centerY, int facing, float alpha, float extraScale = 1f)
    {
        if (!TryGetPicture(slot, frame, out Texture2D texture, out DecodedImage image))
        {
            return false;
        }

        LookData data = _slots[slot].Data;
        float scale = EffectiveScale(slot) * extraScale;
        (float px, float py) = image.Pivot ?? (image.Width / 2f, image.Height / 2f);
        bool mirror = (facing < 0) != data.ArtFacesLeft;
        float left = mirror ? centerX - ((image.Width - px) * scale) : centerX - (px * scale);
        float top = centerY - (py * scale);
        var source = new Rectangle(0, 0, mirror ? -image.Width : image.Width, image.Height);
        var tint = new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp((int)(alpha * 255), 0, 255));
        Raylib.DrawTexturePro(texture, source, new Rectangle(left, top, image.Width * scale, image.Height * scale), Vector2.Zero, 0f, tint);
        return true;
    }

    public void Dispose()
    {
        foreach (Slot s in _slots.Values)
        {
            foreach (Texture2D texture in s.Textures.Values)
            {
                Raylib.UnloadTexture(texture);
            }

            s.Textures.Clear();
        }

        lock (_readerGate)
        {
            _reader.Dispose();
        }
    }

    private void StartLoad(LookSlot slot, Slot s)
    {
        s.ReloadWanted = false;
        bool retry = s.RetryFailed;
        s.RetryFailed = false;

        List<LookFrame> missing = s.Data.AllFrames
            .Where(f => !s.Pictures.TryGetValue(f.Key, out LoadedPicture? p) || (retry && p.Image is null))
            .GroupBy(f => f.Key)
            .Select(g => g.First().Clone())
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        SteamCheckResult steam = _steam;
        s.LoadDone = 0;
        s.LoadTotal = missing.Count;
        var progress = new Progress(p =>
        {
            s.LoadDone = p.Done;
            s.LoadTotal = p.Total;
        });

        s.LoadTask = Task.Run(() =>
        {
            lock (_readerGate)
            {
                return LookLoader.Load(missing, steam, _reader, progress);
            }
        });
    }

    private static void AddPicture(Slot s, string key, LoadedPicture picture)
    {
        if (s.Textures.Remove(key, out Texture2D old))
        {
            Raylib.UnloadTexture(old);
        }

        if (picture.Image is DecodedImage image && image.Rgba.Length >= image.Width * image.Height * 4)
        {
            s.Textures[key] = Upload(image);
            // The pixels now live on the GPU; keep only size, pivot and names.
            s.Pictures[key] = picture with { Image = image with { Rgba = Array.Empty<byte>() } };
        }
        else
        {
            s.Pictures[key] = picture;
        }
    }

    private static unsafe Texture2D Upload(DecodedImage decoded)
    {
        fixed (byte* pixels = decoded.Rgba)
        {
            var image = new Image { Data = pixels, Width = decoded.Width, Height = decoded.Height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
            return texture;
        }
    }

    private static string FirstError(Slot s) =>
        s.Pictures.Values.FirstOrDefault(p => p.Image is null && p.Error is not null)?.Error ?? "unknown error";

    private sealed class Progress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }
}
