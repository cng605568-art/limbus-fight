using System.Text.RegularExpressions;

namespace LimbusFight.Animation;

/// <summary>
/// Keeps automatic looks to ONE character. A Limbus bundle can hold several characters' sprite
/// atlases (for example Faust's and Don Quixote's) whose sprites have the same names ("Move",
/// "Damaged", "skill1_1"), so sprites are split by atlas ("family") first and each family is
/// judged on its own. Also skips pictures that are not whole poses: single layers of layered
/// characters ("idle_f1_l2_p3_n") and numbered portraits ("10315_normal", "1031502").
/// Pure logic, no Raylib.
/// </summary>
public static partial class LookFamilies
{
    /// <summary>One atlas worth of sprites (Atlas null = sprites with their own textures).</summary>
    public sealed record Family(string? Atlas, List<PoseCandidate> Sprites);

    /// <summary>The look picked for one family, with how good it is.</summary>
    public sealed record Pick(AutoLookResult Result, string? Atlas, int Rating, int Match);

    [GeneratedRegex(@"_f\d+_l\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LayerPattern();

    [GeneratedRegex(@"^\d{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedPattern();

    [GeneratedRegex(@"^(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();

    /// <summary>"idle_f1_l2_p3_n": one layer of a layered pose; alone it is only a piece of the body.</summary>
    public static bool IsLayerPiece(string name) => LayerPattern().IsMatch(name);

    /// <summary>"10315_normal", "1031502": Identity portraits and cards, not battle poses.</summary>
    public static bool IsNumberedPicture(string name) => NumberedPattern().IsMatch(name);

    /// <summary>Sprites grouped by atlas, biggest first.</summary>
    public static List<Family> Split(IEnumerable<PoseCandidate> all) => all
        .GroupBy(c => c.Group ?? string.Empty)
        .Select(g => new Family(g.Key.Length == 0 ? null : g.Key, g.ToList()))
        .OrderByDescending(f => f.Sprites.Count)
        .ToList();

    /// <summary>
    /// How well an atlas name fits a wanted character: 100 = names it, 60 = its Identity number
    /// (1nnxx), 0 = says nothing either way, negative = another character (never use it).
    /// </summary>
    public static int Match(string? atlas, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(atlas))
        {
            return 0;
        }

        string flat = Flatten(atlas);
        int index = Array.FindIndex(AutoLook.SinnerNames, n => n.Equals(target, StringComparison.OrdinalIgnoreCase));
        string word = index >= 0 ? AutoLook.SinnerWords[index] : Flatten(target);
        if (word.Length >= 3 && flat.Contains(word, StringComparison.Ordinal))
        {
            return 100;
        }

        if (AutoLook.SinnerWords.Any(w => w != word && flat.Contains(w, StringComparison.Ordinal)))
        {
            return -1000;
        }

        Match number = LeadingNumber().Match(atlas);
        if (number.Success)
        {
            string id = number.Groups[1].Value;
            if (index >= 0 && id.Length == 5 && id.StartsWith($"1{index + 1:00}", StringComparison.Ordinal))
            {
                return 60;
            }

            return -1000; // another Sinner's Identity, or an enemy's numbered atlas
        }

        return 0;
    }

    /// <summary>How good an automatic look is: more poses found by name is better.</summary>
    public static int Rate(AutoLookResult result)
    {
        if (result.Look.IsEmpty)
        {
            return int.MinValue;
        }

        LookData look = result.Look;
        int rating = 0;
        foreach (ClipId clip in Enum.GetValues<ClipId>())
        {
            if (look.Clips.TryGetValue(LookData.ClipKey(clip), out List<LookFrame>? frames) && frames.Count > 0)
            {
                rating += LookData.IsEffect(clip) ? 3 : 10;
                rating += Math.Min(frames.Count, 6);
            }
        }

        if (!Has(look, ClipId.Idle) || !Has(look, ClipId.Punch))
        {
            rating -= 40;
        }

        if (result.UsedShapes)
        {
            rating -= 25;
        }

        return rating;
    }

    /// <summary>
    /// Builds a look from each family of <paramref name="all"/> and returns the best one that is not
    /// another character's. <paramref name="preferAtlas"/> (the sprite the player picked) wins when it
    /// makes a look at all. Effects may come from any family in the file, since they are not tied to a body.
    /// </summary>
    public static Pick? BuildBest(
        IReadOnlyList<PoseCandidate> all,
        string? target,
        Func<PoseCandidate, int, LookFrame> makeFrame,
        string? preferAtlas = null,
        bool usePreferred = false)
    {
        Pick? best = null;
        foreach (Family family in Split(all))
        {
            int match = Match(family.Atlas, target);
            bool preferred = usePreferred && family.Atlas == preferAtlas;
            if (match < 0 && !preferred)
            {
                continue;
            }

            var poses = family.Sprites.Where(c => !IsLayerPiece(c.Name) && !IsNumberedPicture(c.Name)).ToList();
            if (poses.Count == 0)
            {
                continue;
            }

            AutoLookResult result = AutoLook.Build(poses, makeFrame);
            if (!result.Look.PoseFrames.Any())
            {
                continue; // only effects in this atlas: not a character
            }

            int rating = Rate(result) + match + (preferred ? 10_000 : 0);
            if (best is null || rating > best.Rating)
            {
                best = new Pick(result, family.Atlas, rating, match);
            }
        }

        if (best is not null)
        {
            AddMissingEffects(best.Result.Look, all, makeFrame);
        }

        return best;
    }

    /// <summary>Fills effect clips the chosen family had none for, from effect pictures anywhere in the file.</summary>
    private static void AddMissingEffects(LookData look, IReadOnlyList<PoseCandidate> all, Func<PoseCandidate, int, LookFrame> makeFrame)
    {
        ClipId[] effects = { ClipId.SwingFx, ClipId.HitFx, ClipId.Skill2Fx, ClipId.Skill3Fx, ClipId.EgoFx };
        if (effects.All(c => Has(look, c)))
        {
            return;
        }

        var pool = all.Where(c => !IsLayerPiece(c.Name) && !IsNumberedPicture(c.Name)).ToList();
        LookData extra = AutoLook.Build(pool, makeFrame).Look;
        foreach (ClipId clip in effects)
        {
            if (!Has(look, clip) && Has(extra, clip))
            {
                look.Clips[LookData.ClipKey(clip)] = extra.Frames(clip).Select(f => f.Clone()).ToList();
            }
        }
    }

    private static bool Has(LookData look, ClipId clip) =>
        look.Clips.TryGetValue(LookData.ClipKey(clip), out List<LookFrame>? frames) && frames.Count > 0;

    private static string Flatten(string s) => new(s.ToLowerInvariant().Where(char.IsLetter).ToArray());
}
