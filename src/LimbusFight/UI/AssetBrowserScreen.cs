using System.Diagnostics;
using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Core;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using Raylib_cs;

namespace LimbusFight.UI;

/// <summary>
/// F3 screen: browse Limbus Company's own Unity files (read-only), preview any Texture2D,
/// save it as PNG to a local folder, and write the asset report.
/// Slow work (scanning, opening bundles, decoding) runs on a background thread; this class
/// only touches Raylib on the main thread.
/// </summary>
public sealed class AssetBrowserScreen : IDisposable
{
    private const int Font = 20;
    private const int RowHeight = 22;
    private const int PanelTop = 80;
    private const int ListTop = 116;
    private const int ListBottom = 688;
    private const int VisibleRows = (ListBottom - ListTop) / RowHeight;

    private const int FilesX = 16;
    private const int FilesW = 470;
    private const int TexX = 498;
    private const int TexW = 330;
    private const int PreviewX = 840;
    private const int PreviewW = 424;
    private const int PreviewTop = ListTop;
    private const int PreviewH = 400;

    private enum Pane
    {
        Files,
        Pictures,
    }

    /// <summary>One row of the middle list: a sprite (single picture) or a texture (whole sheet).</summary>
    private sealed record PictureEntry(SpriteInfo? Sprite, TextureInfo? Texture)
    {
        public string Name => Sprite?.Name ?? Texture?.Name ?? string.Empty;

        public int Width => Sprite?.Width ?? Texture?.Width ?? 0;

        public int Height => Sprite?.Height ?? Texture?.Height ?? 0;
    }

    private readonly UnityAssetReader _reader = new();
    private readonly UnityAssetReader _indexReader = new();
    private SteamCheckResult? _steam;
    private AssetCache? _cache;

    // ---- Name index (search inside hash-named bundles) -----------------------------------------
    private Task? _indexTask;
    private CancellationTokenSource? _indexCancel;
    private bool _indexPending;
    private volatile int _indexDone;
    private volatile int _indexTotal;
    private int _indexDoneAtLastFilter;

    // ---- Scan -------------------------------------------------------------------------------
    private Task<AssetScanResult>? _scanTask;
    private AssetScanResult? _scan;
    private volatile int _scanCount;
    private string? _scanError;

    // ---- File list --------------------------------------------------------------------------
    private List<ScannedFile> _all = new();
    private List<ScannedFile> _visible = new();
    private string _filter = string.Empty;
    private int _fileIndex;
    private int _fileScroll;

    // ---- Contents of the opened file ----------------------------------------------------------
    private Task<AssetFileContents>? _contentsTask;
    private ScannedFile? _contentsSource;
    private ScannedFile? _wantedSource;
    private AssetFileContents? _contents;
    private List<PictureEntry> _pictures = new();
    private List<PictureEntry> _textureView = new();
    private int _texIndex;
    private int _texScroll;

    // ---- Preview ----------------------------------------------------------------------------
    private Task<DecodedImage>? _decodeTask;
    private PictureEntry? _decodeFor;
    private PictureEntry? _wantedTexture;
    private DecodedImage? _image;
    private Texture2D? _gpuTexture;
    private string? _previewError;

    // ---- Report -----------------------------------------------------------------------------
    private Task<string>? _reportTask;
    private volatile string? _reportProgress;

    private Pane _pane = Pane.Files;
    private string? _status;
    private Color _statusColor = Theme.Text;
    private double _statusUntil;

    /// <summary>Fighter looks; Ctrl+1..5 adds the selected picture to one (set by Program).</summary>
    public LookManager? Looks { get; set; }

    /// <summary>Where Ctrl+Q adds a picture: the slot and clip selected in the F4 look editor (set by Program).</summary>
    public Func<(LookSlot Slot, ClipId Clip)>? SelectedClip { get; set; }

    /// <summary>Fighter files; an auto look names a still-unnamed fighter after the detected Sinner.</summary>
    public RosterManager? Roster { get; set; }

    /// <summary>Folder where Ctrl+S saves PNGs: %LOCALAPPDATA%\LimbusFight\exports.</summary>
    public static string ExportFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimbusFight", "exports");

    /// <summary>Called when the screen opens; starts the first scan.</summary>
    public void Open(SteamCheckResult steam)
    {
        _steam = steam;
        if (_scan is null && _scanTask is null)
        {
            StartScan();
        }
    }

    /// <summary>Re-scans the folders (F5).</summary>
    public void Rescan(SteamCheckResult steam)
    {
        _steam = steam;
        StartScan();
    }

    public void Update(KeyboardSampler keys)
    {
        PollBackgroundWork();
        HandleTyping();
        HandleKeys(keys);
        HandleMouse();
    }

