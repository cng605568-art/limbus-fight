using LimbusFight.Gameplay;

namespace LimbusFight.Roster;

/// <summary>
/// Arcade: a row of CPU opponents for one chosen fighter. Other Sinners first (in a shuffled order),
/// Bosses at the end; the last two fights are one CPU level harder. Pure data, no Raylib.
/// </summary>
public sealed class ArcadeLadder
{
    /// <summary>Most Sinners in one run (Bosses come on top).</summary>
    public const int MaxSinners = 5;

    public ArcadeLadder(string playerId, IReadOnlyList<string> opponents, CpuLevel level)
    {
        PlayerId = playerId;
        Opponents = opponents;
        BaseLevel = level;
    }

    public string PlayerId { get; }

    public IReadOnlyList<string> Opponents { get; }

    public CpuLevel BaseLevel { get; }

    /// <summary>Index of the current fight.</summary>
    public int Stage { get; private set; }

    /// <summary>Times the player lost and tried the same fight again.</summary>
    public int Continues { get; private set; }

    public string Current => Opponents[Math.Clamp(Stage, 0, Opponents.Count - 1)];

    public bool IsLast => Stage >= Opponents.Count - 1;

    public bool IsEmpty => Opponents.Count == 0;

    public string StageLabel => IsLast && Opponents.Count > 1 ? $"FINAL STAGE" : $"STAGE {Stage + 1} / {Opponents.Count}";

    /// <summary>The CPU level for the current fight.</summary>
    public CpuLevel Level => Stage >= Opponents.Count - 2 && Opponents.Count > 2 && BaseLevel < CpuLevel.Hard ? BaseLevel + 1 : BaseLevel;

    public void Advance() => Stage++;

    public void Retry() => Continues++;

    /// <summary>
    /// Builds a ladder: built-in Sinners other than the player's (one fighter per Sinner, preferring the
    /// built-in file), shuffled by <paramref name="seed"/>, up to <see cref="MaxSinners"/>, then Bosses.
    /// </summary>
    public static ArcadeLadder Build(string playerId, IReadOnlyList<(string Id, FighterData Data)> all, uint seed, CpuLevel level)
    {
        FighterData? player = all.FirstOrDefault(t => t.Id == playerId).Data;
        string? playerSinner = player?.Sinner;

        var sinners = all
            .Where(t => t.Data.Sinner is not null && !t.Data.IsBoss && t.Id != playerId
                && !string.Equals(t.Data.Sinner, playerSinner, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.Data.Sinner!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.Data.Preset > 0).ThenBy(t => t.Id, StringComparer.Ordinal).First().Id)
            .ToList();

        Shuffle(sinners, seed);
        var opponents = sinners.Take(MaxSinners).ToList();
        opponents.AddRange(all.Where(t => t.Data.IsBoss && t.Id != playerId).Select(t => t.Id));

        if (opponents.Count == 0)
        {
            // No built-in fighters (all deleted): fight anyone else, or yourself.
            opponents.AddRange(all.Where(t => t.Id != playerId).Select(t => t.Id).Take(MaxSinners));
            if (opponents.Count == 0)
            {
                opponents.Add(playerId);
            }
        }

        return new ArcadeLadder(playerId, opponents, level);
    }

    /// <summary>Boss Rush: every Boss (except the player's own fighter), in roster order.</summary>
    public static ArcadeLadder BuildBossRush(string playerId, IReadOnlyList<(string Id, FighterData Data)> all, CpuLevel level) =>
        new(playerId, all.Where(t => t.Data.IsBoss && t.Id != playerId).Select(t => t.Id).ToList(), level) { IsBossRush = true };

    /// <summary>True for a Boss Rush (single rounds, Bosses only).</summary>
    public bool IsBossRush { get; private init; }

    private static void Shuffle(List<string> items, uint seed)
    {
        uint s = seed == 0 ? 0x2545F491u : seed;
        for (int i = items.Count - 1; i > 0; i--)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            int j = (int)(s % (uint)(i + 1));
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
