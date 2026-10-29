using System;
using System.Collections.Generic;

namespace RynthCore.Plugin.RynthAi.Loot;

/// <summary>
/// Keep # (KeepUpTo) arithmetic, split out of <c>RynthAiPlugin.UnderKeepCap</c> so it can be
/// tested offline. The plugin passes in the live pack snapshot and its per-corpse "approved
/// but not in the pack yet" table; nothing here touches the host.
/// </summary>
internal static class LootKeepCap
{
    /// <summary>How much one item counts toward a cap: its stack size, never less than 1.</summary>
    internal static int StackOf(WorldObject item) => Math.Max(1, item.Values(LongValueKey.StackCount, 1));

    /// <summary>
    /// How many of what a Keep # rule matches the player already has: whole stacks of every
    /// named pack item whose individual units the rule matches (a matcher that throws counts as no match), plus items
    /// approved on this corpse that are not in the pack yet. An approved item that is already in
    /// the pack snapshot, or that no longer exists (a looted stack merged into a pack stack and
    /// was deleted), is not counted twice.
    /// </summary>
    internal static int CountHave(
        IEnumerable<WorldObject>? inventory,
        Func<WorldObject, bool> matches,
        IReadOnlyDictionary<int, int> pending,
        Func<int, bool> stillExists)
    {
        int have = 0;
        var liveIds = new HashSet<int>();
        if (inventory != null)
        {
            foreach (var inv in inventory)
            {
                liveIds.Add(inv.Id);
                if (string.IsNullOrEmpty(inv.Name)) continue;
                bool m;
                try { m = matches(UnitForRule(inv)); } catch { m = false; }
                if (m) have += StackOf(inv);
            }
        }
        foreach (var kv in pending)
            if (!liveIds.Contains(kv.Key) && stillExists(kv.Key)) have += kv.Value;
        return have;
    }

    /// <summary>
    /// A read-only comparison view of one unit in a stack. Loot rules use this view;
    /// pickup and inventory counts still use the original object and its full
    /// stack quantity to the cap; no properties on the live item are changed.
    /// </summary>
    internal static WorldObject UnitForRule(WorldObject item)
    {
        int count = StackOf(item);
        if (count == 1) return item;

        var overlay = new ItemPropertyOverlay { LiveFallback = item.Overlay?.LiveFallback ?? true };
        if (item.Overlay is { } original)
        {
            foreach (var pair in original.Ints) overlay.Ints.Add(pair.Key, pair.Value);
            foreach (var pair in original.Strings) overlay.Strings.Add(pair.Key, pair.Value);
            foreach (var pair in original.Doubles) overlay.Doubles.Add(pair.Key, pair.Value);
        }
        overlay.Ints[12] = 1; // the rule sees one unit, not the pack's entire stack
        UnitProperty(19, 15); // Value / StackUnitValue
        UnitProperty(5, 13);  // EncumbranceVal / StackUnitEncumbrance
        return new WorldObject(item.Id, item.Name, item.ObjectClass)
        {
            Cache = item.Cache,
            Overlay = overlay,
            _wieldedLocationDirect = item._wieldedLocationDirect,
            _directContainerId = item._directContainerId,
            _directSlot = item._directSlot,
        };

        void UnitProperty(uint totalKey, uint unitKey)
        {
            int unit = item.Values((int)unitKey, int.MinValue);
            if (unit >= 0) overlay.Ints[totalKey] = unit;
            else
            {
                int total = item.Values((int)totalKey, int.MinValue);
                if (total != int.MinValue) overlay.Ints[totalKey] = total / count;
            }
        }
    }
    /// <summary>True while the player holds fewer than <paramref name="cap"/>; a cap of 0 or less never loots.</summary>
    internal static bool UnderCap(int have, int cap) => have < cap;
}
