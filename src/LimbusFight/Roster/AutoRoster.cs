using System.Collections.Concurrent;
using System.Text.Json;
using LimbusFight.Animation;
using LimbusFight.Assets;
using LimbusFight.Core;

namespace LimbusFight.Roster;

/// <summary>
/// Gives the built-in Sinners their looks by themselves: in the background it finds the player's
/// Limbus files whose sprite names mention a Sinner (by name or Identity number), builds an auto
/// look from each candidate file and keeps the best one. Only picture names are stored (in the
/// fighter file), never pictures. Read-only, and stops at once if Limbus Company is started.
/// </summary>
public sealed class AutoRoster : IDisposable
{
    /// <summary>Most files per Sinner opened to try an auto look.</summary>
    private const int MaxTriesPerSinner = 8;

    private readonly RosterManager _roster;
    private readonly ConcurrentQueue<(string Id, LookData Look)> _found = new();
    private readonly CancellationTokenSource _cancel = new();
    private Task? _task;

    public AutoRoster(RosterManager roster) => _roster = roster;

    /// <summary>One line for the menu / select screen; null when there is nothing to say.</summary>
    public string? Status { get; private set; }

    public bool Busy => _task is { IsCompleted: false };

    /// <summary>How many Sinner looks were found this run.</summary>
    public int FoundCount { get; private set; }

    private static string StatePath => Path.Combine(FighterStore.Folder, "autoroster.json");

    /// <summary>Starts looking (once). Fighters that already have a look are never touched.</summary>
    public void Start(SteamCheckResult steam, bool force = false)
    {
        if (Busy)
        {
            return;
        }

        List<(string Id, string Sinner)> wanted = _roster.ListForSelect()
            .Where(t => (t.Data.Sinner ?? t.Data.LookSearch) is not null && t.Data.Look.IsEmpty)
            .Select(t => (t.Id, (t.Data.Sinner ?? t.Data.LookSearch)!))
            .ToList();
        if (wanted.Count == 0)
        {
            Status = null;
            return;
        }

        IReadOnlyList<AssetRoot> roots = LimbusAssetRoots.Find(steam);
        CancellationToken cancel = _cancel.Token;
        _task = Task.Run(() => Run(roots, wanted, force, cancel), cancel);
    }

    /// <summary>Main thread: stores looks the background search found.</summary>
    public void Update()
    {
        while (_found.TryDequeue(out var item))
        {
            _roster.SetLookById(item.Id, item.Look);
            FoundCount++;
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        try
        {
            _task?.Wait(2000);
        }
        catch (AggregateException)
        {
            // cancelled
        }

        _cancel.Dispose();
    }

    private void Run(IReadOnlyList<AssetRoot> roots, List<(string Id, string Sinner)> wanted, bool force, CancellationToken cancel)
    {
        try
        {
            if (!roots.Any(r => r.Exists))
            {
                Status = "Sinner looks: Limbus Company files not found (fighters use plain boxes).";
                return;
            }

            if (LimbusAssetRoots.IsGameRunning())
            {
                Status = "Sinner looks: close Limbus Company, then restart Limbus Fight to find them.";
                return;
            }

            Status = "Sinner looks: scanning your Limbus files...";
            AssetCache cache = AssetCache.Load();
            AssetScanResult scan = AssetScanner.Scan(roots, cache, cancel: cancel);
            if (scan.GameWasRunning || cancel.IsCancellationRequested)
            {
                Status = "Sinner looks: stopped (Limbus Company is running).";
                return;
            }

            List<ScannedFile> files = scan.Browsable.ToList();
            string signature = $"{files.Count}-{files.Sum(f => f.Size)}";
            AutoRosterState state = LoadState();
            if (!force && state.Signature == signature)
            {
                wanted = wanted.Where(w => !state.Tried.Contains(w.Sinner)).ToList();
            }
            else
            {
                state = new AutoRosterState { Signature = signature };
            }

            if (wanted.Count == 0)
            {
                Status = null;
                return;
            }

            using var reader = new UnityAssetReader();
            var progress = new Progress<(int Done, int Total)>(p =>
                Status = p.Total > 0 ? $"Sinner looks: reading Limbus file names {p.Done} / {p.Total}..." : Status);
            AssetIndexer.IndexMissing(files, cache, reader, progress, cancel);
            try
            {
                cache.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the cache is an optimisation only
            }

            int done = 0;
            var bySinner = new Dictionary<string, LookData?>(StringComparer.OrdinalIgnoreCase);
            foreach ((string id, string sinner) in wanted)
            {
                if (cancel.IsCancellationRequested || LimbusAssetRoots.IsGameRunning())
                {
                    Status = "Sinner looks: stopped (Limbus Company is running).";
                    return;
                }

                done++;
                Status = $"Sinner looks: looking for {sinner} ({done} / {wanted.Count})...";
                // Several Identities of one Sinner share the search (each gets its own copy of the look).
                if (!bySinner.TryGetValue(sinner, out LookData? look))
                {
                    look = FindLook(sinner, files, cache, reader, cancel);
                    bySinner[sinner] = look;
                }

                if (look is not null)
                {
                    _found.Enqueue((id, LookStore.Deserialize(LookStore.Serialize(look))));
                }

                state.Tried.Add(sinner);
                SaveState(state);
            }

            Status = FoundCount + _found.Count > 0
                ? $"Sinner looks: found {FoundCount + _found.Count}. Check or fix them in F4."
                : "Sinner looks: none found by name. F3 + Ctrl+A makes one by hand.";
        }
        catch (OperationCanceledException)
        {
            Status = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Status = "Sinner looks: " + ex.Message;
        }
    }

    /// <summary>Best auto look for a Sinner from the files whose sprite names mention them.</summary>
    private static LookData? FindLook(string sinner, List<ScannedFile> files, AssetCache cache, UnityAssetReader reader, CancellationToken cancel)
    {
        var ranked = files
            .Select(f => (File: f, Score: ScoreNames(sinner, cache.GetNames(f.FullPath) ?? Array.Empty<AssetName>())))
            .Where(t => t.Score >= 3)
            .OrderByDescending(t => t.Score)
            .Take(MaxTriesPerSinner)
            .ToList();

        LookData? best = null;
        int bestRating = int.MinValue;
        foreach ((ScannedFile file, _) in ranked)
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }

            AssetFileContents contents;
            try
            {
                contents = reader.ReadContents(file);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            var candidates = contents.Sprites
                .Select(sp => new PoseCandidate(sp.Name, sp.Width, sp.Height, sp.PivotX, sp.PivotY))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            AutoLookResult result = AutoLook.Build(candidates,
                (pose, hold) => LookLoader.MakeFrame(file, LookFrame.KindSprite, pose.Name, null, hold));
            int rating = RateLook(result, sinner);
            if (rating > bestRating)
            {
                bestRating = rating;
                best = result.Look;
            }
        }

        reader.Close();
        if (best is null || bestRating < 20)
        {
            return null;
        }

        best.Scale = 0f;
        return best;
    }

