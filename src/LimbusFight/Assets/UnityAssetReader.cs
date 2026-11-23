using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace LimbusFight.Assets;

/// <summary>One Texture2D found inside a bundle or serialized file.</summary>
public sealed record TextureInfo(int FileIndex, long PathId, string Name, int Width, int Height, int Format)
{
    public string FormatName => TextureDecoding.FormatName(Format);
}

/// <summary>What one bundle / serialized file contains.</summary>
public sealed record AssetFileContents(
    string SourcePath,
    string? UnityVersion,
    bool TypeTreeEnabled,
    IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<TextureInfo> Textures,
    IReadOnlyList<string> Errors);

/// <summary>A decoded texture, ready to upload to the GPU or save as PNG.</summary>
public sealed record DecodedImage(string Name, int Width, int Height, int Format, byte[] Rgba);

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
            string? unityVersion = null;
            bool typeTree = true;

            try
            {
                Open(source);
            }
            catch (Exception ex)
            {
                errors.Add($"Could not open: {ex.GetType().Name}: {ex.Message}");
                return new AssetFileContents(source.FullPath, null, false, counts, textures, errors);
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
            }

            textures.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return new AssetFileContents(source.FullPath, unityVersion, typeTree, counts, textures, errors);
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

            AssetTypeValueField field = _manager.GetBaseField(inst, info);
            byte[] data = ReadPixelData(inst, field);

            if (!TextureDecoding.TryDecode(texture.Format, data, texture.Width, texture.Height, out byte[] rgba, out string error))
            {
                throw new InvalidDataException(error);
            }

            return new DecodedImage(texture.Name, texture.Width, texture.Height, texture.Format, rgba);
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
        _bundle = null;
        _openPath = null;
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
