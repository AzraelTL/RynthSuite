using System;

namespace RynthCore.Plugin.RynthAi;

/// <summary>
/// Shared serializer for weapon/wand equip actions across BuffManager and
/// CombatManager (and any other subsystem that swaps the wielded item).
///
/// AC allows only ONE item action in flight at a time. Two UseObject equips
/// fired within ~1s — the buff↔combat wand/sword flap, or CombatManager's own
/// re-equip + EquipWeaponAndSetStance in the same tick — collide and trigger
/// the "you can only move or use one item at a time" notice, and in the worst
/// case an object-teardown access violation (DBOCache::DestroyObj). Each
/// subsystem had its OWN equip throttle (_lastEquipTime, _pendingWieldId) but
/// they didn't know about each other, so cross-subsystem swaps still raced.
///
/// This gate is a single shared instance both managers consult: a swap is only
/// allowed once the last one is done on the server (or MinIntervalMs has passed).
/// The loser yields and retries on a later tick. Spell casts and non-weapon item actions
/// (loot moves, corpse opens) do NOT go through here.
/// </summary>
public sealed class WeaponSwapGate
{
    private readonly object _lock = new();
    private long _lastSwapTickMs = long.MinValue / 4;
    private string _lastSwapBy = "";
    private int _movingId;
    private bool _movingWasWielded;

    /// <summary>
    /// The longest one swap holds the next: a swap the server's answer can't be seen for (or
    /// with no <see cref="ServerIdle"/>) waits this long, as before 2026-10-06.
    /// </summary>
    public int MinIntervalMs { get; set; } = 3000;

    /// <summary>
    /// Swap speed (2026-10-06): the next swap may go once the last one is done on the server,
    /// after at least this long. A weapon change takes up to three steps (a shield or arrows
    /// into the pack, the weapon, the off hand); at a flat 3 s per step it took 6-9 s.
    /// </summary>
    public int FloorMs { get; set; } = 400;

    /// <summary>True when no use or cast is waiting for the server's answer (ActionPacer).</summary>
    public Func<bool>? ServerIdle { get; set; }

    /// <summary>Reads whether the character wields an item (for a noted move, <see cref="NoteMove"/>).</summary>
    public Func<int, bool>? IsWielded { get; set; }

    public Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    /// <summary>
    /// Atomically claim the swap slot. Returns true (recording the swap) when the last swap is
    /// done on the server (see <see cref="FloorMs"/>) or MinIntervalMs has passed; false when
    /// the caller must wait. Only mutates state when it returns true, so a denied caller never
    /// "wastes" the slot.
    /// </summary>
    public bool TryBeginSwap(string requestedBy)
    {
        long now = NowMs();
        lock (_lock)
        {
            long since = now - _lastSwapTickMs;
            if (since < MinIntervalMs && !LastSwapDone(since))
                return false;
            _lastSwapTickMs = now;
            _lastSwapBy = requestedBy ?? "";
            _movingId = 0;
            return true;
        }
    }

    /// <summary>
    /// The claimed swap moved <paramref name="itemId"/> into or out of a hand without a use (a
    /// pack move or a slot wield, which the server answers with no UseDone): the swap is done
    /// once the item's wielded state is no longer <paramref name="wieldedNow"/>.
    /// </summary>
    public void NoteMove(int itemId, bool wieldedNow)
    {
        lock (_lock)
        {
            _movingId = itemId;
            _movingWasWielded = wieldedNow;
        }
    }

    private bool LastSwapDone(long since)
    {
        if (since < FloorMs || ServerIdle == null || IsWielded == null) return false;
        try
        {
            if (!ServerIdle()) return false;
            return _movingId == 0 || IsWielded(_movingId) != _movingWasWielded;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Milliseconds since the last successful swap (diagnostics).</summary>
    public long MsSinceLastSwap
    {
        get { lock (_lock) { return NowMs() - _lastSwapTickMs; } }
    }

    /// <summary>Who last claimed a swap (diagnostics).</summary>
    public string LastSwapBy { get { lock (_lock) { return _lastSwapBy; } } }
}