    /// <summary>How strongly a file's sprite names point at a Sinner (name or Identity number) or another fighter (its search word).</summary>
    public static int ScoreNames(string sinner, IEnumerable<AssetName> names)
    {
        int index = Array.FindIndex(AutoLook.SinnerNames, n => n.Equals(sinner, StringComparison.OrdinalIgnoreCase));
        string word = index >= 0 ? AutoLook.SinnerWords[index] : new string(sinner.ToLowerInvariant().Where(char.IsLetter).ToArray());
        if (word.Length < 3)
        {
            return 0;
        }

        // Identity ids: 101xx = Yi Sang, 102xx = Faust ... 112xx = Gregor. Other fighters have none.
        string number = index >= 0 ? $"1{index + 1:00}" : "\0\0\0";
        int score = 0;
        foreach (AssetName name in names)
        {
            if (name.Kind != NameKind.Sprite)
            {
                continue;
            }

            string lower = name.Name.ToLowerInvariant();
            string flat = new string(lower.Where(char.IsLetter).ToArray());
            if (flat.Contains(word, StringComparison.Ordinal))
            {
                score += 2;
            }
            else if (StartsWithIdentity(lower, number))
            {
                score += 1;
            }
        }

        return score;
    }

    /// <summary>"10101_idle" / "sd_10101" style: a 5-digit Identity id starting with the Sinner's number.</summary>
    private static bool StartsWithIdentity(string lower, string number)
    {
        for (int i = 0; i + 5 <= lower.Length; i++)
        {
            bool digitsHere = i == 0 || !char.IsDigit(lower[i - 1]);
            if (digitsHere && lower.AsSpan(i, 3).SequenceEqual(number) && char.IsDigit(lower[i + 3]) && char.IsDigit(lower[i + 4])
                && (i + 5 == lower.Length || !char.IsDigit(lower[i + 5])))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How good an auto look is: more poses found by name is better; another Sinner's file is rejected.</summary>
    public static int RateLook(AutoLookResult result, string sinner)
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

        if (!HasClip(look, ClipId.Idle) || !HasClip(look, ClipId.Punch))
        {
            rating -= 40;
        }

        if (result.UsedShapes)
        {
            rating -= 25;
        }

        bool isSinner = AutoLook.SinnerNames.Any(n => n.Equals(sinner, StringComparison.OrdinalIgnoreCase));
        if (isSinner && result.Character is string character)
        {
            rating += character.Equals(sinner, StringComparison.OrdinalIgnoreCase) ? 15 : -100;
        }

        return rating;
    }

    private static bool HasClip(LookData look, ClipId clip) =>
        look.Clips.TryGetValue(LookData.ClipKey(clip), out List<LookFrame>? frames) && frames.Count > 0;

    private static AutoRosterState LoadState()
    {
        try
        {
            return File.Exists(StatePath)
                ? JsonSerializer.Deserialize<AutoRosterState>(File.ReadAllText(StatePath)) ?? new AutoRosterState()
                : new AutoRosterState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AutoRosterState();
        }
    }

    private static void SaveState(AutoRosterState state)
    {
        try
        {
            Directory.CreateDirectory(FighterStore.Folder);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // only means the search runs again next time
        }
    }

    /// <summary>Which Sinners were already searched for, for this set of Limbus files.</summary>
    private sealed class AutoRosterState
    {
        public string Signature { get; set; } = string.Empty;

        public HashSet<string> Tried { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
