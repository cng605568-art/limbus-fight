using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace LimbusFight.Assets;

/// <summary>What a file on disk turned out to be, judged from its first bytes.</summary>
public enum AssetFileKind
{
    UnityBundle,
    UnitySerialized,
    UnityStreamData,
    AudioBank,
    AudioFile,
    Video,
    Image,
    Text,
    Program,
    Other,
}

/// <summary>One file found under a Limbus Company asset root.</summary>
public sealed class ScannedFile
{
    public required string RootKind { get; init; }

    public required string FullPath { get; init; }

    /// <summary>Path inside its root, always with '/' separators.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Human name: the bundle name for Unity's download cache (folder/&lt;hash&gt;/__data), else the relative path.</summary>
    public required string DisplayName { get; init; }

    public required long Size { get; init; }

    public required AssetFileKind Kind { get; init; }

    /// <summary>Short description of the detected format, e.g. "UnityFS 2021.3.14f1 LZ4" or "FMOD FSB5".</summary>
    public required string Detail { get; init; }

    public string? UnityVersion { get; init; }

    public string? Compression { get; init; }

    public uint BundleFlags { get; init; }

    /// <summary>First 16 bytes in hex, kept for files that could not be identified.</summary>
    public string HeaderHex { get; init; } = string.Empty;

    /// <summary>True for files the asset browser can open (Unity bundles and serialized files).</summary>
    public bool IsBrowsable => Kind is AssetFileKind.UnityBundle or AssetFileKind.UnitySerialized;
}

/// <summary>Result of one read-only pass over the asset roots.</summary>
public sealed class AssetScanResult
{
    public required IReadOnlyList<AssetRoot> Roots { get; init; }

    public required IReadOnlyList<ScannedFile> Files { get; init; }

    public required bool GameWasRunning { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public bool AnyRootExists => Roots.Any(r => r.Exists);

    public IEnumerable<ScannedFile> Browsable => Files.Where(f => f.IsBrowsable);
}

/// <summary>
/// Inventories the Limbus Company asset roots by reading only the first 64 bytes of each file.
/// Never writes, never keeps files open, and refuses to run while the game is open.
/// </summary>
public static class AssetScanner
{
    private const int HeaderBytes = 64;

    private static readonly string[] SerializedNames =
    {
        "globalgamemanagers", "globalgamemanagers.assets", "resources.assets", "unity default resources", "unity_builtin_extra",
    };

    /// <summary>Scans every existing root. <paramref name="progress"/> receives the number of files examined so far.</summary>
    public static AssetScanResult Scan(IReadOnlyList<AssetRoot> roots, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var watch = Stopwatch.StartNew();
        var files = new List<ScannedFile>();
        var errors = new List<string>();

        if (LimbusAssetRoots.IsGameRunning())
        {
            errors.Add("Limbus Company is running. Close it, then scan again.");
            return new AssetScanResult { Roots = roots, Files = files, GameWasRunning = true, Elapsed = watch.Elapsed, Errors = errors };
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // don't follow links inside the roots (avoids loops)
            ReturnSpecialDirectories = false,
        };

        byte[] buffer = new byte[HeaderBytes];
        int examined = 0;

        foreach (AssetRoot root in roots.Where(r => r.Exists))
        {
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(root.Path, "*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{root.Kind}: cannot list files ({ex.Message})");
                continue;
            }

            foreach (string path in paths)
            {
                cancel.ThrowIfCancellationRequested();

                ScannedFile? scanned = Examine(root, path, buffer, errors);
                if (scanned is not null)
                {
                    files.Add(scanned);
                }

                examined++;
                if (examined % 250 == 0)
                {
                    progress?.Report(examined);
                }
            }
        }

        progress?.Report(examined);
        return new AssetScanResult { Roots = roots, Files = files, GameWasRunning = false, Elapsed = watch.Elapsed, Errors = errors };
    }

