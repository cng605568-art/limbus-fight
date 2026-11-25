using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LimbusFight.Animation;

/// <summary>The five pose clips a test fighter needs in Step 3, plus two effect clips.</summary>
public enum ClipId
{
    Idle,
    Walk,
    Jump,
    Punch,
    Hit,

    /// <summary>Effect drawn on the hitbox while the punch is out (e.g. a slash).</summary>
    SwingFx,

    /// <summary>Effect drawn where a hit lands.</summary>
    HitFx,

    // ---- Step 6 (appended so older look files and Ctrl+1..7 keep their meaning) -------------
    Skill2,
    Skill3,
    Guard,
    Evade,
    Ego,
    Skill2Fx,
    Skill3Fx,
    EgoFx,
}

/// <summary>Which fighter a look belongs to.</summary>
public enum LookSlot
{
    Player1,
    Dummy,
}

/// <summary>
/// One frame of a clip: a reference (names only, never pixels) to a sprite or texture inside the
/// player's own Limbus Company files, plus how many 60 Hz frames it stays on screen.
/// </summary>
public sealed class LookFrame
{
    public const string RootCache = "cache";
    public const string RootGame = "game";
    public const string KindSprite = "sprite";
    public const string KindTexture = "texture";

    /// <summary>"cache" (downloaded bundles in LocalLow) or "game" (LimbusCompany_Data).</summary>
    public string Root { get; set; } = RootCache;

    /// <summary>Cache: the bundle's folder name. Game: the path inside LimbusCompany_Data.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>"sprite" or "texture".</summary>
    public string Kind { get; set; } = KindSprite;

    /// <summary>The sprite's or texture's name inside the file.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sprites: the atlas sheet it sits on (tells same-named sprites apart).</summary>
    public string? Sheet { get; set; }

    /// <summary>Sprites: the SpriteAtlas (character) it belongs to. Bundles can hold several characters
    /// with the same sprite names, so this keeps a look from mixing them.</summary>
    public string? Atlas { get; set; }

    /// <summary>How many 60 Hz frames this frame is shown (idle, walk and hit clips).</summary>
    public int Hold { get; set; } = 8;

    /// <summary>Why this frame is here, e.g. "auto: name says 'attack'" (shown in the F4 editor).</summary>
    public string? Note { get; set; }

    /// <summary>Identifies the picture regardless of timing; frames with the same key share one texture.</summary>
    [JsonIgnore]
    public string Key => $"{Root}|{File}|{Kind}|{Name}|{Sheet}|{Atlas}";

    public LookFrame Clone() => new() { Root = Root, File = File, Kind = Kind, Name = Name, Sheet = Sheet, Atlas = Atlas, Hold = Hold, Note = Note };
}

/// <summary>
/// How one fighter looks: which pictures make up each clip, how big they are drawn and which way
/// the art faces. Saved as JSON in %LOCALAPPDATA%\LimbusFight\looks (p1.json / dummy.json).
/// </summary>
public sealed class LookData
{
    public const int CurrentVersion = 1;

    /// <summary>Drawn height (stage pixels) of the first idle frame when <see cref="Scale"/> is automatic.</summary>
    public const float AutoHeight = 230f;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Drawing scale; 0 or less means automatic (see <see cref="AutoHeight"/>).</summary>
    public float Scale { get; set; }

    /// <summary>True if the pictures face left; they are mirrored so the fighter faces its opponent.</summary>
    public bool ArtFacesLeft { get; set; }

    /// <summary>
    /// False (default): each picture stands on its bottom edge, lined up sideways by its pivot.
    /// True: Unity's pivot is placed exactly on the fighter's feet (best when the art's pivot is at the feet).
    /// </summary>
    public bool AnchorAtPivot { get; set; }

    /// <summary>Nudge (stage pixels) applied when drawing, for art whose pivot is not at the feet.</summary>
    public float OffsetX { get; set; }

    public float OffsetY { get; set; }

    /// <summary>Clip name ("idle", "walk", "jump", "punch", "hit") to its frames.</summary>
    public Dictionary<string, List<LookFrame>> Clips { get; set; } = new();

    public static string ClipKey(ClipId clip) => clip.ToString().ToLowerInvariant();

    public List<LookFrame> Frames(ClipId clip) =>
        Clips.TryGetValue(ClipKey(clip), out List<LookFrame>? frames) ? frames : new List<LookFrame>();

