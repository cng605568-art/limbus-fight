using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace LimbusFight.Assets;

/// <summary>What kind of named object an index entry is.</summary>
public enum NameKind : byte
{
    Texture,
    Sprite,
    Audio,
}

/// <summary>A named object inside a bundle (texture, sprite or sound), used for search.</summary>
public readonly record struct AssetName(NameKind Kind, string Name);

/// <summary>Everything remembered about one file between runs.</summary>
public sealed class CachedFile
{
    public required string FullPath { get; init; }

    public required long Size { get; init; }

    public required long ModifiedTicks { get; init; }

    public required AssetFileKind Kind { get; init; }

    public required string Detail { get; init; }

    public string? UnityVersion { get; init; }

    public string? Compression { get; init; }

    public uint Flags { get; init; }

    public string HeaderHex { get; init; } = string.Empty;

    /// <summary>Names of the textures, sprites and sounds inside; null until the file has been indexed.</summary>
    public AssetName[]? Names { get; set; }
}

/// <summary>
/// Remembers file headers and object names between runs, so the asset browser opens in seconds
/// instead of re-reading 12,000+ files. Entries are reused only while a file's size and
/// modification time are unchanged, so a Limbus Company update is picked up automatically.
/// Stored in %LOCALAPPDATA%\LimbusFight\cache\asset_index.tsv (names and sizes only, no game content).
/// </summary>
public sealed class AssetCache
{
    private const string Magic = "LIMBUSFIGHT-ASSET-CACHE\t1";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, CachedFile> _files = new(PathComparer);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimbusFight", "cache", "asset_index.tsv");

    public int Count => _files.Count;

    /// <summary>Loads the cache, or returns an empty one if it is missing, old or damaged.</summary>
    public static AssetCache Load(string? path = null)
    {
        var cache = new AssetCache();
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
            {
                return cache;
            }

            using var reader = new StreamReader(path, Encoding.UTF8);
            if (reader.ReadLine() != Magic)
            {
                return cache;
            }

            CachedFile? current = null;
            var names = new List<AssetName>();
            bool indexed = false;

            void Flush()
            {
                if (current is not null)
                {
                    current.Names = indexed ? names.ToArray() : null;
                    cache._files[current.FullPath] = current;
                }

                names.Clear();
            }

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string[] parts = line.Split('\t');
                if (parts[0] == "F" && parts.Length >= 11)
                {
                    Flush();
                    current = new CachedFile
                    {
                        FullPath = parts[1],
                        Size = long.Parse(parts[2], CultureInfo.InvariantCulture),
                        ModifiedTicks = long.Parse(parts[3], CultureInfo.InvariantCulture),
                        Kind = Enum.Parse<AssetFileKind>(parts[4]),
                        Detail = parts[5],
                        UnityVersion = parts[6].Length > 0 ? parts[6] : null,
                        Compression = parts[7].Length > 0 ? parts[7] : null,
                        Flags = uint.Parse(parts[8], CultureInfo.InvariantCulture),
                        HeaderHex = parts[9],
                    };
                    indexed = parts[10] == "1";
                }
                else if (current is not null && parts.Length >= 3 && parts[0] == "N")
                {
                    names.Add(new AssetName((NameKind)byte.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]));
                }
            }

            Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or OverflowException)
        {
            return new AssetCache(); // a damaged cache is simply rebuilt
        }

        return cache;
    }

    /// <summary>Writes the cache atomically (temp file, then replace).</summary>
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        string? folder = Path.GetDirectoryName(path);
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        string temp = path + ".tmp";
        using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.WriteLine(Magic);
            foreach (CachedFile file in _files.Values)
            {
                AssetName[]? names = file.Names;
                writer.Write("F\t");
                writer.Write(Clean(file.FullPath));
                writer.Write('\t');
                writer.Write(file.Size.ToString(CultureInfo.InvariantCulture));
                writer.Write('\t');
                writer.Write(file.ModifiedTicks.ToString(CultureInfo.InvariantCulture));
                writer.Write('\t');
                writer.Write(file.Kind.ToString());
                writer.Write('\t');
                writer.Write(Clean(file.Detail));
                writer.Write('\t');
                writer.Write(Clean(file.UnityVersion ?? string.Empty));
                writer.Write('\t');
                writer.Write(Clean(file.Compression ?? string.Empty));
                writer.Write('\t');
                writer.Write(file.Flags.ToString(CultureInfo.InvariantCulture));
                writer.Write('\t');
                writer.Write(file.HeaderHex);
                writer.Write('\t');
                writer.WriteLine(names is null ? "0" : "1");

                if (names is not null)
                {
                    foreach (AssetName name in names)
                    {
                        writer.Write("N\t");
                        writer.Write(((byte)name.Kind).ToString(CultureInfo.InvariantCulture));
                        writer.Write('\t');
                        writer.WriteLine(Clean(name.Name));
                    }
                }
            }
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Returns the cached entry if the file has the same size and modification time.</summary>
    public bool TryGet(string fullPath, long size, long modifiedTicks, out CachedFile file)
    {
        if (_files.TryGetValue(fullPath, out CachedFile? found) && found.Size == size && found.ModifiedTicks == modifiedTicks)
        {
            file = found;
            return true;
        }

        file = null!;
        return false;
    }

    public void Put(CachedFile file) => _files[file.FullPath] = file;

    public AssetName[]? GetNames(string fullPath) =>
        _files.TryGetValue(fullPath, out CachedFile? file) ? file.Names : null;

    public void SetNames(string fullPath, AssetName[] names)
    {
        if (_files.TryGetValue(fullPath, out CachedFile? file))
        {
            file.Names = names;
        }
    }

    /// <summary>Forgets files that no longer exist (Limbus deleted or replaced them).</summary>
    public void KeepOnly(IEnumerable<string> presentPaths)
    {
        var keep = new HashSet<string>(presentPaths, PathComparer);
        foreach (string path in _files.Keys)
        {
            if (!keep.Contains(path))
            {
                _files.TryRemove(path, out _);
            }
        }
    }

    /// <summary>Every remembered file (used to find a sprite again after Limbus renames a bundle).</summary>
    public IEnumerable<CachedFile> Entries => _files.Values;

    /// <summary>All indexed names, for the report.</summary>
    public IEnumerable<AssetName> AllNames() =>
        _files.Values.Where(f => f.Names is not null).SelectMany(f => f.Names!);

    private static string Clean(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
