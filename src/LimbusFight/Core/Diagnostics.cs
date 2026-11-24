using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;

namespace LimbusFight.Core;

/// <summary>Writes LimbusFight_crash.log when something goes wrong before or during the game loop.</summary>
public static class CrashLog
{
    public const string FileName = "LimbusFight_crash.log";

    public static string Write(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.Name} {AppInfo.Version} crashed at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Exe folder: {AppContext.BaseDirectory}");
        sb.AppendLine();
        sb.AppendLine(ex.ToString());

        string text = sb.ToString();
        Console.Error.WriteLine(text);

        foreach (string folder in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
        {
            try
            {
                string path = Path.Combine(folder, FileName);
                File.WriteAllText(path, text);
                Console.Error.WriteLine($"Crash log written to: {path}");
                return path;
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Exe folder may be read-only (e.g. Program Files); fall back to %TEMP%.
            }
        }

        return string.Empty;
    }
}

/// <summary>
/// Headless self-test (<c>LimbusFight.exe --selftest</c>). Runs without opening a window, so CI can
/// prove the published single-file exe starts, unpacks raylib.dll and can load it.
/// Exit code 0 = pass, 1 = raylib native library missing or broken.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} self-test");
        Console.WriteLine($"OS      : {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        Console.WriteLine($"Runtime : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Exe dir : {AppContext.BaseDirectory}");
        Console.WriteLine();

        Console.WriteLine("[Steam] Limbus Company scan");
        SteamCheckResult steam = SteamChecker.Check();
        Console.Write(SteamChecker.Describe(steam));
        Console.WriteLine();

        Console.WriteLine("[Raylib] native library probe");
        bool raylibOk = ProbeRaylib();
        Console.WriteLine();

        Console.WriteLine("[Assets] bundle reader + texture decoder probe");
        bool assetsOk = ProbeAssetLibraries();
        Console.WriteLine();

        Console.WriteLine("[Looks] look file + sprite cutter probe");
        bool looksOk = ProbeLooks();
        Console.WriteLine();

        bool passed = raylibOk && assetsOk && looksOk;
        Console.WriteLine(passed ? "SELFTEST PASSED" : "SELFTEST FAILED");
        return passed ? 0 : 1;
    }

    /// <summary>Proves AssetsTools.NET and the texture decoders made it into the single-file exe.</summary>
    private static bool ProbeAssetLibraries()
    {
        try
        {
            _ = new AssetsTools.NET.Extra.AssetsManager();
            Console.WriteLine("  AssetsTools.NET     : ok");

            // One 4x4 DXT1 block, both colours pure red (RGB565 0xF800).
            byte[] block = { 0x00, 0xF8, 0x00, 0xF8, 0, 0, 0, 0 };
            bool decoded = LimbusFight.Assets.TextureDecoding.TryDecode(10, block, 4, 4, out byte[] rgba, out string error);
            bool red = decoded && rgba[0] == 255 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 255;
            Console.WriteLine($"  DXT1 test decode    : {(red ? "ok" : "FAILED " + error)}");
            return red;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Proves look files survive a save / load round trip and the sprite cutter runs (no files touched).</summary>
    private static bool ProbeLooks()
    {
        try
        {
            var look = new LimbusFight.Animation.LookData { Scale = 0.5f, ArtFacesLeft = true };
            look.EditFrames(LimbusFight.Animation.ClipId.Punch).Add(new LimbusFight.Animation.LookFrame { File = "bundle", Name = "pose", Sheet = "sheet", Hold = 7 });
            var back = LimbusFight.Animation.LookStore.Deserialize(LimbusFight.Animation.LookStore.Serialize(look));
            var frame = back.Frames(LimbusFight.Animation.ClipId.Punch).SingleOrDefault();
            bool jsonOk = back.Scale == 0.5f && back.ArtFacesLeft && frame is { Name: "pose", Sheet: "sheet", Hold: 7 };
            Console.WriteLine($"  look JSON round trip: {(jsonOk ? "ok" : "FAILED")}");

            // 2x2 sheet, cut its top-right pixel as a 1x1 rectangle sprite.
            byte[] sheet = { 1, 1, 1, 255, 2, 2, 2, 255, 3, 3, 3, 255, 4, 4, 4, 255 };
            var g = new LimbusFight.Assets.SpriteGeometry { RectWidth = 1, RectHeight = 1, TexRectX = 1, TexRectY = 1, TexRectWidth = 1, TexRectHeight = 1, SettingsRaw = 1u | 2u };
            var cut = LimbusFight.Assets.SpriteCutter.Cut(g, sheet, 2, 2);
            bool cutOk = cut.Width == 1 && cut.Height == 1 && cut.Rgba[0] == 2;
            Console.WriteLine($"  sprite cutter       : {(cutOk ? "ok" : "FAILED")}");
            return jsonOk && cutOk;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool ProbeRaylib()
    {
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string searchDirs)
        {
            foreach (string dir in searchDirs.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                Console.WriteLine($"  search dir : {dir}");
            }
        }

        if (!NativeLibrary.TryLoad(Raylib.NativeLibName, typeof(Raylib).Assembly, null, out IntPtr handle))
        {
            Console.WriteLine($"  FAILED to load native library '{Raylib.NativeLibName}'.");
            return false;
        }

        string[] requiredExports = { "InitWindow", "SetTargetFPS", "BeginDrawing", "DrawText", "CloseWindow" };
        bool allExports = true;

        foreach (string export in requiredExports)
        {
            bool found = NativeLibrary.TryGetExport(handle, export, out _);
            allExports &= found;
            Console.WriteLine($"  export {export,-14}: {(found ? "ok" : "MISSING")}");
        }

        Console.WriteLine($"  '{Raylib.NativeLibName}' loaded: {(allExports ? "OK" : "INCOMPLETE")}");
        return allExports;
    }
}
