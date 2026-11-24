using System.Text.RegularExpressions;

namespace LimbusFight.Animation;

/// <summary>A sprite that could become a pose: its name and shape (no pixels needed).</summary>
/// <param name="PivotY">Unity pivot, 0 = bottom edge, 1 = top edge.</param>
public sealed record PoseCandidate(string Name, int Width, int Height, float PivotX, float PivotY);

/// <summary>One auto pick: which candidate went into which clip, and why.</summary>
public sealed record AutoPick(ClipId Clip, string Name, string Reason);

/// <summary>Result of <see cref="AutoLook.Build"/>: the new look plus an explanation.</summary>
public sealed record AutoLookResult(LookData Look, IReadOnlyList<AutoPick> Picks, string Summary, bool UsedShapes, string? Character);

/// <summary>
/// Works out which sprites of a file are idle, walk, jump, punch and hit poses, so a fighter's look
/// can be built in one key press. Pure logic on names and sizes, so it can be tested and tuned.
/// <para>
/// 1. Names: every name is split into words ("YiSang_Attack_02" → yi, sang, attack, 2) and matched
///    against keyword lists (attack, skill, s1, move, walk, damage, idle, default, ...). Effects
///    (fx, slash, glow, ...) and UI pictures (portrait, icon, ...) are left out. Numbered names
///    ("attack_1", "attack_2") become the frames of one clip, in number order.
/// 2. Shapes, only when names say nothing: character poses usually share a pivot at the feet;
///    the most upright pose becomes idle and the widest poses become the punch.
/// </para>
/// </summary>
public static class AutoLook
{
    public const int MaxFramesPerClip = 8;

    /// <summary>What a group of sprites looks like it is for.</summary>
    public enum Role
    {
        Unknown,
        Idle,
        Walk,
        Jump,
        Attack,
        Hit,
        Evade,
        Guard,
        Down,
        Excluded,
    }

    private sealed record Rule(Regex Pattern, Role Role, int Weight, string Word);

    /// <summary>Keyword rules, matched against single lower-case words of a name. First match per word counts.</summary>
    private static readonly Rule[] Rules = BuildRules();

    private static readonly string[] SinnerWords =
    {
        "yisang", "faust", "donquixote", "ryoshu", "meursault", "honglu", "heathcliff", "ishmael", "rodion", "sinclair", "outis", "gregor",
    };

    private static readonly string[] SinnerNames =
    {
        "Yi Sang", "Faust", "Don Quixote", "Ryoshu", "Meursault", "Hong Lu", "Heathcliff", "Ishmael", "Rodion", "Sinclair", "Outis", "Gregor",
    };

    private static Rule[] BuildRules()
    {
        var rules = new List<Rule>();
        void Add(Role role, int weight, params string[] patterns)
        {
            foreach (string p in patterns)
            {
                rules.Add(new Rule(new Regex("^(?:" + p + ")$", RegexOptions.CultureInvariant), role, weight, p));
            }
        }

        // Not character poses at all.
        Add(Role.Excluded, 10,
            "fx", "vfx", "eff", "effect", "effects", "particle", "particles", "glow", "trail", "aura", "shadow", "smoke", "dust",
            "spark", "sparks", "flash", "light", "lightning", "beam", "bullet", "projectile", "blood", "ring", "circle", "magiccircle",
            "wave", "slashfx", "line", "lines", "icon", "portrait", "face", "cutin", "cut", "illust", "illustration", "bg", "background",
            "ui", "frame", "banner", "logo", "title", "story", "storylog", "thumb", "thumbnail", "mask", "noise", "gradient", "sheet");

        Add(Role.Idle, 6, "idle", "idling", "stand", "standing", "wait", "waiting", "neutral", "stance", "ready");
        Add(Role.Idle, 4, "default", "normal", "base", "basic", "rest", "breath", "breathe");

        Add(Role.Walk, 6, "walk", "walking", "move", "moving", "run", "running", "advance", "forward", "approach");
        Add(Role.Walk, 4, "dash", "step", "moveforward", "walkforward");

        Add(Role.Jump, 6, "jump", "jumping", "leap", "air", "aerial", "midair", "fall", "falling", "rise", "rising", "land", "landing");

        Add(Role.Attack, 6, "attack", "atk", "att", "punch", "kick", "strike", "swing", "slash", "stab", "thrust", "pierce", "blunt",
            "combo", "shoot", "shot", "fire", "throw", "smash", "cleave", "lunge");
        Add(Role.Attack, 5, "skill", "sk", "s", "ego", "special", "ult", "ultimate", "coin", "clash", "finish", "finisher");

        Add(Role.Hit, 6, "damage", "damaged", "dmg", "hurt", "hit", "hitted", "struck", "flinch", "stagger", "staggered", "groggy", "knockback", "knock");
        Add(Role.Down, 4, "dead", "die", "death", "down", "ko", "defeat", "defeated", "panic", "faint", "collapse");

        Add(Role.Evade, 5, "evade", "evasion", "dodge", "avoid", "backstep", "sidestep");
        Add(Role.Guard, 5, "guard", "guarding", "block", "defense", "defence", "def", "parry", "counter", "protect");

        return rules.ToArray();
    }

