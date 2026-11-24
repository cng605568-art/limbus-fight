using System.Text.Json;
using System.Text.Json.Serialization;
using LimbusFight.Gameplay;

namespace LimbusFight.Core;

/// <summary>
/// Player options, saved in %LOCALAPPDATA%\LimbusFight\settings.json. Every value is clamped on load
/// so a hand-edited file cannot break the game.
/// </summary>
public sealed class GameSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Round lengths the options screen cycles through; 0 = no timer.</summary>
    public static readonly int[] RoundSecondChoices = { 30, 60, 99, 0 };

    /// <summary>%LOCALAPPDATA%\LimbusFight (overridable for tests).</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimbusFight");

    public static string PathFor() => Path.Combine(Folder, "settings.json");

    /// <summary>CPU strength in Versus CPU and Arcade.</summary>
    public CpuLevel CpuLevel { get; set; } = CpuLevel.Normal;

    /// <summary>Rounds needed to win a match (1-3; 2 = best of 3).</summary>
    public int RoundsToWin { get; set; } = 2;

    /// <summary>Seconds per round; 0 = no timer.</summary>
    public int RoundSeconds { get; set; } = 60;

    /// <summary>Start in fullscreen (borderless, desktop size).</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Window size as a multiple of 1280x720 (0.5 - 2) when not fullscreen.</summary>
    public float WindowScale { get; set; } = 1f;

    /// <summary>Which player the first controller plays as in Versus (1 or 2).</summary>
    public int Pad1Player { get; set; } = 1;

    [JsonIgnore]
    public int RoundFrames => RoundSeconds * 60;

    public void Sanitise()
    {
        CpuLevel = Enum.IsDefined(CpuLevel) ? CpuLevel : CpuLevel.Normal;
        RoundsToWin = Math.Clamp(RoundsToWin, 1, 3);
        RoundSeconds = RoundSecondChoices.Contains(RoundSeconds) ? RoundSeconds : 60;
        WindowScale = float.IsFinite(WindowScale) ? Math.Clamp(WindowScale, 0.5f, 2f) : 1f;
        Pad1Player = Pad1Player == 2 ? 2 : 1;
    }

    public static GameSettings Load()
    {
        try
        {
            string path = PathFor();
            if (File.Exists(path))
            {
                GameSettings? loaded = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), Json);
                if (loaded is not null)
                {
                    loaded.Sanitise();
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // damaged or locked: defaults
        }

        return new GameSettings();
    }

    /// <summary>Saves; returns a problem message or null.</summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor();
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
            File.Move(path + ".tmp", path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not save settings: " + ex.Message;
        }
    }

    public static string Describe(CpuLevel level) => level switch
    {
        CpuLevel.Easy => "Easy",
        CpuLevel.Normal => "Normal",
        _ => "Hard",
    };
}
