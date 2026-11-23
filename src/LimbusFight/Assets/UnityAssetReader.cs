using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace LimbusFight.Assets;

/// <summary>One Texture2D found inside a bundle or serialized file.</summary>
public sealed record TextureInfo(int FileIndex, long PathId, string Name, int Width, int Height, int Format)
{
    public string FormatName => TextureDecoding.FormatName(Format);
}

/// <summary>One Sprite found inside a bundle or serialized file. Width / Height are its full size (m_Rect).</summary>
public sealed record SpriteInfo(int FileIndex, long PathId, string Name, int Width, int Height);

/// <summary>What one bundle / serialized file contains.</summary>
public sealed record AssetFileContents(
    string SourcePath,
    string? UnityVersion,
    bool TypeTreeEnabled,
    IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<TextureInfo> Textures,
    IReadOnlyList<SpriteInfo> Sprites,
    IReadOnlyList<string> Errors);

/// <summary>A decoded texture or cut-out sprite, ready to upload to the GPU or save as PNG.</summary>
public sealed record DecodedImage(string Name, int Width, int Height, int Format, byte[] Rgba)
{
    /// <summary>Sprites only: the texture (atlas sheet) the sprite was cut from.</summary>
    public string? Sheet { get; init; }

    /// <summary>Sprites only: how it was packed and cut, for the preview.</summary>
    public string? Detail { get; init; }

    /// <summary>Sprites only: the pivot (the point the game anchors the sprite on), pixels from the top-left.</summary>
    public (float X, float Y)? Pivot { get; init; }
}

/// <summary>How one sprite is stored; used by the asset report to check sprite cutting on real files.</summary>
public sealed record SpriteDiagnostics(
    string Name,
    float RectWidth,
    float RectHeight,
    string Source,
    string Packing,
    int VertexCount,
    int TriangleCount,
    bool HasUvs,
    string Channels,
    string? Problem);

/// <summary>
/// Read-only access to Unity bundles and serialized files through AssetsTools.NET.
/// Keeps exactly one source open at a time; every public method is thread-safe (one caller at a time).
/// </summary>
public sealed class UnityAssetReader : IDisposable
{
    private readonly object _gate = new();
    private readonly AssetsManager _manager = new();
    private readonly List<AssetsFileInstance> _files = new();
    private BundleFileInstance? _bundle;
    private string? _openPath;

