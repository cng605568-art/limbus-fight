using System.Diagnostics;
using System.Numerics;
using LimbusFight.Assets;
using LimbusFight.Core;
using LimbusFight.Input;
using LimbusFight.Rendering;
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
    private const int PreviewH = 424;

    private enum Pane
    {
        Files,
        Textures,
    }

    private readonly UnityAssetReader _reader = new();
    private SteamCheckResult? _steam;

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
    private int _texIndex;
    private int _texScroll;

    // ---- Preview ----------------------------------------------------------------------------
    private Task<DecodedImage>? _decodeTask;
    private TextureInfo? _decodeFor;
    private TextureInfo? _wantedTexture;
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
        IReadOnlyList<AssetRoot> roots = LimbusAssetRoots.Find(_steam);
        var progress = new InlineProgress<int>(n => _scanCount = n);
        _scanTask = Task.Run(() => AssetScanner.Scan(roots, progress));
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
                _all = _scan.Browsable
                    .OrderBy(f => f.RootKind == LimbusAssetRoots.CacheRootKind ? 0 : 1)
                    .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                ApplyFilter();
            }
        }

        if (_contentsTask is { IsCompleted: true } contentsTask)
        {
            _contentsTask = null;
            _contents = contentsTask.IsFaulted
                ? new AssetFileContents(_contentsSource?.FullPath ?? string.Empty, null, false, new Dictionary<string, int>(), Array.Empty<TextureInfo>(),
                    new[] { contentsTask.Exception?.GetBaseException().Message ?? "Could not open file." })
                : contentsTask.Result;
            _texIndex = 0;
            _texScroll = 0;

            if (_wantedSource is not null && _wantedSource != _contentsSource)
            {
                RequestContents(_wantedSource);
            }
            else if (_contents.Textures.Count > 0)
            {
                _pane = Pane.Textures;
                RequestTexture(_contents.Textures[0]);
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
                _previewError = decodeTask.Exception?.GetBaseException().Message ?? "Could not decode texture.";
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
        _image = null;
        _previewError = null;
        _wantedTexture = null;
        UnloadGpuTexture();
        _contentsTask = Task.Run(() => _reader.ReadContents(source));
    }

    private void RequestTexture(TextureInfo texture)
    {
        _wantedTexture = texture;
        if (_decodeTask is not null || _contentsSource is null)
        {
            return;
        }

        ScannedFile source = _contentsSource;
        _decodeFor = texture;
        _decodeTask = Task.Run(() => _reader.DecodeTexture(source, texture));
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
        var progress = new InlineProgress<string>(text => _reportProgress = text);
        _reportProgress = "Writing report...";
        _reportTask = Task.Run(() => AssetReport.Write(AssetReport.Build(steam, scan, _reader, progress)));
    }

    // =========================================================================================
    //  Input
    // =========================================================================================

    private void HandleTyping()
    {
        bool changed = false;
        int codepoint;
        while ((codepoint = Raylib.GetCharPressed()) != 0)
        {
            if (codepoint is >= 32 and < 127 && _filter.Length < 60)
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

        if (keys.WasPressed(KeyboardKey.Tab) || keys.WasPressed(KeyboardKey.Right) || keys.WasPressed(KeyboardKey.Left))
        {
            bool toTextures = keys.WasPressed(KeyboardKey.Right) || (keys.WasPressed(KeyboardKey.Tab) && _pane == Pane.Files);
            _pane = toTextures && _contents is { Textures.Count: > 0 } ? Pane.Textures : Pane.Files;
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
        else if (_contents is { Textures.Count: > 0 } contents && move != 0)
        {
            _texIndex = Math.Clamp((int)Math.Clamp((long)_texIndex + move, int.MinValue, int.MaxValue), 0, contents.Textures.Count - 1);
            _texScroll = KeepVisible(_texIndex, _texScroll);
            RequestTexture(contents.Textures[_texIndex]);
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
            else if (overTextures && _contents is not null)
            {
                _texScroll = Math.Clamp(_texScroll + delta, 0, Math.Max(0, _contents.Textures.Count - VisibleRows));
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
        else if (overTextures && _contents is not null && _texScroll + row < _contents.Textures.Count)
        {
            _pane = Pane.Textures;
            _texIndex = _texScroll + row;
            RequestTexture(_contents.Textures[_texIndex]);
        }
    }

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

    private void ApplyFilter()
    {
        string[] words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _visible = words.Length == 0
            ? _all
            : _all.Where(f => words.All(w => f.DisplayName.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        _fileIndex = Math.Clamp(_fileIndex, 0, Math.Max(0, _visible.Count - 1));
        _fileScroll = Math.Clamp(KeepVisible(_fileIndex, _fileScroll), 0, Math.Max(0, _visible.Count - VisibleRows));
    }

    // =========================================================================================
    //  Saving
    // =========================================================================================

    private unsafe void SavePng()
    {
        if (_image is null)
        {
            SetStatus("Select a texture first.", Theme.Warn);
            return;
        }

        try
        {
            Directory.CreateDirectory(ExportFolder);
            string safeName = string.Concat(_image.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = Path.Combine(ExportFolder, (safeName.Length > 0 ? safeName : "texture") + ".png");

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
            Ui.Text(Ui.FitMiddle(file.DisplayName, Font, FilesW - 30 - sizeWidth), FilesX + 8, y + 1, Font, nameColor);
            Ui.TextRight(size, FilesX + FilesW - 8, y + 1, Font, Theme.TextDim);
        }

        if (_scan is not null && _visible.Count == 0)
        {
            Ui.Text(_all.Count == 0 ? "Nothing to browse yet." : "No file names match the filter.", FilesX + 10, ListTop + 4, Font, Theme.TextDim);
        }
    }

    private void DrawTexturesPane()
    {
        DrawPanel(TexX, TexW, _pane == Pane.Textures);

        if (_contentsTask is not null)
        {
            Ui.Text("Opening...", TexX + 10, PanelTop + 8, Font, Theme.Warn);
            return;
        }

        if (_contents is null)
        {
            Ui.Text("Textures", TexX + 10, PanelTop + 8, Font, Theme.Accent);
            Ui.Text("Pick a file on the left", TexX + 10, ListTop + 4, Font, Theme.TextDim);
            Ui.Text("and press Enter.", TexX + 10, ListTop + 4 + RowHeight, Font, Theme.TextDim);
            return;
        }

        Ui.Text($"Textures {_contents.Textures.Count}", TexX + 10, PanelTop + 8, Font, Theme.Accent);
        if (_contents.UnityVersion is string version)
        {
            Ui.TextRight("Unity " + version, TexX + TexW - 10, PanelTop + 8, Font, Theme.TextDim);
        }

        if (_contents.Textures.Count == 0)
        {
            int y = ListTop + 4;
            foreach (string error in _contents.Errors.Take(3))
            {
                Ui.Text(Ui.FitEnd(error, Font, TexW - 20), TexX + 10, y, Font, Theme.Bad);
                y += RowHeight;
            }

            Ui.Text("No Texture2D here. Contains:", TexX + 10, y, Font, Theme.TextDim);
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
            if (index >= _contents.Textures.Count)
            {
                break;
            }

            TextureInfo texture = _contents.Textures[index];
            int y = ListTop + (row * RowHeight);
            DrawRowHighlight(TexX, TexW, y, index == _texIndex, _pane == Pane.Textures);

            string dims = $"{texture.Width}x{texture.Height}";
            int dimsWidth = Ui.Measure(dims, Font);
            string name = texture.Name.Length > 0 ? texture.Name : "(no name)";
            Ui.Text(Ui.FitEnd(name, Font, TexW - 30 - dimsWidth), TexX + 8, y + 1, Font, Theme.Text);
            Ui.TextRight(dims, TexX + TexW - 8, y + 1, Font, Theme.TextDim);
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
            Ui.Text($"{_image.Width} x {_image.Height}   {TextureDecoding.FormatName(_image.Format)}", PreviewX + 10, infoY, Font, Theme.TextDim);
            infoY += RowHeight;
        }

        if (_contentsSource is not null)
        {
            Ui.Text(Ui.FitMiddle(_contentsSource.DisplayName, Font, PreviewW - 20), PreviewX + 10, infoY, Font, Theme.TextDim);
            infoY += RowHeight;
        }

        if (_image is not null)
        {
            Ui.Text("Ctrl+S  save PNG (stays on this PC)", PreviewX + 10, infoY + 6, Font, Theme.TextDim);
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