    public void Draw(int width, int height)
    {
        Raylib.ClearBackground(Theme.Background);

        Ui.Text("ASSET BROWSER", 20, 14, 30, Theme.Accent);
        Ui.Text("Limbus Company's own files, read-only", 20 + Ui.Measure("ASSET BROWSER", 30) + 18, 22, Font, Theme.TextDim);
        Ui.TextRight("F3  back to the arena", width - 20, 22, Font, Theme.TextDim);
        DrawStatusLine(width);

        DrawFilesPane();
        DrawTexturesPane();
        DrawPreviewPane();

        Ui.TextCentered(
            "Type to filter   Enter open   Tab switch list   Ctrl+S save PNG   Ctrl+O exports   Ctrl+R report   F5 rescan",
            width / 2, height - 26, Font, Theme.TextDim);
    }

    public void Dispose()
    {
        UnloadGpuTexture();
        _indexCancel?.Cancel();
        try
        {
            _indexTask?.Wait(TimeSpan.FromSeconds(3)); // lets the indexer save what it has
        }
        catch (AggregateException)
        {
            // indexing errors are not fatal on exit
        }

        _indexReader.Dispose();
        _reader.Dispose();
    }

    // =========================================================================================
    //  Background work
    // =========================================================================================

    private void StartScan()
    {
        if (_steam is null || _scanTask is not null)
        {
            return;
        }

        _scan = null;
        _scanError = null;
        _scanCount = 0;
        _indexCancel?.Cancel();
        IReadOnlyList<AssetRoot> roots = LimbusAssetRoots.Find(_steam);
        var progress = new InlineProgress<int>(n => _scanCount = n);
        _scanTask = Task.Run(() =>
        {
            _cache ??= AssetCache.Load();
            return AssetScanner.Scan(roots, _cache, progress);
        });
    }

    /// <summary>Reads the names inside every bundle not indexed yet (first run only; later runs use the cache).</summary>
    private void StartIndex()
    {
        if (_scan is null || _cache is null || _scan.GameWasRunning)
        {
            return;
        }

        if (_indexTask is not null)
        {
            _indexPending = true; // restart once the running pass has stopped
            return;
        }

        _indexPending = false;
        _indexCancel = new CancellationTokenSource();
        CancellationToken cancel = _indexCancel.Token;
        AssetCache cache = _cache;
        List<ScannedFile> files = _all.ToList();
        var progress = new InlineProgress<(int Done, int Total)>(p =>
        {
            _indexDone = p.Done;
            _indexTotal = p.Total;
        });

        _indexDone = 0;
        _indexTotal = files.Count(f => cache.GetNames(f.FullPath) is null);
        _indexDoneAtLastFilter = 0;

        _indexTask = Task.Run(() =>
        {
            try
            {
                AssetIndexer.IndexMissing(files, cache, _indexReader, progress, cancel);
            }
            finally
            {
                try
                {
                    cache.Save();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // the cache is an optimisation only
                }
            }
        });
    }

    private void PollBackgroundWork()
    {
        if (_scanTask is { IsCompleted: true } scanTask)
        {
            _scanTask = null;
            if (scanTask.IsFaulted)
            {
                _scanError = scanTask.Exception?.GetBaseException().Message ?? "Scan failed.";
            }
            else
            {
                _scan = scanTask.Result;
                SortFiles(_scan.Browsable);
                ApplyFilter();
                StartIndex();
            }
        }

        if (_indexTask is { IsCompleted: true } indexTask)
        {
            _indexTask = null;
            _ = indexTask.Exception;
            if (_indexPending)
            {
                StartIndex();
            }
            else if (_scan is not null)
            {
                SortFiles(_all);
                ApplyFilter();
                int named = _all.Count(f => _cache?.GetNames(f.FullPath) is { Length: > 0 });
                SetStatus($"Search ready: names read from {named} files. Type a name, e.g. a Sinner or a boss.", Theme.Good);
            }
        }
        else if (_indexTask is not null && _filter.Length > 0 && _indexDone - _indexDoneAtLastFilter >= 50)
        {
            _indexDoneAtLastFilter = _indexDone;
            ApplyFilter(); // results grow while the first index runs
        }

        if (_contentsTask is { IsCompleted: true } contentsTask)
        {
            _contentsTask = null;
            _contents = contentsTask.IsFaulted
                ? new AssetFileContents(_contentsSource?.FullPath ?? string.Empty, null, false, new Dictionary<string, int>(), Array.Empty<TextureInfo>(),
                    Array.Empty<SpriteInfo>(), new[] { contentsTask.Exception?.GetBaseException().Message ?? "Could not open file." })
                : contentsTask.Result;
            _pictures = _contents.Sprites.Select(s => new PictureEntry(s, null))
                .Concat(_contents.Textures.Select(t => new PictureEntry(null, t)))
                .ToList();
            _texIndex = 0;
            _texScroll = 0;
            UpdateTextureView();

            if (_wantedSource is not null && _wantedSource != _contentsSource)
            {
                RequestContents(_wantedSource);
            }
            else if (_textureView.Count > 0)
            {
                _pane = Pane.Pictures;
                RequestTexture(_textureView[0]);
            }
        }

        if (_decodeTask is { IsCompleted: true } decodeTask)
        {
            _decodeTask = null;
            bool stillWanted = _wantedTexture is not null && _wantedTexture == _decodeFor;

            if (!stillWanted)
            {
                // The selection moved on while decoding: drop this result, decode the latest pick.
                _ = decodeTask.Exception;
                if (_wantedTexture is not null)
                {
                    RequestTexture(_wantedTexture);
                }
            }
            else if (decodeTask.IsFaulted)
            {
                _previewError = decodeTask.Exception?.GetBaseException().Message ?? "Could not decode this picture.";
                _image = null;
                UnloadGpuTexture();
            }
            else
            {
                _previewError = null;
                _image = decodeTask.Result;
                UnloadGpuTexture();
                _gpuTexture = Upload(_image);
            }
        }

        if (_reportTask is { IsCompleted: true } reportTask)
        {
            _reportTask = null;
            _reportProgress = null;
            if (reportTask.IsFaulted)
            {
                SetStatus("Report failed: " + reportTask.Exception?.GetBaseException().Message, Theme.Bad);
            }
            else
            {
                SetStatus("Report saved: " + LimbusAssetRoots.Redact(reportTask.Result) + "  (send this file to Claude)", Theme.Good);
                RevealInExplorer(reportTask.Result);
            }
        }
    }