    /// <summary>Lists every asset class and every Texture2D in <paramref name="source"/>.</summary>
    public AssetFileContents ReadContents(ScannedFile source)
    {
        lock (_gate)
        {
            var errors = new List<string>();
            var counts = new Dictionary<string, int>();
            var textures = new List<TextureInfo>();
            var sprites = new List<SpriteInfo>();
            string? unityVersion = null;
            bool typeTree = true;

            try
            {
                Open(source);
            }
            catch (Exception ex)
            {
                errors.Add($"Could not open: {ex.GetType().Name}: {ex.Message}");
                return new AssetFileContents(source.FullPath, null, false, counts, textures, sprites, errors);
            }

            for (int fileIndex = 0; fileIndex < _files.Count; fileIndex++)
            {
                AssetsFileInstance inst = _files[fileIndex];
                AssetsFileMetadata meta = inst.file.Metadata;
                unityVersion ??= meta.UnityVersion;
                typeTree &= meta.TypeTreeEnabled;

                foreach (AssetFileInfo info in meta.AssetInfos)
                {
                    string className = ClassName(info.TypeId);
                    counts[className] = counts.TryGetValue(className, out int n) ? n + 1 : 1;
                }

                foreach (AssetFileInfo info in inst.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    try
                    {
                        AssetTypeValueField field = _manager.GetBaseField(inst, info);
                        textures.Add(new TextureInfo(
                            fileIndex,
                            info.PathId,
                            field["m_Name"].AsString,
                            field["m_Width"].AsInt,
                            field["m_Height"].AsInt,
                            field["m_TextureFormat"].AsInt));
                    }
                    catch (Exception ex)
                    {
                        if (errors.Count < 20)
                        {
                            errors.Add($"Texture2D #{info.PathId}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }

                foreach (AssetFileInfo info in inst.file.GetAssetsOfType(AssetClassID.Sprite))
                {
                    try
                    {
                        AssetTypeValueField field = _manager.GetBaseField(inst, info);
                        AssetTypeValueField rect = field["m_Rect"];
                        sprites.Add(new SpriteInfo(
                            fileIndex,
                            info.PathId,
                            field["m_Name"].AsString,
                            (int)MathF.Round(rect["width"].AsFloat),
                            (int)MathF.Round(rect["height"].AsFloat)));
                    }
                    catch (Exception ex)
                    {
                        if (errors.Count < 20)
                        {
                            errors.Add($"Sprite #{info.PathId}: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
            }

            textures.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            sprites.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return new AssetFileContents(source.FullPath, unityVersion, typeTree, counts, textures, sprites, errors);
        }
    }

    /// <summary>
    /// Fast listing of texture, sprite and sound names: reads only each object's name, not its data.
    /// Throws <see cref="InvalidOperationException"/> if Limbus Company is running.
    /// </summary>
    public AssetName[] ReadNames(ScannedFile source)
    {
        lock (_gate)
        {
            Open(source);
            var names = new List<AssetName>();
            foreach (AssetsFileInstance inst in _files)
            {
                AddNames(inst, AssetClassID.Texture2D, NameKind.Texture, names);
                AddNames(inst, AssetClassID.Sprite, NameKind.Sprite, names);
                AddNames(inst, AssetClassID.AudioClip, NameKind.Audio, names);
            }

            return names.ToArray();
        }
    }

    /// <summary>Reads and decodes one texture. Throws <see cref="InvalidDataException"/> with a readable reason on failure.</summary>
    public DecodedImage DecodeTexture(ScannedFile source, TextureInfo texture)
    {
        lock (_gate)
        {
            Open(source);

            if (texture.FileIndex < 0 || texture.FileIndex >= _files.Count)
            {
                throw new InvalidDataException("Texture no longer exists in this file.");
            }

            AssetsFileInstance inst = _files[texture.FileIndex];
            AssetFileInfo info = inst.file.GetAssetInfo(texture.PathId)
                ?? throw new InvalidDataException("Texture no longer exists in this file.");

            return DecodeCached(inst, info);
        }
    }

    /// <summary>
    /// Cuts one sprite out of its texture or atlas sheet. Throws <see cref="InvalidDataException"/>
    /// with a readable reason on failure.
    /// </summary>
    public DecodedImage DecodeSprite(ScannedFile source, SpriteInfo sprite)
    {
        lock (_gate)
        {
            Open(source);
            SpriteSource found = FindSpriteSource(sprite);

            if (found.Problem is not null || found.SheetFile is null || found.SheetInfo is null || found.Geometry is null)
            {
                throw new InvalidDataException(found.Problem ?? "Sprite has no texture.");
            }

            DecodedImage sheet = DecodeCached(found.SheetFile, found.SheetInfo);
            CutSprite cut = SpriteCutter.Cut(found.Geometry, sheet.Rgba, sheet.Width, sheet.Height);
            string packing = new SpritePacking(found.Geometry.SettingsRaw).Describe();

            return new DecodedImage(sprite.Name, cut.Width, cut.Height, sheet.Format, cut.Rgba)
            {
                Sheet = sheet.Name,
                Detail = $"{packing}, cut by {cut.Method}",
                Pivot = (cut.PivotX, cut.PivotY),
            };
        }
    }

    /// <summary>How the sprites of <paramref name="source"/> are stored (for the asset report). Never throws.</summary>
    public IReadOnlyList<SpriteDiagnostics> DescribeSprites(ScannedFile source, int max)
    {
        lock (_gate)
        {
            var result = new List<SpriteDiagnostics>();
            try
            {
                Open(source);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return result;
            }

            for (int fileIndex = 0; fileIndex < _files.Count && result.Count < max; fileIndex++)
            {
                foreach (AssetFileInfo info in _files[fileIndex].file.GetAssetsOfType(AssetClassID.Sprite))
                {
                    if (result.Count >= max)
                    {
                        break;
                    }

                    try
                    {
                        SpriteSource found = FindSpriteSource(new SpriteInfo(fileIndex, info.PathId, string.Empty, 0, 0));
                        SpriteGeometry? g = found.Geometry;
                        string sheetName = found.SheetFile is not null && found.SheetInfo is not null
                            ? ReadName(found.SheetFile, found.SheetInfo) ?? "?"
                            : "-";
                        result.Add(new SpriteDiagnostics(
                            found.Name,
                            g?.RectWidth ?? 0,
                            g?.RectHeight ?? 0,
                            $"{found.Via} -> {sheetName}",
                            g is null ? "-" : new SpritePacking(g.SettingsRaw).Describe() + (g.DownscaleMultiplier is > 0 and not 1f ? $", scale {g.DownscaleMultiplier:0.###}" : string.Empty),
                            g?.Positions.Length ?? 0,
                            (g?.Triangles.Length ?? 0) / 3,
                            g is not null && g.Uvs.Any(uv => uv != Vector2.Zero),
                            found.Channels,
                            found.Problem));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        result.Add(new SpriteDiagnostics($"#{info.PathId}", 0, 0, "-", "-", 0, 0, false, "-", $"{ex.GetType().Name}: {ex.Message}"));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Closes the open source and frees its memory.</summary>
    public void Close()
    {
        lock (_gate)
        {
            CloseCore();
        }
    }

    public void Dispose() => Close();

    private void Open(ScannedFile source)
    {
        if (_openPath == source.FullPath)
        {
            return;
        }

        CloseCore();

        if (LimbusAssetRoots.IsGameRunning())
        {
            throw new InvalidOperationException("Limbus Company is running. Close it first.");
        }

        var stream = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (source.Kind == AssetFileKind.UnityBundle)
        {
            _bundle = _manager.LoadBundleFile(stream, source.FullPath, unpackIfPacked: true);
            int count = _bundle.file.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < count; i++)
            {
                if (_bundle.file.IsAssetsFile(i))
                {
                    AssetsFileInstance? inst = _manager.LoadAssetsFileFromBundle(_bundle, i, loadDeps: false);
                    if (inst is not null)
                    {
                        _files.Add(inst);
                    }
                }
            }
        }
        else
        {
            _files.Add(_manager.LoadAssetsFile(stream, source.FullPath, loadDeps: false));
        }

        _openPath = source.FullPath;
    }

    private void CloseCore()
    {
        _manager.UnloadAll();
        _files.Clear();
        _decoded.Clear();
        _bundle = null;
        _openPath = null;
    }

    // =========================================================================================
    //  Decoded texture cache (atlas sheets are reused by every sprite on them)
    // =========================================================================================

    private const int DecodedCacheSize = 3;
    private readonly List<(AssetsFileInstance File, long PathId, DecodedImage Image)> _decoded = new();

    private DecodedImage DecodeCached(AssetsFileInstance inst, AssetFileInfo info)
    {
        int hit = _decoded.FindIndex(e => e.File == inst && e.PathId == info.PathId);
        if (hit >= 0)
        {
            var entry = _decoded[hit];
            _decoded.RemoveAt(hit);
            _decoded.Add(entry);
            return entry.Image;
        }

        if (info.TypeId != (int)AssetClassID.Texture2D)
        {
            throw new InvalidDataException($"Expected a Texture2D, found {ClassName(info.TypeId)}.");
        }

        AssetTypeValueField field = _manager.GetBaseField(inst, info);
        string name = field["m_Name"].AsString;
        int width = field["m_Width"].AsInt;
        int height = field["m_Height"].AsInt;
        int format = field["m_TextureFormat"].AsInt;
        byte[] data = ReadPixelData(inst, field);

        if (!TextureDecoding.TryDecode(format, data, width, height, out byte[] rgba, out string error))
        {
            throw new InvalidDataException(error);
        }

        var image = new DecodedImage(name, width, height, format, rgba);
        _decoded.Add((inst, info.PathId, image));
        if (_decoded.Count > DecodedCacheSize)
        {
            _decoded.RemoveAt(0);
        }

        return image;
    }

    // =========================================================================================
    //  Sprites
    // =========================================================================================

    /// <summary>Where a sprite's pixels are: the sheet texture plus the geometry to cut it out.</summary>
    private sealed record SpriteSource(
        string Name,
        string Via,
        SpriteGeometry? Geometry,
        AssetsFileInstance? SheetFile,
        AssetFileInfo? SheetInfo,
        string Channels,
        string? Problem);

    private SpriteSource FindSpriteSource(SpriteInfo sprite)
    {
        if (sprite.FileIndex < 0 || sprite.FileIndex >= _files.Count)
        {
            throw new InvalidDataException("Sprite no longer exists in this file.");
        }

        AssetsFileInstance inst = _files[sprite.FileIndex];
        AssetFileInfo info = inst.file.GetAssetInfo(sprite.PathId)
            ?? throw new InvalidDataException("Sprite no longer exists in this file.");

        AssetTypeValueField sf = _manager.GetBaseField(inst, info);
        string name = sf["m_Name"].AsString;
        AssetTypeValueField rd = sf["m_RD"];
        if (rd.IsDummy)
        {
            return new SpriteSource(name, "-", null, null, null, "-", "Sprite has no render data.");
        }

        MeshData mesh = ReadMesh(rd);

        // 1) Packed into a SpriteAtlas: the atlas holds the sheet and the rectangle for this sprite.
        AssetTypeValueField? atlasData = null;
        AssetsFileInstance dataFile = inst;
        string via = "own texture";
        (AssetsFileInstance File, AssetTypeValueField Field)? atlas = FindAtlas(inst, sf, out string? atlasProblem);

        if (atlas is { } found)
        {
            atlasData = FindRenderData(found.Field, sf["m_RenderDataKey"]);
            if (atlasData is null)
            {
                atlasProblem = $"Atlas {found.Field["m_Name"].AsString} has no entry for this sprite.";
            }
            else
            {
                dataFile = found.File;
                via = "atlas " + found.Field["m_Name"].AsString;
            }
        }

        // 2) Otherwise the sprite's own render data names its texture.
        AssetTypeValueField data = atlasData ?? rd;
        (AssetsFileInstance File, AssetFileInfo Info)? sheet = Resolve(dataFile, data["texture"]);

        if (sheet is null)
        {
            string problem = atlasProblem
                ?? "The picture sheet for this sprite is stored in another file (not supported yet).";
            return new SpriteSource(name, via, null, null, null, mesh.Channels, problem);
        }

        AssetTypeValueField rect = sf["m_Rect"];
        AssetTypeValueField texRect = data["textureRect"];
        AssetTypeValueField texOffset = data["textureRectOffset"];
        AssetTypeValueField pivot = sf["m_Pivot"];
        AssetTypeValueField downscale = data["downscaleMultiplier"];
        AssetTypeValueField ppu = sf["m_PixelsToUnits"];

        var geometry = new SpriteGeometry
        {
            Name = name,
            RectWidth = rect["width"].AsFloat,
            RectHeight = rect["height"].AsFloat,
            PivotX = pivot.IsDummy ? 0.5f : pivot["x"].AsFloat,
            PivotY = pivot.IsDummy ? 0.5f : pivot["y"].AsFloat,
            PixelsToUnits = ppu.IsDummy ? 100f : ppu.AsFloat,
            TexRectX = texRect["x"].AsFloat,
            TexRectY = texRect["y"].AsFloat,
            TexRectWidth = texRect["width"].AsFloat,
            TexRectHeight = texRect["height"].AsFloat,
            TexRectOffsetX = texOffset.IsDummy ? 0f : texOffset["x"].AsFloat,
            TexRectOffsetY = texOffset.IsDummy ? 0f : texOffset["y"].AsFloat,
            SettingsRaw = data["settingsRaw"].IsDummy ? 0u : data["settingsRaw"].AsUInt,
            DownscaleMultiplier = downscale.IsDummy ? 1f : downscale.AsFloat,
            Positions = mesh.Positions,
            Uvs = mesh.Uvs,
            Triangles = mesh.Triangles,
        };

        return new SpriteSource(name, via, geometry, sheet.Value.File, sheet.Value.Info, mesh.Channels, null);
    }

    /// <summary>The sprite's SpriteAtlas: by its direct link, else by atlas tag name inside the open file.</summary>
    private (AssetsFileInstance File, AssetTypeValueField Field)? FindAtlas(AssetsFileInstance inst, AssetTypeValueField sprite, out string? problem)
    {
        problem = null;
        AssetTypeValueField link = sprite["m_SpriteAtlas"];

        if (!link.IsDummy && link["m_PathID"].AsLong != 0)
        {
            (AssetsFileInstance File, AssetFileInfo Info)? target = Resolve(inst, link);
            if (target is null)
            {
                problem = "This sprite's atlas is stored in another file (not supported yet).";
                return null;
            }

            return (target.Value.File, _manager.GetBaseField(target.Value.File, target.Value.Info));
        }

        AssetTypeValueField tags = sprite["m_AtlasTags"];
        if (tags.IsDummy || tags["Array"].Children.Count == 0)
        {
            return null;
        }

        string tag = tags["Array"].Children[0].AsString;
        foreach (AssetsFileInstance file in _files)
        {
            foreach (AssetFileInfo info in file.file.GetAssetsOfType(AssetClassID.SpriteAtlas))
            {
                if (ReadName(file, info) == tag)
                {
                    return (file, _manager.GetBaseField(file, info));
                }
            }
        }

        return null;
    }

    /// <summary>The atlas entry whose key (GUID + id) equals the sprite's m_RenderDataKey.</summary>
    private static AssetTypeValueField? FindRenderData(AssetTypeValueField atlas, AssetTypeValueField key)
    {
        AssetTypeValueField map = atlas["m_RenderDataMap"];
        if (map.IsDummy || key.IsDummy)
        {
            return null;
        }

        (uint, uint, uint, uint, long) wanted = ReadKey(key);
        foreach (AssetTypeValueField pair in map["Array"].Children)
        {
            if (ReadKey(pair["first"]) == wanted)
            {
                return pair["second"];
            }
        }

        return null;
    }

    private static (uint, uint, uint, uint, long) ReadKey(AssetTypeValueField key)
    {
        List<AssetTypeValueField> guid = key["first"].Children;
        uint Part(int i) => i < guid.Count ? guid[i].AsUInt : 0u;
        return (Part(0), Part(1), Part(2), Part(3), key["second"].AsLong);
    }

    /// <summary>Follows a PPtr inside the open source only; never loads other files from disk.</summary>
    private (AssetsFileInstance File, AssetFileInfo Info)? Resolve(AssetsFileInstance from, AssetTypeValueField pptr)
    {
        if (pptr.IsDummy)
        {
            return null;
        }

        int fileId = pptr["m_FileID"].AsInt;
        long pathId = pptr["m_PathID"].AsLong;
        if (pathId == 0)
        {
            return null;
        }

        AssetsFileInstance? target = from;
        if (fileId != 0)
        {
            List<AssetsFileExternal> externals = from.file.Metadata.Externals;
            if (fileId < 1 || fileId > externals.Count)
            {
                return null;
            }

            string path = externals[fileId - 1].PathName;
            string fileName = path[(path.LastIndexOf('/') + 1)..];
            target = _files.FirstOrDefault(f => string.Equals(f.name, fileName, StringComparison.OrdinalIgnoreCase));
        }

        AssetFileInfo? info = target?.file.GetAssetInfo(pathId);
        return target is null || info is null ? null : (target, info);
    }

    private sealed record MeshData(Vector2[] Positions, Vector2[] Uvs, int[] Triangles, string Channels);

    /// <summary>
    /// Reads the sprite mesh: vertex positions, UVs and triangles from m_VertexData / m_IndexBuffer
    /// (Unity 5.6+), or from the older "vertices" / "indices" arrays. Missing parts come back empty.
    /// </summary>
    private static MeshData ReadMesh(AssetTypeValueField rd)
    {
        var empty = new MeshData(Array.Empty<Vector2>(), Array.Empty<Vector2>(), Array.Empty<int>(), "-");
        try
        {
            int[] triangles = ReadTriangles(rd);
            AssetTypeValueField vertexData = rd["m_VertexData"];
            if (vertexData.IsDummy)
            {
                return ReadOldMesh(rd, triangles) ?? empty;
            }

            int count = (int)vertexData["m_VertexCount"].AsUInt;
            List<AssetTypeValueField> channelFields = vertexData["m_Channels"]["Array"].Children;
            byte[] bytes = vertexData["m_DataSize"].AsByteArray;
            if (count <= 0 || channelFields.Count == 0 || bytes.Length == 0)
            {
                return empty with { Triangles = triangles };
            }

            var channels = channelFields
                .Select(c => (Stream: c["stream"].AsInt, Offset: c["offset"].AsInt, Format: c["format"].AsInt, Dimension: c["dimension"].AsInt & 0xF))
                .ToArray();

            // 2018+ meshes list 14 channels and keep UV0 in channel 4; older ones list 8 with UV0 in channel 3.
            bool modern = channels.Length >= 14;
            int uvChannel = modern ? 4 : 3;
            int[] streamOffsets = StreamOffsets(channels, count, modern, out int[] strides);

            string describe = string.Join(" ", channels
                .Select((c, i) => (c, i))
                .Where(x => x.c.Dimension > 0)
                .Select(x => $"ch{x.i}:s{x.c.Stream}/f{x.c.Format}x{x.c.Dimension}"));

            Vector2[] positions = ReadChannel(bytes, channels, 0, count, streamOffsets, strides, modern);
            Vector2[] uvs = channels.Length > uvChannel
                ? ReadChannel(bytes, channels, uvChannel, count, streamOffsets, strides, modern)
                : Array.Empty<Vector2>();

            return new MeshData(positions, uvs, positions.Length > 0 ? triangles : Array.Empty<int>(), describe);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return empty with { Channels = "unreadable: " + ex.Message };
        }
    }

    private static int[] ReadTriangles(AssetTypeValueField rd)
    {
        AssetTypeValueField indexField = rd["m_IndexBuffer"];
        AssetTypeValueField subMeshes = rd["m_SubMeshes"];
        if (indexField.IsDummy || subMeshes.IsDummy)
        {
            return Array.Empty<int>();
        }

        byte[] indexBytes = indexField["Array"].AsByteArray;
        var triangles = new List<int>();

        foreach (AssetTypeValueField sub in subMeshes["Array"].Children)
        {
            AssetTypeValueField topology = sub["topology"];
            if (!topology.IsDummy && topology.AsInt != 0)
            {
                continue; // only plain triangle lists are used by sprites
            }

            long first = sub["firstByte"].AsUInt / 2;
            long indexCount = sub["indexCount"].AsUInt;
            for (long i = 0; i + 2 < indexCount; i += 3)
            {
                long at = (first + i) * 2;
                if (at + 6 > indexBytes.Length)
                {
                    break;
                }

                for (int k = 0; k < 3; k++)
                {
                    triangles.Add(BitConverter.ToUInt16(indexBytes, (int)at + (k * 2)));
                }
            }
        }

        return triangles.ToArray();
    }

    /// <summary>Vertex streams are stored one after another, each padded to 16 bytes.</summary>
    private static int[] StreamOffsets((int Stream, int Offset, int Format, int Dimension)[] channels, int count, bool modern, out int[] strides)
    {
        int streamCount = channels.Max(c => c.Stream) + 1;
        strides = new int[streamCount];
        foreach (var c in channels)
        {
            if (c.Dimension > 0)
            {
                strides[c.Stream] += c.Dimension * FormatSize(c.Format, modern);
            }
        }

        int[] offsets = new int[streamCount];
        int offset = 0;
        for (int s = 0; s < streamCount; s++)
        {
            offsets[s] = offset;
            offset += strides[s] * count;
            offset = (offset + 15) & ~15;
        }

        return offsets;
    }

    private static int FormatSize(int format, bool modern) => modern
        ? format switch { 0 => 4, 1 => 2, 2 or 3 or 6 or 7 => 1, 4 or 5 or 8 or 9 => 2, _ => 4 }
        : format switch { 0 => 4, 1 => 2, 2 or 3 or 4 or 7 or 8 => 1, 5 or 6 or 9 or 10 => 2, _ => 4 };

    /// <summary>Reads the first two components of a float / half channel; empty if the channel is absent or not float.</summary>
    private static Vector2[] ReadChannel(byte[] bytes, (int Stream, int Offset, int Format, int Dimension)[] channels, int index, int count, int[] streamOffsets, int[] strides, bool modern)
    {
        var c = channels[index];
        if (c.Dimension < 2 || (c.Format != 0 && c.Format != 1))
        {
            return Array.Empty<Vector2>();
        }

        int size = FormatSize(c.Format, modern);
        int stride = strides[c.Stream];
        var result = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            int at = streamOffsets[c.Stream] + (i * stride) + c.Offset;
            if (at + (2 * size) > bytes.Length)
            {
                return Array.Empty<Vector2>();
            }

            result[i] = size == 4
                ? new Vector2(BitConverter.ToSingle(bytes, at), BitConverter.ToSingle(bytes, at + 4))
                : new Vector2((float)BitConverter.ToHalf(bytes, at), (float)BitConverter.ToHalf(bytes, at + 2));
        }

        return result;
    }

    /// <summary>Unity before 5.6: SpriteRenderData has "vertices" (pos, uv) and "indices" arrays.</summary>
    private static MeshData? ReadOldMesh(AssetTypeValueField rd, int[] triangles)
    {
        AssetTypeValueField vertices = rd["vertices"];
        if (vertices.IsDummy)
        {
            return null;
        }

        var positions = new List<Vector2>();
        var uvs = new List<Vector2>();
        foreach (AssetTypeValueField v in vertices["Array"].Children)
        {
            positions.Add(new Vector2(v["pos"]["x"].AsFloat, v["pos"]["y"].AsFloat));
            AssetTypeValueField uv = v["uv"];
            if (!uv.IsDummy)
            {
                uvs.Add(new Vector2(uv["x"].AsFloat, uv["y"].AsFloat));
            }
        }

        AssetTypeValueField indices = rd["indices"];
        if (triangles.Length == 0 && !indices.IsDummy)
        {
            triangles = indices["Array"].Children.Select(i => i.AsInt).ToArray();
            triangles = triangles[..(triangles.Length - (triangles.Length % 3))];
        }

        return new MeshData(
            positions.ToArray(),
            uvs.Count == positions.Count ? uvs.ToArray() : Array.Empty<Vector2>(),
            triangles,
            "old vertices");
    }

    /// <summary>Texture bytes live either inline ("image data") or in a separate stream (.resS) named by m_StreamData.</summary>
    private byte[] ReadPixelData(AssetsFileInstance inst, AssetTypeValueField field)
    {
        AssetTypeValueField streamData = field["m_StreamData"];
        if (!streamData.IsDummy)
        {
            long size = (long)streamData["size"].AsULong;
            string path = streamData["path"].AsString;

            if (size > 0 && !string.IsNullOrEmpty(path))
            {
                long offset = (long)streamData["offset"].AsULong;
                return ReadStreamedBytes(inst, path, offset, size);
            }
        }

        AssetTypeValueField inline = field["image data"];
        if (inline.IsDummy)
        {
            throw new InvalidDataException("Texture has no pixel data.");
        }

        return inline.AsByteArray;
    }

    private byte[] ReadStreamedBytes(AssetsFileInstance inst, string streamPath, long offset, long size)
    {
        if (size > int.MaxValue)
        {
            throw new InvalidDataException("Texture is too large.");
        }

        byte[] buffer = new byte[size];
        string fileName = streamPath[(streamPath.LastIndexOf('/') + 1)..];

        if (_bundle is not null)
        {
            // Inside a bundle: "archive:/CAB-xxxx/CAB-xxxx.resS" is another entry of the same bundle.
            int index = _bundle.file.GetFileIndex(fileName);
            if (index < 0)
            {
                throw new InvalidDataException($"Stream file {fileName} is missing from the bundle.");
            }

            _bundle.file.GetFileRange(index, out long entryOffset, out long entryLength);
            if (offset + size > entryLength)
            {
                throw new InvalidDataException("Stream data range is outside its file.");
            }

            lock (_bundle.file.DataReader)
            {
                Stream data = _bundle.DataStream;
                data.Position = entryOffset + offset;
                data.ReadExactly(buffer);
            }

            return buffer;
        }

        // Loose serialized file (LimbusCompany_Data\sharedassets0.assets -> sharedassets0.assets.resS).
        string directory = Path.GetDirectoryName(inst.path) ?? string.Empty;
        string external = Path.Combine(directory, fileName);
        using var stream = new FileStream(external, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private void AddNames(AssetsFileInstance inst, AssetClassID type, NameKind kind, List<AssetName> names)
    {
        foreach (AssetFileInfo info in inst.file.GetAssetsOfType(type))
        {
            string? name = ReadName(inst, info);
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(new AssetName(kind, name));
            }
        }
    }

    /// <summary>m_Name is the first field of Texture2D / Sprite / AudioClip, so it can be read directly.</summary>
    private string? ReadName(AssetsFileInstance inst, AssetFileInfo info)
    {
        try
        {
            AssetsFile file = inst.file;
            if (file.Metadata.TypeTreeEnabled)
            {
                TypeTreeType? type = file.Metadata.FindTypeTreeTypeByID(info.TypeId, info.GetScriptIndex(file));
                if (type is not null && type.Nodes.Count > 1 && type.Nodes[1].GetNameString(type.StringBufferBytes) == "m_Name")
                {
                    lock (file.Reader)
                    {
                        file.Reader.Position = info.GetAbsoluteByteOffset(file);
                        return file.Reader.ReadCountStringInt32();
                    }
                }
            }

            return _manager.GetBaseField(inst, info)["m_Name"].AsString;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string ClassName(int typeId) =>
        Enum.IsDefined(typeof(AssetClassID), typeId) ? ((AssetClassID)typeId).ToString() : $"Class{typeId}";
}
