using System.Collections.Generic;

namespace RynthCore.Plugin.RynthAi.Loot;

/// <summary>
/// Packs the server would not take loot into. The destination comes from the client's own
/// item counts, and a side pack whose contents the client never loaded counts as nearly empty
/// however full it is: every item went to that one pack, the server refused all 3 tries each
/// time, and every item on every corpse was skipped (Drakkon, 2026-10-15, 62 items in a row).
/// A pack that takes none of an item's tries counts as full for <see cref="HoldMs"/>; the
/// destination search skips it, so the next pack is tried. Pure: the caller passes the time.
/// </summary>
internal sealed class PackRefusals
{
    /// <summary>How long a refusing pack is left out (time to free some room, or open it).</summary>
    public const long HoldMs = 5 * 60_000;

    private readonly Dictionary<int, long> _until = new();

    /// <summary>The server took none of the tries into <paramref name="packId"/>.</summary>
    public void Refuse(int packId, long nowMs)
    {
        if (packId != 0) _until[packId] = nowMs + HoldMs;
    }

    /// <summary>True while <paramref name="packId"/> is left out.</summary>
    public bool IsRefused(int packId, long nowMs)
    {
        if (!_until.TryGetValue(packId, out long until)) return false;
        if (nowMs < until) return true;
        _until.Remove(packId);
        return false;
    }

    /// <summary>An item landed in <paramref name="packId"/>: it has room after all.</summary>
    public void Accepted(int packId) => _until.Remove(packId);

    public void Clear() => _until.Clear();
}