    /// <summary>The frame list for editing (created if missing).</summary>
    public List<LookFrame> EditFrames(ClipId clip)
    {
        string key = ClipKey(clip);
        if (!Clips.TryGetValue(key, out List<LookFrame>? frames))
        {
            frames = new List<LookFrame>();
            Clips[key] = frames;
        }

        return frames;
    }

    [JsonIgnore]
    public bool IsEmpty => Clips.Values.All(c => c.Count == 0);

    [JsonIgnore]
    public IEnumerable<LookFrame> AllFrames => Clips.Values.SelectMany(c => c);

    /// <summary>Default hold for a new frame of <paramref name="clip"/>.</summary>
    public static int DefaultHold(ClipId clip) => clip switch
    {
        ClipId.Idle => 12,
        ClipId.Walk => 8,
        ClipId.Hit => 8,
        ClipId.SwingFx or ClipId.HitFx or ClipId.Skill2Fx or ClipId.Skill3Fx or ClipId.EgoFx => 3,
        _ => 6,
    };

    /// <summary>True for the effect clips (drawn on top of the fighters, not as a pose).</summary>
    public static bool IsEffect(ClipId clip) => clip is ClipId.SwingFx or ClipId.HitFx or ClipId.Skill2Fx or ClipId.Skill3Fx or ClipId.EgoFx;

    /// <summary>Clips whose frames follow a move's startup / active / recovery.</summary>
    public static bool IsMove(ClipId clip) => clip is ClipId.Punch or ClipId.Skill2 or ClipId.Skill3 or ClipId.Ego;

    /// <summary>Frames of the pose clips only (no effects).</summary>
    [JsonIgnore]
    public IEnumerable<LookFrame> PoseFrames =>
        Clips.Where(kv => !kv.Key.EndsWith("fx", StringComparison.Ordinal)).SelectMany(kv => kv.Value);

    /// <summary>Drops broken entries and clamps numbers after loading a hand-edited file.</summary>
    public void Sanitise()
    {
        Clips ??= new Dictionary<string, List<LookFrame>>();
        foreach (string key in Clips.Keys.ToList())
        {
            List<LookFrame> frames = Clips[key] ?? new List<LookFrame>();
            frames.RemoveAll(f => f is null || string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.File));
            foreach (LookFrame f in frames)
            {
                f.Hold = Math.Clamp(f.Hold, 1, 600);
                f.Root = f.Root == LookFrame.RootGame ? LookFrame.RootGame : LookFrame.RootCache;
                f.Kind = f.Kind == LookFrame.KindTexture ? LookFrame.KindTexture : LookFrame.KindSprite;
            }

            Clips[key] = frames;
        }

        if (!float.IsFinite(Scale) || Scale > 20f)
        {
            Scale = 0f;
        }

        OffsetX = float.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -500f, 500f) : 0f;
        OffsetY = float.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -500f, 500f) : 0f;
    }
}

/// <summary>Reads and writes look files. Never throws for a missing or damaged file.</summary>
public static class LookStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(LookData data) => JsonSerializer.Serialize(data, Json);

    public static LookData Deserialize(string json)
    {
        LookData data = JsonSerializer.Deserialize<LookData>(json, Json) ?? new LookData();
        data.Sanitise();
        return data;
    }

    /// <summary>%LOCALAPPDATA%\LimbusFight\looks</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LimbusFight", "looks");

    public static string PathFor(LookSlot slot) => Path.Combine(Folder, slot == LookSlot.Player1 ? "p1.json" : "dummy.json");

    /// <summary>Loads a slot's look; a missing file gives an empty look, a damaged one is set aside as .bad.</summary>
    public static LookData Load(LookSlot slot, out string? problem)
    {
        problem = null;
        string path = PathFor(slot);
        if (!File.Exists(path))
        {
            return new LookData();
        }

        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"{Path.GetFileName(path)} could not be read ({ex.Message}); starting a new look.";
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // keep going with an empty look
            }

            return new LookData();
        }
    }

    /// <summary>Saves atomically (temp file, then replace). Returns an error message, or null on success.</summary>
    public static string? Save(LookSlot slot, LookData data)
    {
        string path = PathFor(slot);
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Serialize(data), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {Path.GetFileName(path)}: {ex.Message}";
        }
    }
}
