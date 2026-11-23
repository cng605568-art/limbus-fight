using LimbusFight.Core;

namespace LimbusFight.Assets;

/// <summary>
/// <c>LimbusFight.exe --scan-assets</c>: scans Limbus Company's files without opening a window
/// and writes LimbusFight_asset_report.txt next to the exe.
/// </summary>
public static class AssetScanCli
{
    public static int Run()
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} asset scan");

        SteamCheckResult steam = SteamChecker.Check();
        Console.WriteLine($"Steam: {steam.Status} - {steam.Summary}");

        IReadOnlyList<AssetRoot> roots = LimbusAssetRoots.Find(steam);
        foreach (AssetRoot root in roots)
        {
            Console.WriteLine($"  [{root.Kind}] {LimbusAssetRoots.Redact(root.Path)}  {(root.Exists ? "found" : "NOT FOUND")}");
        }

        AssetCache cache = AssetCache.Load();
        AssetScanResult scan = AssetScanner.Scan(roots, cache, new ConsoleProgress());
        Console.WriteLine();

        if (scan.GameWasRunning)
        {
            Console.WriteLine("Limbus Company is running. Close it and run the scan again.");
            return 2;
        }

        using var reader = new UnityAssetReader();
        int indexed = AssetIndexer.IndexMissing(scan.Files, cache, reader, new IndexProgress());
        Console.WriteLine();
        Console.WriteLine($"  {indexed} files indexed this run ({cache.Count} files remembered)");
        SaveCache(cache);

        string text = AssetReport.Build(steam, scan, reader, cache, new ConsoleStatus());
        string path = AssetReport.Write(text);

        Console.WriteLine();
        Console.WriteLine($"{scan.Files.Count} files checked. Report written to:");
        Console.WriteLine("  " + path);
        return 0;
    }

    private static void SaveCache(AssetCache cache)
    {
        try
        {
            cache.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("  (could not save the index cache: " + ex.Message + ")");
        }
    }

    private sealed class IndexProgress : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => Console.Write($"\r  reading names {value.Done}/{value.Total}   ");
    }

    private sealed class ConsoleProgress : IProgress<int>
    {
        public void Report(int value) => Console.Write($"\r  {value} files checked");
    }

    private sealed class ConsoleStatus : IProgress<string>
    {
        public void Report(string value) => Console.WriteLine("  " + value);
    }
}