    /// <summary>
    /// Builds a look from the candidates of one file. <paramref name="makeFrame"/> turns a picked
    /// candidate into a <see cref="LookFrame"/> (the caller knows which file it came from).
    /// </summary>
    public static AutoLookResult Build(IReadOnlyList<PoseCandidate> candidates, Func<PoseCandidate, int, LookFrame> makeFrame)
    {
        // Same name twice (several sheets) counts once.
        List<PoseCandidate> unique = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Width > 0 && c.Height > 0)
            .GroupBy(c => c.Name)
            .Select(g => g.First())
            .ToList();

        List<Group> groups = unique
            .Select(c => (Candidate: c, Split: SplitFrameNumber(c.Name)))
            .GroupBy(x => x.Split.Base, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Group(g.Key, g.OrderBy(x => x.Split.Number).ThenBy(x => x.Candidate.Name, StringComparer.Ordinal).Select(x => x.Candidate).ToList()))
            .ToList();

        foreach (Group group in groups)
        {
            (group.Role, group.Score, group.Word) = Classify(group.Base);
        }

        // Tiny pictures are bits and pieces (icons, sparks), not poses.
        foreach (Group group in groups.Where(g => g.Role != Role.Excluded && g.Frames.All(f => Math.Max(f.Width, f.Height) < 48)))
        {
            group.Role = Role.Excluded;
            group.Word = "tiny";
        }

        var look = new LookData();
        var picks = new List<AutoPick>();

        Group? idle = Best(groups, Role.Idle, "idle", "stand", "wait", "default", "normal");
        Group? walk = Best(groups, Role.Walk, "walk", "move", "run", "dash");
        Group? jump = Best(groups, Role.Jump, "jump", "air", "fall");
        Group? attack = BestAttack(groups);
        Group? hit = Best(groups, Role.Hit, "damage", "damaged", "dmg", "hurt", "hit");

        string jumpNote = "name says '{0}'";
        if (jump is null)
        {
            // Limbus characters do not jump; a dodge or dash pose reads well in the air.
            jump = Best(groups, Role.Evade, "evade", "dodge") ?? walk;
            jumpNote = "no jump pose in this file; using the '{0}' pose";
        }

        string hitNote = "name says '{0}'";
        if (hit is null)
        {
            hit = Best(groups, Role.Down, "down", "dead");
            hitNote = "no damage pose; using the '{0}' pose";
        }

        Fill(look, picks, ClipId.Idle, idle, makeFrame, "name says '{0}'");
        Fill(look, picks, ClipId.Walk, walk, makeFrame, "name says '{0}'");
        Fill(look, picks, ClipId.Jump, jump, makeFrame, jumpNote);
        Fill(look, picks, ClipId.Punch, attack, makeFrame, "name says '{0}'");
        Fill(look, picks, ClipId.Hit, hit, makeFrame, hitNote);

        bool usedShapes = false;
        if (look.Frames(ClipId.Idle).Count == 0 || look.Frames(ClipId.Punch).Count == 0)
        {
            usedShapes = GuessByShape(groups, look, picks, makeFrame);
        }

