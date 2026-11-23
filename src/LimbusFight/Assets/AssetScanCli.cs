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

        var progress = new ConsoleProgress();
        AssetScanResult scan = AssetScanner.Scan(roots, progress);
        Console.WriteLine();

        if (scan.GameWasRunning)
        {
            Console.WriteLine("Limbus Company is running. Close it and run the scan again.");
            return 2;
        }

        using var reader = new UnityAssetReader();
        string text = AssetReport.Build(steam, scan, reader, new ConsoleStatus());
        string path = AssetReport.Write(text);

        Console.WriteLine();
        Console.WriteLine($"{scan.Files.Count} files checked. Report written to:");
        Console.WriteLine("  " + path);
        return 0;
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