    private void RequestContents(ScannedFile source)
    {
        _wantedSource = source;
        if (_contentsTask is not null)
        {
            return; // picked up when the running task finishes
        }

        _contentsSource = source;
        _contents = null;
        _pictures = new List<PictureEntry>();
        _image = null;
        _previewError = null;
        _wantedTexture = null;
        UnloadGpuTexture();
        _contentsTask = Task.Run(() => _reader.ReadContents(source));
    }

    private void RequestTexture(PictureEntry entry)
    {
        _wantedTexture = entry;
        if (_decodeTask is not null || _contentsSource is null)
        {
            return;
        }

        ScannedFile source = _contentsSource;
        _decodeFor = entry;
        _decodeTask = Task.Run(() => entry.Sprite is SpriteInfo sprite
            ? _reader.DecodeSprite(source, sprite)
            : _reader.DecodeTexture(source, entry.Texture!));
    }

    private void StartReport()
    {
        if (_reportTask is not null || _steam is null)
        {
            return;
        }

        if (_scan is null)
        {
            SetStatus("Wait for the scan to finish first.", Theme.Warn);
            return;
        }

        SteamCheckResult steam = _steam;
        AssetScanResult scan = _scan;
        AssetCache? cache = _cache;
        var progress = new InlineProgress<string>(text => _reportProgress = text);
        _reportProgress = "Writing report...";
        _reportTask = Task.Run(() => AssetReport.Write(AssetReport.Build(steam, scan, _reader, cache, progress)));
    }

    // =========================================================================================
    //  Input
    // =========================================================================================

    private void HandleTyping()
    {
        bool changed = false;
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        int codepoint;
        while ((codepoint = Raylib.GetCharPressed()) != 0)
        {
            if (!ctrl && codepoint is >= 32 and < 127 && _filter.Length < 60)
            {
                _filter += (char)codepoint;
                changed = true;
            }
        }

        if (_filter.Length > 0 && Repeat(KeyboardKey.Backspace))
        {
            _filter = _filter[..^1];
            changed = true;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Delete) && _filter.Length > 0)
        {
            _filter = string.Empty;
            changed = true;
        }

