using System.Globalization;
using System.Text;
using LimbusFight.Animation;
using LimbusFight.Core;

namespace LimbusFight.Assets;

/// <summary>
/// Builds LimbusFight_asset_report.txt: file names, sizes and formats of the Limbus Company install,
/// plus a look inside a sample of bundles. Contains no game content (no images, sounds or text),
/// and the Windows user name is replaced with %USERPROFILE%.
/// </summary>
public static class AssetReport
{
    public const string FileName = "LimbusFight_asset_report.txt";

    /// <summary>How many bundles / serialized files get opened for the deep sample.</summary>
    public const int DeepSampleSize = 60;

    public static string Build(SteamCheckResult steam, AssetScanResult scan, UnityAssetReader reader, AssetCache? cache = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Limbus Fight asset scan report");
        sb.AppendLine($"Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        sb.AppendLine($"Version   : {AppInfo.Name} {AppInfo.Version}");
        sb.AppendLine("Contents  : file names, sizes and formats only. No images, sounds or text from the game.");
        sb.AppendLine();

        Section(sb, "Steam");
        sb.AppendLine($"Status     : {steam.Status} - {steam.Summary}");
        sb.AppendLine($"StateFlags : {steam.StateFlagsDisplay}");
        sb.AppendLine($"buildid    : {steam.BuildIdDisplay}");
        sb.AppendLine($"Install    : {Redact(steam.InstallPath)}");

        Section(sb, "Asset roots");
        foreach (AssetRoot root in scan.Roots)
        {
            List<ScannedFile> inRoot = scan.Files.Where(f => f.RootKind == root.Kind).ToList();
            sb.AppendLine($"[{root.Kind}] {Redact(root.Path)}");
            sb.AppendLine($"    exists: {(root.Exists ? "yes" : "NO")}   files: {inRoot.Count}   size: {Bytes(inRoot.Sum(f => f.Size))}");
        }

        sb.AppendLine($"Scan time: {scan.Elapsed.TotalSeconds:0.0} s   Game running: {(scan.GameWasRunning ? "YES (scan refused)" : "no")}");

        Section(sb, "Files by kind");
        foreach (IGrouping<string, ScannedFile> byRoot in scan.Files.GroupBy(f => f.RootKind))
        {
            sb.AppendLine($"[{byRoot.Key}]");
            foreach (IGrouping<string, ScannedFile> byDetail in byRoot.GroupBy(f => $"{f.Kind}: {CollapseVersion(f)}").OrderByDescending(g => g.Sum(f => f.Size)))
            {
                sb.AppendLine($"    {byDetail.Count(),7} files {Bytes(byDetail.Sum(f => f.Size)),10}   {byDetail.Key}");
            }
        }

        List<ScannedFile> bundles = scan.Files.Where(f => f.Kind == AssetFileKind.UnityBundle).ToList();
        Section(sb, "Unity bundles");
        sb.AppendLine($"Count: {bundles.Count}   Total: {Bytes(bundles.Sum(f => f.Size))}");
        AppendHistogram(sb, "Unity version", bundles.Select(f => f.UnityVersion ?? "?"));
        AppendHistogram(sb, "Compression", bundles.Select(f => f.Compression ?? "?"));
        AppendHistogram(sb, "Header flags", bundles.Select(f => $"0x{f.BundleFlags:X8}"));

        List<ScannedFile> unknown = scan.Files.Where(f => f.Kind == AssetFileKind.Other).ToList();
        Section(sb, $"Unidentified files ({unknown.Count})");
        AppendHistogram(sb, "Extension", unknown.Select(f => Path.GetExtension(f.RelativePath) is { Length: > 0 } e ? e.ToLowerInvariant() : "(none)"));
        foreach (ScannedFile file in unknown.OrderByDescending(f => f.Size).Take(40))
        {
            sb.AppendLine($"    {Bytes(file.Size),10}  {file.HeaderHex,-32}  [{file.RootKind}] {file.RelativePath}");
        }

        Section(sb, "Top-level folders");
        foreach (IGrouping<string, ScannedFile> byRoot in scan.Files.GroupBy(f => f.RootKind))
        {
            sb.AppendLine($"[{byRoot.Key}]");
            foreach (IGrouping<string, ScannedFile> folder in byRoot.GroupBy(f => FirstSegment(f.RelativePath)).OrderByDescending(g => g.Sum(f => f.Size)).Take(400))
            {
                sb.AppendLine($"    {folder.Count(),7} files {Bytes(folder.Sum(f => f.Size)),10}   {folder.Key}");
            }
        }

        Section(sb, "Name prefixes of browsable files");
        AppendHistogram(sb, "Prefix", scan.Browsable.Select(f => NamePrefix(f.DisplayName)), top: 80);

        if (cache is not null)
        {
            AppendNames(sb, scan, cache);
        }

        AppendDeepSample(sb, scan, reader, progress, cancel);

        if (cache is not null)
        {
            AppendSpriteSample(sb, scan, reader, cache, progress, cancel);
        }

        Section(sb, "All browsable files (name, size)");
        foreach (ScannedFile file in scan.Browsable.OrderBy(f => f.RootKind).ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).Take(20000))
        {
            sb.AppendLine($"    {Bytes(file.Size),10}  [{(file.RootKind == LimbusAssetRoots.CacheRootKind ? "cache" : "game")}] {file.DisplayName}");
        }

        if (scan.Errors.Count > 0)
        {
            Section(sb, "Scan errors");
            foreach (string error in scan.Errors)
            {
                sb.AppendLine("    " + Redact(error));
            }
        }

        return sb.ToString();
    }

