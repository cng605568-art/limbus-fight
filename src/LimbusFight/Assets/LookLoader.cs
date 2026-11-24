using LimbusFight.Animation;
using LimbusFight.Core;

namespace LimbusFight.Assets;

/// <summary>Result for one look frame: the cut-out picture, or why it could not be loaded.</summary>
public sealed record LoadedPicture(string Key, DecodedImage? Image, string? Error);

/// <summary>
/// Finds the pictures a look refers to in the player's own Limbus Company files and cuts them out.
/// Runs on a background thread with its own <see cref="UnityAssetReader"/>; never writes anywhere.
/// </summary>
public static class LookLoader
{
    /// <summary>
    /// Loads every distinct picture in <paramref name="frames"/>. Each file is opened once.
    /// Throws <see cref="InvalidOperationException"/> if Limbus Company is running.
    /// </summary>
    public static Dictionary<string, LoadedPicture> Load(
        IEnumerable<LookFrame> frames,
        SteamCheckResult steam,
        UnityAssetReader reader,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancel = default)
    {
        if (LimbusAssetRoots.IsGameRunning())
        {
            throw new InvalidOperationException("Limbus Company is running. Close it, then press F5 to load the fighters' pictures.");
        }

        var distinct = frames.GroupBy(f => f.Key).Select(g => g.First()).ToList();
        var result = new Dictionary<string, LoadedPicture>();
        var contents = new Dictionary<string, AssetFileContents?>(StringComparer.OrdinalIgnoreCase);
        AssetCache? cache = null;
        int done = 0;

        try
        {
            foreach (IGrouping<string, LookFrame> group in distinct.GroupBy(f => $"{f.Root}|{f.File}"))
            {
                cancel.ThrowIfCancellationRequested();
                ScannedFile? file = Locate(group.First(), steam);

                foreach (LookFrame frame in group)
                {
                    progress?.Report((done, distinct.Count));
                    ScannedFile? source = file;

                    // The bundle may have been renamed by a game update: look the picture up by name.
                    if (source is null || !Holds(Read(reader, source, contents), frame))
                    {
                        cache ??= AssetCache.Load();
                        source = FindByName(frame, steam, cache, reader, contents) ?? source;
                    }

                    result[frame.Key] = source is null
                        ? new LoadedPicture(frame.Key, null, $"{frame.Name}: its file is no longer in Limbus Company's folders.")
                        : Decode(reader, source, Read(reader, source, contents), frame);
                    done++;
                }
            }
        }
        finally
        {
            reader.Close();
        }

        progress?.Report((done, distinct.Count));
        return result;
    }

    /// <summary>Builds the reference for a sprite or texture picked in the asset browser.</summary>
    public static LookFrame MakeFrame(ScannedFile file, string kind, string name, string? sheet, int hold) => new()
    {
        Root = file.RootKind == LimbusAssetRoots.CacheRootKind ? LookFrame.RootCache : LookFrame.RootGame,
        File = file.RootKind == LimbusAssetRoots.CacheRootKind ? file.DisplayName : file.RelativePath,
        Kind = kind,
        Name = name,
        Sheet = sheet,
        Hold = hold,
    };

    private static DecodedImage? Cut(UnityAssetReader reader, ScannedFile file, AssetFileContents? contents, LookFrame frame, out string? error)
    {
        error = null;
        if (contents is null)
        {
            error = $"{frame.Name}: its file could not be opened.";
            return null;
        }

        if (frame.Kind == LookFrame.KindTexture)
        {
            TextureInfo? texture = contents.Textures.FirstOrDefault(t => t.Name == frame.Name);
            if (texture is null)
            {
                error = $"{frame.Name}: not found in its file.";
                return null;
            }

            DecodedImage image = reader.DecodeTexture(file, texture);
            return image with { Pivot = (image.Width / 2f, image.Height) }; // whole textures stand on their bottom edge
        }

        List<SpriteInfo> candidates = contents.Sprites.Where(s => s.Name == frame.Name).ToList();
        if (candidates.Count == 0)
        {
            error = $"{frame.Name}: not found in its file.";
            return null;
        }

        DecodedImage? firstGood = null;
        foreach (SpriteInfo sprite in candidates)
        {
            try
            {
                DecodedImage image = reader.DecodeSprite(file, sprite);
                if (frame.Sheet is null || image.Sheet == frame.Sheet)
                {
                    return image;
                }

                firstGood ??= image;
            }
            catch (InvalidDataException ex)
            {
                error = $"{frame.Name}: {ex.Message}";
            }
        }

        return firstGood;
    }

