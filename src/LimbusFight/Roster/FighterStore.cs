using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LimbusFight.Animation;

namespace LimbusFight.Roster;

/// <summary>A fighter file on disk: its id (file name without .json) and display name.</summary>
public sealed record FighterEntry(string Id, string Name);

/// <summary>Which fighter is in each slot; saved as roster.json next to the fighter files.</summary>
public sealed class RosterSettings
{
    public string Player1 { get; set; } = FighterStore.DefaultPlayerId;

    public string Dummy { get; set; } = FighterStore.DefaultDummyId;
}

/// <summary>
/// Reads and writes fighter files in %LOCALAPPDATA%\LimbusFight\fighters (one JSON file per fighter)
/// and roster.json. Never throws for missing or damaged files: they are set aside as .bad and a
/// default fighter takes their place.
/// </summary>
public static class FighterStore
{
    public const string DefaultPlayerId = "player-1";
    public const string DefaultDummyId = "training-dummy";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>%LOCALAPPDATA%\LimbusFight\fighters (overridable for tests).</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimbusFight", "fighters");

    private static string SettingsPath => Path.Combine(Folder, "roster.json");

    public static string PathFor(string id) => Path.Combine(Folder, id + ".json");

    public static string Serialize(FighterData data) => JsonSerializer.Serialize(data, Json);

    public static FighterData Deserialize(string json)
    {
        FighterData data = JsonSerializer.Deserialize<FighterData>(json, Json) ?? new FighterData();
        data.Sanitise();
        return data;
    }

    /// <summary>Every fighter file, sorted by name.</summary>
    public static List<FighterEntry> List()
    {
        var result = new List<FighterEntry>();
        if (!Directory.Exists(Folder))
        {
            return result;
        }

        foreach (string path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (id.Equals("roster", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = id;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String)
                {
                    name = n.GetString() ?? id;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                name = id + " (damaged)";
            }

            result.Add(new FighterEntry(id, name));
        }

        return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    public static FighterData Load(string id, out string? problem)
    {
        problem = null;
        string path = PathFor(id);
        if (!File.Exists(path))
        {
            problem = $"Fighter file {id}.json is missing; using a training box.";
            return new FighterData();
        }

        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"{id}.json could not be read ({ex.Message}); it was renamed to {id}.json.bad.";
            TryMove(path, path + ".bad");
            return new FighterData();
        }
    }

    /// <summary>Saves atomically. Returns an error message, or null on success.</summary>
    public static string? Save(string id, FighterData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = PathFor(id);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(data), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {id}.json: {ex.Message}";
        }
    }

    /// <summary>A file id for a new fighter: the name in lower-case-with-dashes, made unique.</summary>
    public static string NewId(string name)
    {
        string slug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        if (slug.Length == 0 || slug == "roster")
        {
            slug = "fighter";
        }

        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        string id = slug;
        for (int n = 2; File.Exists(PathFor(id)); n++)
        {
            id = $"{slug}-{n}";
        }

        return id;
    }

    public static RosterSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<RosterSettings>(File.ReadAllText(SettingsPath), Json) ?? new RosterSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // fall through to defaults
        }

        return new RosterSettings();
    }

    public static void SaveSettings(RosterSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the slot choice is a convenience only
        }
    }

    /// <summary>
    /// First run of Step 4: creates the two default fighters. Looks made in Step 3 (looks\p1.json,
    /// looks\dummy.json) move into them, so nothing the player set up is lost.
    /// </summary>
    public static void EnsureDefaults()
    {
        if (!File.Exists(PathFor(DefaultPlayerId)))
        {
            var player = new FighterData { Name = "Player 1" };
            player.Look = MigrateLook(LookSlot.Player1) ?? player.Look;
            Save(DefaultPlayerId, player);
        }

        if (!File.Exists(PathFor(DefaultDummyId)))
        {
            var dummy = new FighterData { Name = "Training Dummy" };
            dummy.Look = MigrateLook(LookSlot.Dummy) ?? dummy.Look;
            Save(DefaultDummyId, dummy);
        }
    }

    private static LookData? MigrateLook(LookSlot slot)
    {
        string old = LookStore.PathFor(slot);
        if (!File.Exists(old))
        {
            return null;
        }

        LookData look = LookStore.Load(slot, out string? problem);
        if (problem is null)
        {
            TryMove(old, old + ".moved-to-fighters");
        }

        return look;
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // keep going
        }
    }
}