        string? character = DetectCharacter(unique.Select(c => c.Name));
        string summary = Summarise(look, usedShapes, groups.Count(g => g.Role == Role.Excluded), character);
        return new AutoLookResult(look, picks, summary, usedShapes, character);
    }

    /// <summary>Splits a name into lower-case words: case changes, separators and letter/digit boundaries.</summary>
    public static List<string> Words(string name)
    {
        var words = new List<string>();
        foreach (Match m in Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant))
        {
            words.Add(m.Value.ToLowerInvariant());
        }

        return words;
    }

    /// <summary>"Attack_02" → ("Attack", 2); "Idle" → ("Idle", 0). Only a trailing number is a frame number.</summary>
    public static (string Base, int Number) SplitFrameNumber(string name)
    {
        Match m = Regex.Match(name, @"^(.*?\D)[_\-\s\.]*(\d{1,3})$", RegexOptions.CultureInvariant);
        if (m.Success && m.Groups[1].Value.Trim('_', '-', ' ', '.').Length > 0)
        {
            return (m.Groups[1].Value.TrimEnd('_', '-', ' ', '.'), int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return (name, 0);
    }

    /// <summary>The role a name's words point to, with its score and the deciding word.</summary>
    public static (Role Role, int Score, string Word) Classify(string name)
    {
        var scores = new Dictionary<Role, (int Score, string Word)>();
        List<string> words = Words(name);

        // Joined neighbours catch "Magic_Circle" → magiccircle, "Move_Forward" → moveforward, "S_1" → s.
        var probes = new List<string>(words);
        for (int i = 0; i + 1 < words.Count; i++)
        {
            probes.Add(words[i] + words[i + 1]);
        }

        foreach (string word in probes)
        {
            if (word.All(char.IsDigit))
            {
                continue;
            }

            foreach (Rule rule in Rules)
            {
                if (rule.Pattern.IsMatch(word))
                {
                    int previous = scores.TryGetValue(rule.Role, out var s) ? s.Score : 0;
                    if (rule.Weight > previous)
                    {
                        scores[rule.Role] = (rule.Weight, word);
                    }

                    break;
                }
            }
        }

        if (scores.Count == 0)
        {
            return (Role.Unknown, 0, string.Empty);
        }

        // Effects win over everything; otherwise the strongest keyword wins (ties: the more specific role).
        if (scores.TryGetValue(Role.Excluded, out var excluded))
        {
            return (Role.Excluded, excluded.Score, excluded.Word);
        }

        Role[] order = { Role.Hit, Role.Down, Role.Evade, Role.Guard, Role.Jump, Role.Attack, Role.Walk, Role.Idle };
        KeyValuePair<Role, (int Score, string Word)> best = scores
            .OrderByDescending(kv => kv.Value.Score)
            .ThenBy(kv => Array.IndexOf(order, kv.Key))
            .First();
        return (best.Key, best.Value.Score, best.Value.Word);
    }

    /// <summary>Which Sinner the names mention most, if any.</summary>
    public static string? DetectCharacter(IEnumerable<string> names)
    {
        int[] counts = new int[SinnerWords.Length];
        foreach (string name in names)
        {
            string flat = new string(name.ToLowerInvariant().Where(char.IsLetter).ToArray());
            for (int i = 0; i < SinnerWords.Length; i++)
            {
                if (flat.Contains(SinnerWords[i], StringComparison.Ordinal))
                {
                    counts[i]++;
                }
            }
        }

        int top = Array.IndexOf(counts, counts.Max());
        return counts[top] > 0 ? SinnerNames[top] : null;
    }

    // -----------------------------------------------------------------------------------------

    private sealed class Group(string @base, List<PoseCandidate> frames)
    {
        public string Base { get; } = @base;

        public List<PoseCandidate> Frames { get; } = frames;

        public Role Role { get; set; }

        public int Score { get; set; }

        public string Word { get; set; } = string.Empty;
    }

    /// <summary>Best group of a role: preferred words first, then stronger keywords, then more frames.</summary>
    private static Group? Best(List<Group> groups, Role role, params string[] preferred)
    {
        return groups
            .Where(g => g.Role == role)
            .OrderBy(g => PreferenceRank(g.Word, preferred))
            .ThenByDescending(g => g.Score)
            .ThenByDescending(g => Math.Min(g.Frames.Count, MaxFramesPerClip))
            .ThenBy(g => g.Base.Length)
            .FirstOrDefault();
    }

    /// <summary>The punch: a plain attack before skills, the lowest skill number first, 2-5 frames preferred.</summary>
    private static Group? BestAttack(List<Group> groups)
    {
        return groups
            .Where(g => g.Role == Role.Attack)
            .OrderBy(g => g.Word is "attack" or "atk" or "att" or "punch" or "strike" or "slash" or "swing" ? 0 : 1)
            .ThenBy(g => SkillNumber(g.Base))
            .ThenBy(g => g.Frames.Count is >= 2 and <= 5 ? 0 : 1)
            .ThenByDescending(g => g.Score)
            .ThenBy(g => g.Base, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>"S1_..." / "Skill2" / "Attack3" → 1 / 2 / 3; no number → 0.</summary>
    private static int SkillNumber(string name)
    {
        List<string> words = Words(name);
        for (int i = 0; i + 1 < words.Count; i++)
        {
            if (words[i] is "s" or "skill" or "sk" or "attack" or "atk" && int.TryParse(words[i + 1], out int n))
            {
                return n;
            }
        }

        return 0;
    }

    private static int PreferenceRank(string word, string[] preferred)
    {
        int index = Array.IndexOf(preferred, word);
        return index < 0 ? preferred.Length : index;
    }

    private static void Fill(LookData look, List<AutoPick> picks, ClipId clip, Group? group, Func<PoseCandidate, int, LookFrame> makeFrame, string note)
    {
        if (group is null)
        {
            return;
        }

        List<LookFrame> frames = look.EditFrames(clip);
        string reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, note, group.Word);
        foreach (PoseCandidate pose in group.Frames.Take(MaxFramesPerClip))
        {
            LookFrame frame = makeFrame(pose, LookData.DefaultHold(clip));
            frame.Note = "auto: " + reason;
            frames.Add(frame);
            picks.Add(new AutoPick(clip, pose.Name, reason));
        }
    }

    /// <summary>
    /// Names gave no idle or punch: guess from shapes. Poses share a pivot near the feet (effects do
    /// not); idle = the most upright pose of typical height, punch = the widest poses.
    /// </summary>
    private static bool GuessByShape(List<Group> groups, LookData look, List<AutoPick> picks, Func<PoseCandidate, int, LookFrame> makeFrame)
    {
        HashSet<string> used = look.AllFrames.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        List<PoseCandidate> pool = groups
            .Where(g => g.Role is Role.Unknown or Role.Idle or Role.Attack)
            .SelectMany(g => g.Frames)
            .Where(c => !used.Contains(c.Name))
            .ToList();

        // Prefer pictures whose pivot is at the feet, if the file has a few of them.
        List<PoseCandidate> footed = pool.Where(c => c.PivotY <= 0.2f).ToList();
        if (footed.Count >= 2)
        {
            pool = footed;
        }

        if (pool.Count == 0)
        {
            return false;
        }

        float medianHeight = pool.Select(c => (float)c.Height).OrderBy(h => h).ElementAt(pool.Count / 2);
        List<PoseCandidate> typical = pool.Where(c => c.Height >= medianHeight * 0.75f && c.Height <= medianHeight * 1.35f).ToList();
        if (typical.Count == 0)
        {
            typical = pool;
        }

        bool changed = false;
        if (look.Frames(ClipId.Idle).Count == 0)
        {
            PoseCandidate idle = typical.OrderBy(c => (float)c.Width / c.Height).ThenBy(c => c.Name, StringComparer.Ordinal).First();
            AddGuess(look, picks, ClipId.Idle, idle, makeFrame, "guessed: most upright pose");
            used.Add(idle.Name);
            changed = true;
        }

        if (look.Frames(ClipId.Punch).Count == 0)
        {
            List<PoseCandidate> wide = typical.Where(c => !used.Contains(c.Name))
                .OrderByDescending(c => (float)c.Width / c.Height)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (wide.Count > 0)
            {
                // Widest = the hit; the next widest winds up before it and recovers after it.
                List<PoseCandidate> ordered = wide.Count switch
                {
                    1 => wide,
                    2 => new List<PoseCandidate> { wide[1], wide[0] },
                    _ => new List<PoseCandidate> { wide[1], wide[0], wide[2] },
                };

                foreach (PoseCandidate pose in ordered)
                {
                    AddGuess(look, picks, ClipId.Punch, pose, makeFrame, pose == wide[0] ? "guessed: widest pose (the hit)" : "guessed: wide pose");
                }

                changed = true;
            }
        }

        return changed;
    }

    private static void AddGuess(LookData look, List<AutoPick> picks, ClipId clip, PoseCandidate pose, Func<PoseCandidate, int, LookFrame> makeFrame, string reason)
    {
        LookFrame frame = makeFrame(pose, LookData.DefaultHold(clip));
        frame.Note = reason;
        look.EditFrames(clip).Add(frame);
        picks.Add(new AutoPick(clip, pose.Name, reason));
    }

    private static string Summarise(LookData look, bool usedShapes, int excluded, string? character)
    {
        string Count(ClipId clip)
        {
            int n = look.Frames(clip).Count;
            return $"{LookData.ClipKey(clip)} {(n == 0 ? "-" : n.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
        }

        string clips = string.Join(", ", new[] { ClipId.Idle, ClipId.Walk, ClipId.Jump, ClipId.Punch, ClipId.Hit }.Select(Count));
        string who = character is null ? string.Empty : $"{character}: ";
        string how = usedShapes ? " (some guessed by shape)" : string.Empty;
        string skipped = excluded > 0 ? $", {excluded} effect group{(excluded == 1 ? string.Empty : "s")} skipped" : string.Empty;
        return $"{who}{clips}{how}{skipped}";
    }
}