    /// <summary>Writes the report next to the exe (or %TEMP% if that folder is read-only). Returns the path.</summary>
    public static string Write(string text)
    {
        foreach (string folder in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
        {
            try
            {
                string path = Path.Combine(folder, FileName);
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try the next folder
            }
        }

        throw new IOException("Could not write the asset report anywhere.");
    }

    /// <summary>
    /// How sprites are stored (atlas, packing, mesh) in a few sprite-heavy bundles, so sprite cutting
    /// can be checked against the real files. Names and numbers only.
    /// </summary>
    private static void AppendSpriteSample(StringBuilder sb, AssetScanResult scan, UnityAssetReader reader, AssetCache cache, IProgress<string>? progress, CancellationToken cancel)
    {
        List<(ScannedFile File, int Sprites, bool Atlas)> candidates = scan.Browsable
            .Select(f => (File: f, Names: cache.GetNames(f.FullPath) ?? Array.Empty<AssetName>()))
            .Select(x => (x.File,
                Sprites: x.Names.Count(n => n.Kind == NameKind.Sprite),
                Atlas: x.Names.Any(n => n.Kind == NameKind.Texture && n.Name.StartsWith("sactx", StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Sprites > 0)
            .ToList();

        Section(sb, $"Sprite sample ({candidates.Count} files contain sprites)");
        if (candidates.Count == 0)
        {
            sb.AppendLine("    (no sprites indexed yet - open the F3 browser and wait for \"Search ready\")");
            return;
        }

        // A spread of atlas bundles (largest first) plus a couple of non-atlas sprite files.
        List<ScannedFile> picks = candidates.Where(c => c.Atlas).OrderByDescending(c => c.Sprites).Take(6)
            .Concat(candidates.Where(c => !c.Atlas).OrderByDescending(c => c.Sprites).Take(2))
            .Select(c => c.File)
            .ToList();

        int done = 0;
        foreach (ScannedFile file in picks)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            progress?.Report($"Writing report: sprite sample {++done} / {picks.Count}...");
            int total = candidates.First(c => c.File == file).Sprites;
            sb.AppendLine($"  {file.DisplayName}  ({total} sprite{(total == 1 ? string.Empty : "s")})");

            IReadOnlyList<SpriteDiagnostics> sprites = reader.DescribeSprites(file, 12);
            foreach (SpriteDiagnostics s in sprites)
            {
                sb.AppendLine($"      {s.Name,-40} {s.RectWidth,5:0}x{s.RectHeight,-5:0} {s.Packing,-22} v{s.VertexCount,-4} t{s.TriangleCount,-4} uv:{(s.HasUvs ? "yes" : "no "),-4} {s.Source}");
                sb.AppendLine($"          mesh: {s.Channels}" + (s.Problem is null ? string.Empty : "   PROBLEM: " + s.Problem));
            }

            AppendAutoLook(sb, reader, file);
        }

        reader.Close();
    }

    /// <summary>What Ctrl+A would build from this file, and how each name was read, to tune the auto-detector.</summary>
    private static void AppendAutoLook(StringBuilder sb, UnityAssetReader reader, ScannedFile file)
    {
        try
        {
            AssetFileContents contents = reader.ReadContents(file);
            var candidates = contents.Sprites.Select(sp => new PoseCandidate(sp.Name, sp.Width, sp.Height, sp.PivotX, sp.PivotY, sp.Atlas)).ToList();
            AutoLookResult result = AutoLook.Build(candidates, (pose, hold) => new LookFrame { File = "x", Name = pose.Name, Hold = hold });
            sb.AppendLine($"      auto look: {result.Summary}");
            foreach (AutoPick pick in result.Picks.Take(24))
            {
                sb.AppendLine($"          {LookData.ClipKey(pick.Clip),-6} {pick.Name,-44} {pick.Reason}");
            }

            var roles = candidates
                .Select(c => AutoLook.SplitFrameNumber(c.Name).Base)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(b => (Base: b, Read: AutoLook.Classify(b)))
                .Take(60)
                .Select(x => $"{x.Base}={x.Read.Role}{(x.Read.Word.Length > 0 ? "(" + x.Read.Word + ")" : string.Empty)}");
            sb.AppendLine("      names read as: " + string.Join(", ", roles));
            int unique = candidates.Count(c => c.PivotY <= 0.2f);
            sb.AppendLine($"      pivots at the feet: {unique} of {candidates.Count}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            sb.AppendLine($"      auto look: failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static void AppendDeepSample(StringBuilder sb, AssetScanResult scan, UnityAssetReader reader, IProgress<string>? progress, CancellationToken cancel)
    {
        List<ScannedFile> browsable = scan.Browsable.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        var sample = new List<ScannedFile>();
        sample.AddRange(browsable.OrderByDescending(f => f.Size).Take(DeepSampleSize / 4));

        int spread = DeepSampleSize - sample.Count;
        if (browsable.Count > 0 && spread > 0)
        {
            double step = Math.Max(1.0, (double)browsable.Count / spread);
            for (double i = 0; i < browsable.Count && sample.Count < DeepSampleSize; i += step)
            {
                ScannedFile pick = browsable[(int)i];
                if (!sample.Contains(pick))
                {
                    sample.Add(pick);
                }
            }
        }

        Section(sb, $"Deep sample: {sample.Count} of {browsable.Count} browsable files opened");
        var formatTotals = new Dictionary<string, int>();
        int withTypeTree = 0;

        for (int i = 0; i < sample.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            ScannedFile file = sample[i];
            progress?.Report($"Looking inside {i + 1}/{sample.Count}: {file.DisplayName}");

            AssetFileContents contents = reader.ReadContents(file);
            if (contents.TypeTreeEnabled)
            {
                withTypeTree++;
            }

            sb.AppendLine($"* {file.DisplayName}   ({Bytes(file.Size)}, {file.Detail})");
            sb.AppendLine($"    Unity {contents.UnityVersion ?? "?"}   type tree: {(contents.TypeTreeEnabled ? "yes" : "NO")}");

            string classes = string.Join(", ", contents.ClassCounts.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value}"));
            sb.AppendLine($"    objects: {(classes.Length > 0 ? classes : "(none)")}");

            if (contents.Textures.Count > 0)
            {
                foreach (IGrouping<string, TextureInfo> group in contents.Textures.GroupBy(t => t.FormatName))
                {
                    formatTotals[group.Key] = formatTotals.TryGetValue(group.Key, out int n) ? n + group.Count() : group.Count();
                }

                sb.AppendLine($"    textures ({contents.Textures.Count}): " + string.Join(", ",
                    contents.Textures.Take(8).Select(t => $"{t.Name} {t.Width}x{t.Height} {t.FormatName}")));
            }

            foreach (string error in contents.Errors.Take(3))
            {
                sb.AppendLine($"    ! {error}");
            }
        }

        reader.Close();

        sb.AppendLine();
        sb.AppendLine($"Files with type trees: {withTypeTree} of {sample.Count}");
        sb.AppendLine("Texture formats in sample: " + string.Join(", ", formatTotals.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
    }

    private static void AppendNames(StringBuilder sb, AssetScanResult scan, AssetCache cache)
    {
        List<ScannedFile> browsable = scan.Browsable.ToList();
        List<AssetName[]> indexed = browsable.Select(f => cache.GetNames(f.FullPath)).OfType<AssetName[]>().ToList();
        List<AssetName> names = indexed.SelectMany(n => n).ToList();

        Section(sb, $"Object names ({indexed.Count} of {browsable.Count} files indexed)");
        foreach (NameKind kind in Enum.GetValues<NameKind>())
        {
            List<string> ofKind = names.Where(n => n.Kind == kind).Select(n => n.Name).ToList();
            sb.AppendLine($"{kind}: {ofKind.Count} objects, {ofKind.Distinct(StringComparer.Ordinal).Count()} unique names");
        }

        sb.AppendLine($"Files with no textures, sprites or sounds: {indexed.Count(n => n.Length == 0)}");

        foreach (NameKind kind in Enum.GetValues<NameKind>())
        {
            Section(sb, $"{kind} name prefixes");
            AppendHistogram(sb, "Prefix", names.Where(n => n.Kind == kind).Select(n => AssetIndexer.Prefix(n.Name)), top: 200);
        }

        foreach ((NameKind kind, int cap) in new[] { (NameKind.Texture, 20000), (NameKind.Sprite, 20000), (NameKind.Audio, 6000) })
        {
            List<string> unique = names.Where(n => n.Kind == kind).Select(n => n.Name).Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            Section(sb, $"All {kind} names ({unique.Count} unique{(unique.Count > cap ? $", first {cap}" : string.Empty)})");
            foreach (string name in unique.Take(cap))
            {
                sb.AppendLine("    " + name);
            }
        }
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine("== " + title + " " + new string('=', Math.Max(4, 70 - title.Length)));
    }

    private static void AppendHistogram(StringBuilder sb, string label, IEnumerable<string> values, int top = 30)
    {
        List<IGrouping<string, string>> groups = values.GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0)
        {
            sb.AppendLine($"{label}: (none)");
            return;
        }

        sb.AppendLine($"{label}:");
        foreach (IGrouping<string, string> group in groups.Take(top))
        {
            sb.AppendLine($"    {group.Count(),7}  {group.Key}");
        }

        if (groups.Count > top)
        {
            sb.AppendLine($"    ... and {groups.Count - top} more");
        }
    }

    private static string CollapseVersion(ScannedFile file) =>
        file.Kind == AssetFileKind.UnityBundle ? $"UnityFS {file.UnityVersion} {file.Compression}" : file.Detail;

    private static string FirstSegment(string relative)
    {
        int slash = relative.IndexOf('/');
        return slash < 0 ? "(files in root)" : relative[..slash];
    }

    /// <summary>"battle_sinner_10101" -> "battle". Groups bundles by their naming scheme.</summary>
    public static string NamePrefix(string displayName)
    {
        string name = displayName.Replace('\\', '/');
        int cut = name.IndexOfAny(new[] { '_', '-', '.', '/', ' ' });
        string prefix = cut > 0 ? name[..cut] : name;
        prefix = new string(prefix.TakeWhile(c => !char.IsAsciiDigit(c)).ToArray());
        return prefix.Length > 0 ? prefix.ToLowerInvariant() : "(digits)";
    }

    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : value.ToString(unit >= 3 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static string Redact(string? path) => path is null ? "n/a" : LimbusAssetRoots.Redact(path);
}
