using LimbusFight.Animation;
using LimbusFight.Gameplay;

namespace LimbusFight.Roster;

/// <summary>
/// Which fighter file is in each slot (P1, Dummy), and every change to them: switching, copying,
/// renaming, editing numbers or looks. Each change is saved at once. No Raylib, no UI.
/// </summary>
public sealed class RosterManager
{
    private static readonly HashSet<string> DefaultNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Player 1", "Training Box", "Training Dummy", "Fighter",
    };

    private readonly Dictionary<LookSlot, (string Id, FighterData Data)> _slots = new();
    private RosterSettings _settings;

    public RosterManager()
    {
        FighterStore.EnsureDefaults();
        Problem = FighterStore.PresetProblem;
        _settings = FighterStore.LoadSettings();
        Load(LookSlot.Player1, _settings.Player1);
        Load(LookSlot.Dummy, _settings.Dummy);
    }

    /// <summary>Raised when a slot's fighter or its numbers change (not for look edits).</summary>
    public event Action<LookSlot>? Changed;

    /// <summary>Raised when a slot's fighter got a new look from outside the look editor (the auto roster). Numbers are unchanged.</summary>
    public event Action<LookSlot>? LookChanged;

    /// <summary>Last problem (a damaged or missing file, a failed save), for the status line.</summary>
    public string? Problem { get; private set; }

    public FighterData Get(LookSlot slot) => _slots[slot].Data;

    public string IdOf(LookSlot slot) => _slots[slot].Id;

    public FighterStats Stats(LookSlot slot) => _slots[slot].Data.ToStats();

    public List<FighterEntry> List() => FighterStore.List();

    /// <summary>Every fighter for the select screen: built-in Sinners first (in roster order), then the player's own.</summary>
    public List<(string Id, FighterData Data)> ListForSelect()
    {
        var order = Presets.All.Select((p, i) => (p.Id, i)).ToDictionary(t => t.Id, t => t.i);
        var all = new List<(string Id, FighterData Data)>();
        foreach (FighterEntry e in List())
        {
            FighterData data = _slots.Values.FirstOrDefault(s => s.Id == e.Id).Data ?? FighterStore.Load(e.Id, out _);
            all.Add((e.Id, data));
        }

        return all
            .OrderBy(t => order.TryGetValue(t.Id, out int i) ? i : int.MaxValue)
            .ThenBy(t => t.Id == FighterStore.DefaultDummyId ? 1 : 0)
            .ThenBy(t => t.Data.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Saves a look into a fighter file that may not be in a slot (the auto roster); updates the slot if it is.</summary>
    public void SetLookById(string id, LookData look)
    {
        foreach (LookSlot slot in _slots.Keys.ToList())
        {
            if (IdOf(slot) == id)
            {
                SetLook(slot, look);
                LookChanged?.Invoke(slot);
                LookSlot other = slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
                if (IdOf(other) == id)
                {
                    LookChanged?.Invoke(other);
                }

                return;
            }
        }

        FighterData data = FighterStore.Load(id, out string? problem);
        if (problem is null)
        {
            data.Look = look;
            Problem = FighterStore.Save(id, data);
        }
    }

    /// <summary>True while the slot still has a starter fighter's name (so an auto look may name it).</summary>
    public bool HasDefaultName(LookSlot slot) => DefaultNames.Contains(Get(slot).Name);

    /// <summary>Changes numbers (body, movement, moves), saves, and tells the arena.</summary>
    public void Edit(LookSlot slot, Action<FighterData> edit)
    {
        FighterData data = Get(slot);
        edit(data);
        data.Preset = 0; // edited by the player: game updates no longer replace these numbers
        data.Sanitise();
        Save(slot);
        RaiseChanged(slot);
    }

    /// <summary>Stores a new or edited look in the fighter file.</summary>
    public void SetLook(LookSlot slot, LookData look)
    {
        Get(slot).Look = look;
        Save(slot);
    }

    public void Rename(LookSlot slot, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        Edit(slot, d => d.Name = name.Trim());
    }

    /// <summary>Puts the next (+1) or previous (-1) fighter file into the slot.</summary>
    public void Cycle(LookSlot slot, int direction)
    {
        List<FighterEntry> all = List();
        if (all.Count == 0)
        {
            return;
        }

        int index = all.FindIndex(e => e.Id == IdOf(slot));
        int next = ((index < 0 ? 0 : index + direction) % all.Count + all.Count) % all.Count;
        Use(slot, all[next].Id);
    }

    /// <summary>Copies the slot's fighter to a new file ("Name copy") and puts the copy in the slot. Returns its id.</summary>
    public string CreateCopy(LookSlot slot)
    {
        FighterData copy = FighterStore.Deserialize(FighterStore.Serialize(Get(slot)));
        copy.Name = (copy.Name + " copy").Length <= 40 ? copy.Name + " copy" : copy.Name;
        string id = FighterStore.NewId(copy.Name);
        Problem = FighterStore.Save(id, copy);
        Use(slot, id);
        return id;
    }

    /// <summary>Puts a fighter file into a slot. If the other slot already has it, both share one copy.</summary>
    public void Use(LookSlot slot, string id)
    {
        Load(slot, id);
        if (slot == LookSlot.Player1)
        {
            _settings.Player1 = id;
        }
        else
        {
            _settings.Dummy = id;
        }

        FighterStore.SaveSettings(_settings);
        RaiseChanged(slot);
    }

    private void Load(LookSlot slot, string id)
    {
        LookSlot other = slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
        if (_slots.TryGetValue(other, out var shared) && shared.Id == id)
        {
            _slots[slot] = shared;
            return;
        }

        FighterData data = FighterStore.Load(id, out string? problem);
        if (problem is not null)
        {
            Problem = problem;
            FighterStore.Save(id, data);
        }

        _slots[slot] = (id, data);
    }

    private void Save(LookSlot slot) => Problem = FighterStore.Save(IdOf(slot), Get(slot));

    private void RaiseChanged(LookSlot slot)
    {
        Changed?.Invoke(slot);
        LookSlot other = slot == LookSlot.Player1 ? LookSlot.Dummy : LookSlot.Player1;
        if (_slots.TryGetValue(other, out var o) && o.Id == IdOf(slot))
        {
            Changed?.Invoke(other);
        }
    }
}