    private static LoadedPicture Decode(UnityAssetReader reader, ScannedFile file, AssetFileContents? contents, LookFrame frame)
    {
        try
        {
            DecodedImage? image = Cut(reader, file, contents, frame, out string? error);
            return new LoadedPicture(frame.Key, image, image is null ? error ?? $"{frame.Name}: could not be read." : null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new LoadedPicture(frame.Key, null, $"{frame.Name}: {ex.Message}");
        }
    }

    /// <summary>Lists a file's pictures once per load; null if it can't be opened.</summary>
    private static AssetFileContents? Read(UnityAssetReader reader, ScannedFile file, Dictionary<string, AssetFileContents?> memo)
    {
        if (!memo.TryGetValue(file.FullPath, out AssetFileContents? contents))
        {
            try
            {
                contents = reader.ReadContents(file);
                if (contents.Errors.Any(e => e.StartsWith("Could not open", StringComparison.Ordinal)))
                {
                    contents = null;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                contents = null;
            }

            memo[file.FullPath] = contents;
        }

        return contents;
    }

    private static bool Holds(AssetFileContents? contents, LookFrame frame) =>
        contents is not null && (frame.Kind == LookFrame.KindTexture
            ? contents.Textures.Any(t => t.Name == frame.Name)
            : contents.Sprites.Any(s => s.Name == frame.Name));

    /// <summary>Where the reference's file is now: cache bundles keep their folder name, game files their path.</summary>
    private static ScannedFile? Locate(LookFrame frame, SteamCheckResult steam)
    {
        if (frame.Root == LookFrame.RootCache)
        {
            string? cacheRoot = LimbusAssetRoots.FindDownloadCache();
            if (cacheRoot is null)
            {
                return null;
            }

            string folder = Path.Combine(cacheRoot, frame.File.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder))
            {
                return null;
            }

            // <bundle>/<version hash>/__data: Unity may keep more than one version; use the newest.
            FileInfo? data = new DirectoryInfo(folder).EnumerateDirectories()
                .Select(d => new FileInfo(Path.Combine(d.FullName, "__data")))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            return data is null ? null : MakeFile(LimbusAssetRoots.CacheRootKind, cacheRoot, data, AssetFileKind.UnityBundle);
        }

        if (steam.InstallPath is not string install)
        {
            return null;
        }

        string gameData = Path.Combine(install, LimbusAssetRoots.GameDataFolderName);
        var path = new FileInfo(Path.Combine(gameData, frame.File.Replace('/', Path.DirectorySeparatorChar)));
        return path.Exists ? MakeFile(LimbusAssetRoots.GameRootKind, gameData, path, SniffKind(path.FullName)) : null;
    }

    /// <summary>Searches the remembered name index for a file holding this picture (and its sheet, if known).</summary>
    private static ScannedFile? FindByName(LookFrame frame, SteamCheckResult steam, AssetCache cache, UnityAssetReader reader, Dictionary<string, AssetFileContents?> memo)
    {
        NameKind kind = frame.Kind == LookFrame.KindTexture ? NameKind.Texture : NameKind.Sprite;
        IReadOnlyList<AssetRoot> roots = LimbusAssetRoots.Find(steam);

        IEnumerable<CachedFile> matches = cache.Entries
            .Where(e => e.Names is not null && e.Names.Any(n => n.Kind == kind && n.Name == frame.Name))
            .OrderByDescending(e => frame.Sheet is not null && e.Names!.Any(n => n.Kind == NameKind.Texture && n.Name == frame.Sheet));

        foreach (CachedFile entry in matches)
        {
            if (!File.Exists(entry.FullPath))
            {
                continue;
            }

            AssetRoot? root = roots.FirstOrDefault(r => entry.FullPath.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
            if (root is null)
            {
                continue;
            }

            ScannedFile file = MakeFile(root.Kind, root.Path, new FileInfo(entry.FullPath), entry.Kind);
            if (Holds(Read(reader, file, memo), frame))
            {
                return file;
            }
        }

        return null;
    }

    private static ScannedFile MakeFile(string rootKind, string rootPath, FileInfo info, AssetFileKind kind)
    {
        string relative = Path.GetRelativePath(rootPath, info.FullName).Replace('\\', '/');
        return new ScannedFile
        {
            RootKind = rootKind,
            FullPath = info.FullName,
            RelativePath = relative,
            DisplayName = AssetScanner.MakeDisplayName(rootKind, relative),
            Size = info.Length,
            Kind = kind,
            Detail = string.Empty,
        };
    }

    private static AssetFileKind SniffKind(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] head = new byte[8];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return read >= 7 && System.Text.Encoding.ASCII.GetString(head, 0, 7) == "UnityFS"
                ? AssetFileKind.UnityBundle
                : AssetFileKind.UnitySerialized;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AssetFileKind.UnitySerialized;
        }
    }
}
