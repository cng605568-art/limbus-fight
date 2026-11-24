using System.Numerics;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Core;
using LimbusFight.Gameplay;
using LimbusFight.Input;
using LimbusFight.Rendering;
using LimbusFight.Roster;
using LimbusFight.UI;
using Raylib_cs;

namespace LimbusFight;

/// <summary>
/// Entry point: a 60 FPS Raylib window with the Limbus Company Steam status, the fighting sandbox
/// (Step 1), the F3 asset browser (Step 2) and fighter looks made of the player's own Limbus
/// sprites with the F4 look editor (Step 3), and fighters as data files with the F2 editor (Step 4). Everything is drawn to a fixed 1280x720
/// canvas that is scaled (letterboxed) to the window, so coordinates never depend on window size.
/// </summary>
internal static class Program
{
    public const int VirtualWidth = Viewport.Width;
    public const int VirtualHeight = Viewport.Height;
    public const int TargetFps = 60;

    /// <summary>How often the red warning screen re-reads Steam's manifest by itself.</summary>
    private const double AutoRescanSeconds = 5.0;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                return SelfTest.Run();
            }

            if (args.Any(a => a.Equals("--scan-assets", StringComparison.OrdinalIgnoreCase)))
            {
                return AssetScanCli.Run();
            }

            RunGame();
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return 1;
        }
    }

    private static void RunGame()
    {
        // Steam scan first: it is pure file/registry IO and is logged even if the window fails.
        SteamCheckResult steam = SteamChecker.Check();
        Console.WriteLine($"[LimbusFight] {AppInfo.Version} | Steam: {steam.Status} - {steam.Summary}");

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(VirtualWidth, VirtualHeight, $"{AppInfo.Name} v{AppInfo.Version}");

        if (!Raylib.IsWindowReady())
        {
            throw new InvalidOperationException("Raylib could not create a window (is an OpenGL 3.3 capable GPU driver installed?).");
        }

        Raylib.SetWindowMinSize(VirtualWidth / 2, VirtualHeight / 2);
        Raylib.SetTargetFPS(TargetFps);
        Raylib.SetExitKey(KeyboardKey.Null); // Esc pauses / goes back; the window close button quits

        new GameApp(steam).Run();
        Raylib.CloseWindow();
    }
}