    private static ScannedFile? Examine(AssetRoot root, string path, byte[] buffer, List<string> errors)
    {
        long size;
        int read;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            size = stream.Length;
            read = stream.ReadAtLeast(buffer, Math.Min(HeaderBytes, (int)Math.Min(size, HeaderBytes)), throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (errors.Count < 50)
            {
                errors.Add($"{root.Kind}: cannot read {Path.GetFileName(path)} ({ex.Message})");
            }

            return null;
        }

        string relative = Path.GetRelativePath(root.Path, path).Replace('\\', '/');
        ReadOnlySpan<byte> header = buffer.AsSpan(0, read);
        Sniffed sniffed = Sniff(Path.GetFileName(path), header);

        return new ScannedFile
        {
            RootKind = root.Kind,
            FullPath = path,
            RelativePath = relative,
            DisplayName = MakeDisplayName(root.Kind, relative),
            Size = size,
            Kind = sniffed.Kind,
            Detail = sniffed.Detail,
            UnityVersion = sniffed.UnityVersion,
            Compression = sniffed.Compression,
            BundleFlags = sniffed.Flags,
            HeaderHex = Convert.ToHexString(header[..Math.Min(16, header.Length)]),
        };
    }

    /// <summary>Unity's download cache stores bundles as &lt;bundle name&gt;/&lt;hash&gt;/__data: show just the bundle name.</summary>
    public static string MakeDisplayName(string rootKind, string relative)
    {
        string[] parts = relative.Split('/');
        if (rootKind == LimbusAssetRoots.CacheRootKind && parts.Length >= 3 && parts[^1] == "__data")
        {
            return string.Join('/', parts[..^2]);
        }

        return relative;
    }

    private readonly record struct Sniffed(AssetFileKind Kind, string Detail, string? UnityVersion = null, string? Compression = null, uint Flags = 0);

    private static Sniffed Sniff(string name, ReadOnlySpan<byte> h)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();

        if (StartsWith(h, "UnityFS\0"))
        {
            return SniffUnityFs(h);
        }

        if (StartsWith(h, "UnityWeb\0") || StartsWith(h, "UnityRaw\0") || StartsWith(h, "UnityArchive"))
        {
            return new Sniffed(AssetFileKind.UnityBundle, "Legacy Unity bundle");
        }

        if (ext is ".ress" or ".resource")
        {
            return new Sniffed(AssetFileKind.UnityStreamData, "Unity stream data (" + ext + ")");
        }

        if (StartsWith(h, "FSB5"))
        {
            return new Sniffed(AssetFileKind.AudioBank, "FMOD FSB5 sound bank");
        }

        if (StartsWith(h, "RIFF") && h.Length >= 12)
        {
            string form = Encoding.ASCII.GetString(h.Slice(8, 4));
            return form switch
            {
                "FEV " => new Sniffed(AssetFileKind.AudioBank, "FMOD Studio bank"),
                "WAVE" when ext == ".wem" => new Sniffed(AssetFileKind.AudioFile, "Wwise WEM audio"),
                "WAVE" => new Sniffed(AssetFileKind.AudioFile, "WAV audio"),
                "WEBP" => new Sniffed(AssetFileKind.Image, "WebP image"),
                _ => new Sniffed(AssetFileKind.Other, "RIFF/" + form.Trim()),
            };
        }

        if (StartsWith(h, "BKHD"))
        {
            return new Sniffed(AssetFileKind.AudioBank, "Wwise sound bank");
        }

        if (StartsWith(h, "AKPK"))
        {
            return new Sniffed(AssetFileKind.AudioBank, "Wwise package");
        }

        if (StartsWith(h, "AFS2"))
        {
            return new Sniffed(AssetFileKind.AudioBank, "CRI AWB wave bank");
        }

        if (StartsWith(h, "@UTF"))
        {
            return new Sniffed(AssetFileKind.AudioBank, "CRI ACB cue bank");
        }

        if (StartsWith(h, "OggS"))
        {
            return new Sniffed(AssetFileKind.AudioFile, "Ogg audio");
        }

        if (StartsWith(h, "ID3") || (h.Length >= 2 && h[0] == 0xFF && (h[1] & 0xE0) == 0xE0 && ext == ".mp3"))
        {
            return new Sniffed(AssetFileKind.AudioFile, "MP3 audio");
        }

        if (StartsWith(h, "CRID"))
        {
            return new Sniffed(AssetFileKind.Video, "CRI USM video");
        }

        if (h.Length >= 8 && Encoding.ASCII.GetString(h.Slice(4, 4)) == "ftyp")
        {
            return new Sniffed(AssetFileKind.Video, "MP4 video");
        }