        if (changed)
        {
            _pane = Pane.Files;
            ApplyFilter();
        }
    }

    private void HandleKeys(KeyboardSampler keys)
    {
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);

        if (ctrl && keys.WasPressed(KeyboardKey.S))
        {
            SavePng();
        }

        if (ctrl && keys.WasPressed(KeyboardKey.O))
        {
            OpenFolder(ExportFolder);
        }

        if (ctrl && keys.WasPressed(KeyboardKey.R))
        {
            StartReport();
        }

        if (ctrl)
        {
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            if (keys.WasPressed(KeyboardKey.A))
            {
                AutoLookFromFile(shift ? LookSlot.Dummy : LookSlot.Player1);
            }

            if (keys.WasPressed(KeyboardKey.Q) && SelectedClip?.Invoke() is (LookSlot qSlot, ClipId qClip))
            {
                AddToLook(qSlot, qClip);
            }

            if (keys.WasPressed(KeyboardKey.Z))
            {
                LookSlot slot = shift ? LookSlot.Dummy : LookSlot.Player1;
                bool undone = Looks?.UndoReplace(slot) ?? false;
                SetStatus(undone ? $"{(slot == LookSlot.Player1 ? "P1" : "Dummy")} look restored to how it was before the auto look." : "Nothing to undo.", undone ? Theme.Good : Theme.Warn);
            }

            KeyboardKey[] digits = { KeyboardKey.One, KeyboardKey.Two, KeyboardKey.Three, KeyboardKey.Four, KeyboardKey.Five, KeyboardKey.Six, KeyboardKey.Seven };
            for (int i = 0; i < digits.Length; i++)
            {
                if (keys.WasPressed(digits[i]))
                {
                    AddToLook(shift ? LookSlot.Dummy : LookSlot.Player1, (ClipId)i);
                }
            }
        }

        if (keys.WasPressed(KeyboardKey.Tab) || keys.WasPressed(KeyboardKey.Right) || keys.WasPressed(KeyboardKey.Left))
        {
            bool toTextures = keys.WasPressed(KeyboardKey.Right) || (keys.WasPressed(KeyboardKey.Tab) && _pane == Pane.Files);
            _pane = toTextures && _textureView.Count > 0 ? Pane.Pictures : Pane.Files;
        }

        int move = 0;
        if (Repeat(KeyboardKey.Down)) move = 1;
        if (Repeat(KeyboardKey.Up)) move = -1;
        if (Repeat(KeyboardKey.PageDown)) move = VisibleRows;
        if (Repeat(KeyboardKey.PageUp)) move = -VisibleRows;
        if (keys.WasPressed(KeyboardKey.Home)) move = int.MinValue / 2;
        if (keys.WasPressed(KeyboardKey.End)) move = int.MaxValue / 2;

        if (_pane == Pane.Files)
        {
            if (move != 0 && _visible.Count > 0)
            {
                _fileIndex = Math.Clamp((int)Math.Clamp((long)_fileIndex + move, int.MinValue, int.MaxValue), 0, _visible.Count - 1);
                _fileScroll = KeepVisible(_fileIndex, _fileScroll);
            }

            if ((keys.WasPressed(KeyboardKey.Enter) || keys.WasPressed(KeyboardKey.KpEnter)) && _fileIndex < _visible.Count)
            {
                RequestContents(_visible[_fileIndex]);
            }
        }
        else if (_textureView.Count > 0 && move != 0)
        {
            _texIndex = Math.Clamp((int)Math.Clamp((long)_texIndex + move, int.MinValue, int.MaxValue), 0, _textureView.Count - 1);
            _texScroll = KeepVisible(_texIndex, _texScroll);
            RequestTexture(_textureView[_texIndex]);
        }
    }

    private void HandleMouse()
    {
        Vector2 mouse = Viewport.MouseOnCanvas();
        float wheel = Raylib.GetMouseWheelMove();
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

        bool overFiles = mouse.X >= FilesX && mouse.X < FilesX + FilesW && mouse.Y >= ListTop && mouse.Y < ListBottom;
        bool overTextures = mouse.X >= TexX && mouse.X < TexX + TexW && mouse.Y >= ListTop && mouse.Y < ListBottom;

        if (wheel != 0)
        {
            int delta = -(int)MathF.Round(wheel) * 3;
            if (overFiles)
            {
                _fileScroll = Math.Clamp(_fileScroll + delta, 0, Math.Max(0, _visible.Count - VisibleRows));
            }
            else if (overTextures)
            {
                _texScroll = Math.Clamp(_texScroll + delta, 0, Math.Max(0, _textureView.Count - VisibleRows));
            }
        }

        if (!click)
        {
            return;
        }

        int row = (int)((mouse.Y - ListTop) / RowHeight);
        if (overFiles && _fileScroll + row < _visible.Count)
        {
            _pane = Pane.Files;
            _fileIndex = _fileScroll + row;
            RequestContents(_visible[_fileIndex]);
        }
        else if (overTextures && _texScroll + row < _textureView.Count)
        {
            _pane = Pane.Pictures;
            _texIndex = _texScroll + row;
            RequestTexture(_textureView[_texIndex]);
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static bool Repeat(KeyboardKey key) => Raylib.IsKeyPressed(key) || Raylib.IsKeyPressedRepeat(key);

    private static int KeepVisible(int index, int scroll)
    {
        if (index < scroll)
        {
            return index;
        }

        if (index >= scroll + VisibleRows)
        {
            return index - VisibleRows + 1;
        }

        return scroll;
    }

    private string[] FilterWords => _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string text, string[] words) =>
        words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>A file matches if its name, or the name of any texture / sprite / sound inside it, contains every word.</summary>
    private void ApplyFilter()
    {
        string[] words = FilterWords;
        _visible = words.Length == 0
            ? _all
            : _all.Where(f => Matches(f.DisplayName, words) || (_cache?.GetNames(f.FullPath)?.Any(n => Matches(n.Name, words)) ?? false)).ToList();
        _fileIndex = Math.Clamp(_fileIndex, 0, Math.Max(0, _visible.Count - 1));
        _fileScroll = Math.Clamp(KeepVisible(_fileIndex, _fileScroll), 0, Math.Max(0, _visible.Count - VisibleRows));
        UpdateTextureView();
    }

    /// <summary>When the filter matches some pictures of the open file, list only those.</summary>
    private void UpdateTextureView()
    {
        PictureEntry? selected = _texIndex < _textureView.Count ? _textureView[_texIndex] : null;
        string[] words = FilterWords;
        List<PictureEntry> matching = words.Length == 0 ? new List<PictureEntry>() : _pictures.Where(p => Matches(p.Name, words)).ToList();
        _textureView = matching.Count > 0 ? matching : _pictures.ToList();

        int keep = selected is null ? -1 : _textureView.IndexOf(selected);
        _texIndex = keep >= 0 ? keep : 0;
        _texScroll = Math.Clamp(KeepVisible(_texIndex, _texScroll), 0, Math.Max(0, _textureView.Count - VisibleRows));
    }

    /// <summary>Named files first, alphabetically by their main texture / sprite / sound name; hash-only files last.</summary>
    private void SortFiles(IEnumerable<ScannedFile> files)
    {
        _all = files
            .Select(f => (File: f, Name: BestName(f)))
            .OrderBy(x => x.Name is null ? 1 : 0)
            .ThenBy(x => x.Name ?? x.File.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.File)
            .ToList();
    }

    private string? BestName(ScannedFile file)
    {
        AssetName[]? names = _cache?.GetNames(file.FullPath);
        if (names is not { Length: > 0 })
        {
            return null;
        }

        foreach (NameKind kind in new[] { NameKind.Texture, NameKind.Sprite, NameKind.Audio })
        {
            foreach (AssetName name in names)
            {
                if (name.Kind == kind)
                {
                    return name.Name;
                }
            }
        }

        return null;
    }

    /// <summary>What a file row shows: a matching (or the main) object name, else the bundle's hash name.</summary>
    private string Label(ScannedFile file, string[] words)
    {
        AssetName[]? names = _cache?.GetNames(file.FullPath);
        if (names is not { Length: > 0 })
        {
            return file.DisplayName;
        }

        string? match = words.Length == 0 ? null : names.FirstOrDefault(n => Matches(n.Name, words)).Name;
        string main = match ?? BestName(file) ?? file.DisplayName;
        return names.Length > 1 ? $"{main}  +{names.Length - 1}" : main;
    }

    // =========================================================================================
    //  Saving
    // =========================================================================================

    private unsafe void SavePng()
    {
        if (_image is null)
        {
            SetStatus("Select a picture first.", Theme.Warn);
            return;
        }

        try
        {
            Directory.CreateDirectory(ExportFolder);
            string safeName = string.Concat(_image.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(ExportFolder, (safeName.Length > 0 ? safeName : "picture") + ".png");

            bool ok;
            fixed (byte* pixels = _image.Rgba)
            {
                var image = new Image { Data = pixels, Width = _image.Width, Height = _image.Height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
                ok = Raylib.ExportImage(image, path);
            }

            SetStatus(ok ? "Saved " + LimbusAssetRoots.Redact(path) : "Could not save PNG.", ok ? Theme.Good : Theme.Bad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save PNG: " + ex.Message, Theme.Bad);
        }
    }

    /// <summary>
    /// Ctrl+A: builds a whole look from the open file's sprites, sorting them into idle, walk, jump,
    /// punch and hit by their names (or shapes). Size, flip and anchor settings are kept.
    /// </summary>
    private void AutoLookFromFile(LookSlot slot)
    {
        if (Looks is null || _contentsSource is null || _contents is null)
        {
            SetStatus("Open a sprite file first (e.g. a sactx... file), then press Ctrl+A.", Theme.Warn);
            return;
        }

        if (_contents.Sprites.Count == 0)
        {
            SetStatus("This file has no sprites. Pick one whose middle list shows sprites.", Theme.Warn);
            return;
        }

        ScannedFile source = _contentsSource;
        var candidates = _contents.Sprites
            .Select(sp => new PoseCandidate(sp.Name, sp.Width, sp.Height, sp.PivotX, sp.PivotY))
            .ToList();
        AutoLookResult result = AutoLook.Build(candidates,
            (pose, hold) => LookLoader.MakeFrame(source, LookFrame.KindSprite, pose.Name, null, hold));

        if (result.Look.IsEmpty)
        {
            SetStatus("Could not find any poses in this file (only effects or UI pictures).", Theme.Warn);
            return;
        }

        LookData old = Looks.Get(slot);
        result.Look.Scale = 0f;
        result.Look.ArtFacesLeft = old.ArtFacesLeft;
        result.Look.AnchorAtPivot = old.AnchorAtPivot;
        Looks.Replace(slot, result.Look);

        string who = slot == LookSlot.Player1 ? "P1" : "Dummy";
        if (result.Character is string character && Roster is not null && Roster.HasDefaultName(slot))
        {
            Roster.Rename(slot, character);
        }
        SetStatus($"Auto look {who}: {result.Summary}.  F4 check, Ctrl+Z undo", result.UsedShapes ? Theme.Warn : Theme.Good);
    }

    /// <summary>Ctrl+1..5: appends the selected sprite (or sheet) to a pose clip; Ctrl+6 / 7 to the swing / hit effect.</summary>
    private void AddToLook(LookSlot slot, ClipId clip)
    {
        if (Looks is null || _contentsSource is null || _texIndex >= _textureView.Count)
        {
            SetStatus("Open a file and pick a sprite first.", Theme.Warn);
            return;
        }

        PictureEntry entry = _textureView[_texIndex];
        string kind = entry.Sprite is not null ? LookFrame.KindSprite : LookFrame.KindTexture;
        string? sheet = entry.Sprite is not null && _image is not null && _image.Name == entry.Name ? _image.Sheet : null;
        LookFrame frame = LookLoader.MakeFrame(_contentsSource, kind, entry.Name, sheet, LookData.DefaultHold(clip));

        int count = 0;
        Looks.Modify(slot, look =>
        {
            List<LookFrame> frames = look.EditFrames(clip);
            frames.Add(frame);
            count = frames.Count;
        });

        string who = slot == LookSlot.Player1 ? "P1" : "Dummy";
        SetStatus($"Added {entry.Name} to {who} {LookData.ClipKey(clip)} (frame {count}). F4 edits looks; F3 goes back to the arena.", Theme.Good);
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Could not open folder: " + ex.Message, Theme.Bad);
        }
    }

    private static void RevealInExplorer(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{file}\"", UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Opening Explorer is a convenience only.
        }
    }

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
        _statusUntil = Raylib.GetTime() + 10;
    }

    // =========================================================================================
    //  Drawing
    // =========================================================================================

    private void DrawStatusLine(int width)
    {
        string text;
        Color color = Theme.TextDim;

        if (_reportProgress is string report)
        {
            text = report;
            color = Theme.Warn;
        }
        else if (_status is not null && Raylib.GetTime() < _statusUntil)
        {
            text = _status;
            color = _statusColor;
        }
        else if (_scanTask is not null)
        {
            text = $"Scanning Limbus Company files... {_scanCount} checked";
            color = Theme.Warn;
        }
        else if (_indexTask is not null && _indexTotal > 0)
        {
            text = $"Reading names for search: {_indexDone} / {_indexTotal} files (only the first time; you can browse meanwhile)";
            color = Theme.Warn;
        }
        else if (_scanError is not null)
        {
            text = "Scan failed: " + _scanError;
            color = Theme.Bad;
        }
        else if (_scan is { GameWasRunning: true })
        {
            text = "Limbus Company is running. Close it, then press F5 to scan its files.";
            color = Theme.Bad;
        }
        else if (_scan is not null && !_scan.AnyRootExists)
        {
            text = "Limbus Company's files were not found. Install the game (and launch it once so it downloads its data).";
            color = Theme.Bad;
        }
        else if (_scan is not null)
        {
            int bundles = _scan.Files.Count(f => f.Kind == AssetFileKind.UnityBundle);
            text = $"{_scan.Files.Count} files checked in {_scan.Elapsed.TotalSeconds:0.0}s: {bundles} bundles, {_all.Count} browsable. Nothing is changed or copied unless you press Ctrl+S.";
        }
        else
        {
            text = string.Empty;
        }

        Ui.Text(Ui.FitEnd(text, Font, width - 40), 20, 52, Font, color);
    }

    private void DrawFilesPane()
    {
        DrawPanel(FilesX, FilesW, _pane == Pane.Files);

        string title = _visible.Count == _all.Count ? $"Files {_all.Count}" : $"Files {_visible.Count} / {_all.Count}";
        Ui.Text(title, FilesX + 10, PanelTop + 8, Font, Theme.Accent);

        string filter = "filter: " + (_filter.Length > 0 ? _filter : "(type)") + (((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : " ");
        int filterMax = FilesW - 30 - Ui.Measure(title, Font);
        Ui.TextRight(Ui.FitEnd(filter, Font, filterMax), FilesX + FilesW - 10, PanelTop + 8, Font, _filter.Length > 0 ? Theme.Text : Theme.TextDim);

        string[] words = FilterWords;
        for (int row = 0; row < VisibleRows; row++)
        {
            int index = _fileScroll + row;
            if (index >= _visible.Count)
            {
                break;
            }

            ScannedFile file = _visible[index];
            int y = ListTop + (row * RowHeight);
            DrawRowHighlight(FilesX, FilesW, y, index == _fileIndex, _pane == Pane.Files);

            string size = AssetReport.Bytes(file.Size);
            int sizeWidth = Ui.Measure(size, Font);
            Color nameColor = file == _contentsSource ? Theme.Accent : Theme.Text;
            Ui.Text(Ui.FitMiddle(Label(file, words), Font, FilesW - 30 - sizeWidth), FilesX + 8, y + 1, Font, nameColor);
            Ui.TextRight(size, FilesX + FilesW - 8, y + 1, Font, Theme.TextDim);
        }

        if (_scan is not null && _visible.Count == 0)
        {
            Ui.Text(_all.Count == 0 ? "Nothing to browse yet." : "No file names match the filter.", FilesX + 10, ListTop + 4, Font, Theme.TextDim);
        }
    }

    private void DrawTexturesPane()
    {
        DrawPanel(TexX, TexW, _pane == Pane.Pictures);

        if (_contentsTask is not null)
        {
            Ui.Text("Opening...", TexX + 10, PanelTop + 8, Font, Theme.Warn);
            return;
        }

        if (_contents is null)
        {
            Ui.Text("Pictures", TexX + 10, PanelTop + 8, Font, Theme.Accent);
            Ui.Text("Pick a file on the left", TexX + 10, ListTop + 4, Font, Theme.TextDim);
            Ui.Text("and press Enter.", TexX + 10, ListTop + 4 + RowHeight, Font, Theme.TextDim);
            return;
        }

        string title = _textureView.Count == _pictures.Count ? $"Pictures {_pictures.Count}" : $"Pictures {_textureView.Count} / {_pictures.Count}";
        Ui.Text(title, TexX + 10, PanelTop + 8, Font, Theme.Accent);
        if (_pictures.Count > 0)
        {
            string counts = _contents.Sprites.Count > 0 ? Plural(_contents.Sprites.Count, "sprite") : Plural(_contents.Textures.Count, "sheet");
            int countsMax = TexW - 30 - Ui.Measure(title, Font);
            if (Ui.Measure(counts, Font) <= countsMax)
            {
                Ui.TextRight(counts, TexX + TexW - 10, PanelTop + 8, Font, Theme.TextDim);
            }
        }

        if (_pictures.Count == 0)
        {
            int y = ListTop + 4;
            foreach (string error in _contents.Errors.Take(3))
            {
                Ui.Text(Ui.FitEnd(error, Font, TexW - 20), TexX + 10, y, Font, Theme.Bad);
                y += RowHeight;
            }

            Ui.Text("No pictures here. Contains:", TexX + 10, y, Font, Theme.TextDim);
            y += RowHeight;
            foreach (KeyValuePair<string, int> kv in _contents.ClassCounts.OrderByDescending(kv => kv.Value).Take(18))
            {
                Ui.Text(Ui.FitEnd($"{kv.Value,5}  {kv.Key}", Font, TexW - 20), TexX + 10, y, Font, Theme.Text);
                y += RowHeight;
            }

            return;
        }

        for (int row = 0; row < VisibleRows; row++)
        {
            int index = _texScroll + row;
            if (index >= _textureView.Count)
            {
                break;
            }

            PictureEntry entry = _textureView[index];
            int y = ListTop + (row * RowHeight);
            DrawRowHighlight(TexX, TexW, y, index == _texIndex, _pane == Pane.Pictures);

            // Sheets (whole textures) show their size in the accent colour so they stand out from sprites.
            bool sheet = entry.Texture is not null;
            string dims = sheet ? $"sheet {entry.Width}x{entry.Height}" : $"{entry.Width}x{entry.Height}";
            int dimsWidth = Ui.Measure(dims, Font);
            string name = entry.Name.Length > 0 ? entry.Name : "(no name)";
            Ui.Text(Ui.FitEnd(name, Font, TexW - 30 - dimsWidth), TexX + 8, y + 1, Font, sheet ? Theme.TextDim : Theme.Text);
            Ui.TextRight(dims, TexX + TexW - 8, y + 1, Font, sheet ? Theme.Accent : Theme.TextDim);
        }
    }

    private void DrawPreviewPane()
    {
        DrawPanel(PreviewX, PreviewW, false);
        Ui.Text("Preview", PreviewX + 10, PanelTop + 8, Font, Theme.Accent);

        var area = new Rectangle(PreviewX + 10, PreviewTop, PreviewW - 20, PreviewH - 20);
        DrawCheckerboard(area);

        if (_gpuTexture is Texture2D texture && _image is not null)
        {
            float scale = MathF.Min(area.Width / texture.Width, area.Height / texture.Height);
            scale = MathF.Min(scale, 4f);
            float w = texture.Width * scale;
            float h = texture.Height * scale;
            var dest = new Rectangle(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
            Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.Width, texture.Height), dest, Vector2.Zero, 0f, Color.White);

            if (_image.Pivot is (float px, float py))
            {
                // The pivot is the point the game stands the sprite on (usually the feet).
                int cx = (int)(dest.X + (px * scale));
                int cy = (int)(dest.Y + (py * scale));
                if (area.X <= cx && cx < area.X + area.Width && area.Y <= cy && cy < area.Y + area.Height)
                {
                    var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)200);
                    Raylib.DrawRectangle(cx - 9, cy - 1, 19, 3, shadow);
                    Raylib.DrawRectangle(cx - 1, cy - 9, 3, 19, shadow);
                    Raylib.DrawLine(cx - 8, cy, cx + 9, cy, Theme.Accent);
                    Raylib.DrawLine(cx, cy - 8, cx, cy + 9, Theme.Accent);
                }
            }
        }
        else if (_decodeTask is not null)
        {
            Ui.TextCentered("Decoding...", (int)(area.X + (area.Width / 2)), (int)(area.Y + (area.Height / 2)) - 10, Font, Theme.Warn);
        }

        int infoY = PreviewTop + PreviewH - 4;
        if (_previewError is not null)
        {
            Ui.Text(Ui.FitEnd(_previewError, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.Bad);
            infoY += RowHeight;
        }

        if (_image is not null)
        {
            Ui.Text(Ui.FitEnd(_image.Name, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.Text);
            infoY += RowHeight;
            string kind = _image.Pivot is null ? TextureDecoding.FormatName(_image.Format) : "sprite, + marks the pivot";
            Ui.Text(Ui.FitEnd($"{_image.Width} x {_image.Height}   {kind}", Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.TextDim);
            infoY += RowHeight;
            if (_image.Sheet is string sheetName)
            {
                Ui.Text(Ui.FitMiddle("sheet " + sheetName, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.TextDim);
                infoY += RowHeight;
            }

            if (_image.Detail is string detail)
            {
                Ui.Text(Ui.FitEnd(detail, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.TextDim);
                infoY += RowHeight;
            }
        }

        if (_contentsSource is not null && _image?.Sheet is null)
        {
            Ui.Text(Ui.FitMiddle(_contentsSource.DisplayName, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.TextDim);
            infoY += RowHeight;
        }

        if (_image is not null)
        {
            Ui.Text(Ui.FitEnd("Ctrl+A  auto look for P1 from this file", Font, PreviewW - 20), PreviewX + 10, infoY + 6, Font, Theme.Accent);
            Ui.Text(Ui.FitEnd("Ctrl+1-5 pose: idle walk jump punch hit, 6/7 fx", Font, PreviewW - 20), PreviewX + 10, infoY + 6 + RowHeight, Font, Theme.Text);
            Ui.Text(Ui.FitEnd("Ctrl+Q add to F4's clip  +Shift Dummy  Ctrl+Z undo", Font, PreviewW - 20), PreviewX + 10, infoY + 6 + (2 * RowHeight), Font, Theme.TextDim);
        }
    }

    private static void DrawPanel(int x, int w, bool focused)
    {
        Raylib.DrawRectangle(x, PanelTop, w, ListBottom - PanelTop + 6, Theme.Panel);
        Raylib.DrawRectangleLines(x, PanelTop, w, ListBottom - PanelTop + 6, focused ? Theme.Accent : Theme.PanelBorder);
        Raylib.DrawLine(x, ListTop - 4, x + w, ListTop - 4, Theme.PanelBorder);
    }

    private static void DrawRowHighlight(int x, int w, int y, bool selected, bool focused)
    {
        if (!selected)
        {
            return;
        }

        Color fill = focused ? new Color(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B, (byte)60) : new Color((byte)70, (byte)70, (byte)84, (byte)255);
        Raylib.DrawRectangle(x + 2, y, w - 4, RowHeight, fill);
    }

    private static void DrawCheckerboard(Rectangle area)
    {
        const int cell = 16;
        var dark = new Color((byte)36, (byte)36, (byte)44, (byte)255);
        var light = new Color((byte)52, (byte)52, (byte)62, (byte)255);

        for (int y = 0; y < area.Height; y += cell)
        {
            for (int x = 0; x < area.Width; x += cell)
            {
                bool odd = ((x / cell) + (y / cell)) % 2 == 1;
                int w = (int)MathF.Min(cell, area.Width - x);
                int h = (int)MathF.Min(cell, area.Height - y);
                Raylib.DrawRectangle((int)area.X + x, (int)area.Y + y, w, h, odd ? light : dark);
            }
        }
    }

    // =========================================================================================
    //  GPU upload
    // =========================================================================================

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

    private void UnloadGpuTexture()
    {
        if (_gpuTexture is Texture2D texture)
        {
            Raylib.UnloadTexture(texture);
            _gpuTexture = null;
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
