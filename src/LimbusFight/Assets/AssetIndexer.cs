namespace LimbusFight.Assets;

/// <summary>
/// Reads the names of the textures, sprites and sounds inside every bundle, so the asset browser
/// can search by name even though Limbus Company's bundle files have hash names.
/// Only new or changed files are read; everything else comes from <see cref="AssetCache"/>.
/// </summary>
public static class AssetIndexer
{
    /// <summary>Indexes browsable files the cache has no names for. Returns how many were indexed.</summary>
    public static int IndexMissing(
        IReadOnlyList<ScannedFile> files,
        AssetCache cache,
        UnityAssetReader reader,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancel = default)
    {
        List<ScannedFile> todo = files.Where(f => f.IsBrowsable && cache.GetNames(f.FullPath) is null).ToList();
        int done = 0;
        progress?.Report((0, todo.Count));

        try
        {
            foreach (ScannedFile file in todo)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                AssetName[] names;
                try
                {
                    names = reader.ReadNames(file);
                }
                catch (InvalidOperationException) when (LimbusAssetRoots.IsGameRunning())
                {
                    break; // the game was started: stop reading its files immediately
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    names = Array.Empty<AssetName>(); // unreadable file: remember that, don't retry every run
                }

                cache.SetNames(file.FullPath, names);
                done++;

                if (done % 10 == 0 || done == todo.Count)
                {
                    progress?.Report((done, todo.Count));
                }
            }
        }
        finally
        {
            reader.Close();
        }

        return done;
    }

    /// <summary>"YiSang_Idle_01" -> "yisang". Used to group names in the report.</summary>
    public static string Prefix(string name)
    {
        int cut = name.IndexOfAny(new[] { '_', '-', ' ', '.', '(' });
        string head = cut > 0 ? name[..cut] : name;
        return head.ToLowerInvariant();
    }
}