        if (h.Length >= 4 && h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3)
        {
            return new Sniffed(AssetFileKind.Video, "WebM video");
        }

        if (h.Length >= 8 && h[0] == 0x89 && StartsWith(h[1..], "PNG"))
        {
            return new Sniffed(AssetFileKind.Image, "PNG image");
        }

        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return new Sniffed(AssetFileKind.Image, "JPEG image");
        }

        if (StartsWith(h, "MZ") || (h.Length >= 4 && h[0] == 0x7F && StartsWith(h[1..], "ELF")))
        {
            return new Sniffed(AssetFileKind.Program, "Program / library");
        }

        if (LooksLikeSerializedFile(name, ext, h))
        {
            return new Sniffed(AssetFileKind.UnitySerialized, "Unity serialized file");
        }

        if (name == "__info" || LooksLikeText(h))
        {
            return new Sniffed(AssetFileKind.Text, ext.Length > 0 ? "Text (" + ext + ")" : "Text");
        }

        return new Sniffed(AssetFileKind.Other, ext.Length > 0 ? "Unknown (" + ext + ")" : "Unknown");
    }

    /// <summary>Parses the UnityFS header: signature, format, Unity version, size, block info sizes, flags.</summary>
    private static Sniffed SniffUnityFs(ReadOnlySpan<byte> h)
    {
        int pos = 8; // "UnityFS\0"
        if (h.Length < pos + 4)
        {
            return new Sniffed(AssetFileKind.UnityBundle, "UnityFS (truncated header)");
        }

        uint format = BinaryPrimitives.ReadUInt32BigEndian(h[pos..]);
        pos += 4;

        string? playerVersion = ReadCString(h, ref pos);
        string? engineVersion = ReadCString(h, ref pos);

        if (engineVersion is null || h.Length < pos + 8 + 4 + 4 + 4)
        {
            return new Sniffed(AssetFileKind.UnityBundle, $"UnityFS v{format}", engineVersion);
        }

        pos += 8; // total size
        pos += 8; // compressed + uncompressed block info sizes
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(h[pos..]);

        string compression = (flags & 0x3F) switch
        {
            0 => "none",
            1 => "LZMA",
            2 => "LZ4",
            3 => "LZ4HC",
            4 => "LZHAM",
            uint other => $"custom({other})",
        };

        return new Sniffed(
            AssetFileKind.UnityBundle,
            $"UnityFS v{format} {engineVersion} {compression}" + (playerVersion is "5.x.x" or null ? string.Empty : $" ({playerVersion})"),
            engineVersion,
            compression,
            flags);
    }

    private static bool LooksLikeSerializedFile(string name, string ext, ReadOnlySpan<byte> h)
    {
        if (h.Length < 20)
        {
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(h[8..]);
        bool plausibleHeader = version is >= 9 and <= 40 && h[16] is 0 or 1 && h[17] == 0 && h[18] == 0 && h[19] == 0;
        if (!plausibleHeader)
        {
            return false;
        }

        string lower = name.ToLowerInvariant();
        return ext == ".assets"
            || SerializedNames.Contains(lower)
            || lower.StartsWith("level", StringComparison.Ordinal)
            || lower.StartsWith("sharedassets", StringComparison.Ordinal)
            || lower.StartsWith("cab-", StringComparison.Ordinal)
            || ext.Length == 0;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> h)
    {
        if (h.Length == 0)
        {
            return false;
        }

        foreach (byte b in h)
        {
            bool printable = b is >= 0x20 and < 0x7F || b is (byte)'\n' or (byte)'\r' or (byte)'\t';
            if (!printable)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, string ascii)
    {
        if (data.Length < ascii.Length)
        {
            return false;
        }

        for (int i = 0; i < ascii.Length; i++)
        {
            if (data[i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadCString(ReadOnlySpan<byte> data, ref int pos)
    {
        int start = pos;
        while (pos < data.Length && data[pos] != 0)
        {
            pos++;
        }

        if (pos >= data.Length)
        {
            return null;
        }

        string value = Encoding.ASCII.GetString(data[start..pos]);
        pos++; // skip the terminator
        return value;
    }
}
