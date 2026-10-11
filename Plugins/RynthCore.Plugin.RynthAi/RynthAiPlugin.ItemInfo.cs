using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using RynthCore.Plugin.RynthAi.ItemInfo;
using RynthCore.PluginSdk;

namespace RynthCore.Plugin.RynthAi;

/// <summary>
/// /ra iteminfo [name]: a selected (or named) item's details on one chat line, Mag-Tools
/// style (ItemInfo/ItemInfoText). An item the client hasn't appraised is identified first
/// and printed when the appraisal arrives, or after <see cref="ItemInfoTimeoutMs"/> with
/// what the client has. Partial class of RynthAiPlugin.
///
/// Threads: the command only queues its argument; <see cref="TickItemInfo"/> on the plugin
/// tick does every host read, the ID request and the print. Nothing is drawn.
/// </summary>
public sealed partial class RynthAiPlugin
{
    internal const long ItemInfoTimeoutMs = 5000;

    /// <summary>Test seam: the clock the ID timeout runs on.</summary>
    internal static Func<long> ItemInfoClock = () => Environment.TickCount64;

    private readonly ConcurrentQueue<string> _itemInfoRequests = new();
    // One item waiting for its appraisal at a time (tick thread only); a newer request replaces it.
    private uint _itemInfoPendingId;
    private long _itemInfoDeadline;

    /// <summary>/ra iteminfo [item name]. Queued for the next tick.</summary>
    private void HandleItemInfoCommand(string[] parts)
    {
        string arg = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2).Trim() : string.Empty;
        if (arg.Equals("help", StringComparison.OrdinalIgnoreCase) || arg == "?")
        {
            ChatLine("[RynthAi] /ra iteminfo - the selected item's details (identifies it first when needed).");
            ChatLine("[RynthAi] /ra iteminfo <name> - the first item in your inventory with that name (else one whose name contains it).");
            return;
        }
        if (_itemInfoRequests.Count < 8) _itemInfoRequests.Enqueue(arg);
    }

    /// <summary>Plugin tick: starts queued requests, prints an item once its ID lands or times out.</summary>
    private void TickItemInfo()
    {
        while (_itemInfoRequests.TryDequeue(out string? arg))
            StartItemInfo(arg);

        uint selected = System.Threading.Interlocked.Exchange(ref _itemInfoSelectId, 0);
        if (selected != 0)
            StartItemInfoFor(selected, quiet: true);

        uint pending = _itemInfoPendingId;
        if (pending == 0) return;
        bool appraised = !Host.HasHasAppraisalData || Host.HasAppraisalData(pending);
        if (appraised || ItemInfoClock() >= _itemInfoDeadline)
        {
            _itemInfoPendingId = 0;
            PrintItemInfo(pending, identified: appraised);
        }
    }

    private void StartItemInfo(string arg)
    {
        uint id;
        if (arg.Length == 0)
        {
            id = Host.HasGetSelectedItemId ? Host.GetSelectedItemId() : 0;
            if (id == 0)
            {
                ChatLine("[RynthAi] Item info: nothing selected. Select an item, or /ra iteminfo <name>.");
                return;
            }
        }
        else
        {
            id = FindInventoryItemByName(arg);
            if (id == 0)
            {
                ChatLine($"[RynthAi] Item info: no \"{arg}\" in your inventory.");
                return;
            }
        }
        StartItemInfoFor(id, quiet: false);
    }

    /// <summary>
    /// Prints <paramref name="id"/> now when it's appraised, else asks for its ID and prints when
    /// it lands. <paramref name="quiet"/> (Item Info On Select): no "identifying" chatter.
    /// </summary>
    private void StartItemInfoFor(uint id, bool quiet)
    {
        if (!Host.HasHasAppraisalData || Host.HasAppraisalData(id))
        {
            if (_itemInfoPendingId == id) _itemInfoPendingId = 0;
            PrintItemInfo(id, identified: true);
            return;
        }

        if (!quiet && _itemInfoPendingId != 0 && _itemInfoPendingId != id)
            ChatLine($"{ItemPrefix}Item info: stopped waiting for {ObjectNameOr(_itemInfoPendingId)}.");
        if (Host.HasRequestId) Host.RequestId(id);
        _itemInfoPendingId = id;
        _itemInfoDeadline = ItemInfoClock() + ItemInfoTimeoutMs;
        if (!quiet) ChatLine($"{ItemPrefix}Item info: identifying {ObjectNameOr(id)}...");
    }

    // Item Info On Select: the item the player just selected, for the next tick (0 = none).
    private uint _itemInfoSelectId;

    /// <summary>
    /// Selection change (OnSelectedTargetChange): queues Item Info for an item the player
    /// selected. Skips non-items and RynthAi's own selections (the looter, metas).
    /// </summary>
    /// <summary>Queues item info for the player's pick; true when it will print (Item Info On Select).</summary>
    private bool QueueItemInfoOnSelect(uint id, WorldObject? obj)
    {
        if (id == 0 || _dashboard?.Settings is not { ItemInfoOnSelect: true }) return false;
        if (obj == null || !IsLootableClass(obj.ObjectClass)) return false;
        if (SelfSelect.IsOurs(id)) return false;
        System.Threading.Interlocked.Exchange(ref _itemInfoSelectId, id);
        return true;
    }

    /// <summary>
    /// An inventory item (carried or worn) whose name is <paramref name="name"/>, ignoring case;
    /// else the first whose name contains it. 0 when none.
    /// </summary>
    private uint FindInventoryItemByName(string name)
    {
        if (_objectCache == null) return 0;
        uint partial = 0;
        foreach (WorldObject wo in _objectCache.GetInventory())
        {
            if (wo.Name.Length == 0) continue;
            if (wo.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return unchecked((uint)wo.Id);
            if (partial == 0 && wo.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) partial = unchecked((uint)wo.Id);
        }
        return partial;
    }

    private string ObjectNameOr(uint id) =>
        Host.TryGetObjectName(id, out string n) && n.Length > 0 ? n : $"0x{id:X8}";

    /// <summary>
    /// Item info lines carry their own prefix so RynthChat files them under its Items tab
    /// (ChatClassifier.IsItemInfo, 2026-10-09); it still starts "[Rynth", so other chat
    /// windows treat it as RynthAi output.
    /// </summary>
    internal const string ItemPrefix = "[RynthAi Item] ";

    private void PrintItemInfo(uint id, bool identified)
    {
        var source = new HostItemInfoSource(Host, id);
        ChatLine(ItemPrefix + ItemInfoText.Describe(source, SpellDatabase.GetSpellName, identified) + LootVerdictSuffix(id));
    }

    /// <summary>
    /// " | Loot: [Keep] (A) MajorSkill Set": the looter's own verdict on the item, as a preview
    /// (nothing recorded), so it says exactly what [LootEval] would. Item info on select replaced
    /// the separate verdict line (95df8ed), which was the only place it showed (Tom, 2026-10-10:
    /// lots of Major armor kept and no way to tell which rule). "" when there's no loot profile,
    /// or /ra lootcheck off (the toggle that hid the old verdict line too).
    /// </summary>
    private string LootVerdictSuffix(uint id)
    {
        try
        {
            LegacyUi.LegacyUiSettings? settings = _dashboard?.Settings;
            WorldObject? wo = _objectCache?[unchecked((int)id)];
            if (!_lootInspectMode || settings == null || wo == null || string.IsNullOrEmpty(settings.CurrentLootPath)) return "";
            if (!ClassifyLootItem(wo, settings, preview: true, out string action, out _, out string rule, out string builtIn))
                // A rule can match and still not take it (its Keep # reached, a Read rule on a non-scroll).
                return rule.Length > 0 ? $" | Loot: leave ({rule} matched, not taken)" : " | Loot: no rule matched";
            if (builtIn.Length > 0) return $" | Loot: [{action}] built-in - {builtIn}";
            return $" | Loot: [{action}] {(rule.Length > 0 ? rule : "(unnamed rule)")}";
        }
        catch (Exception ex)
        {
            Host.Log($"[RynthAi] Item info: loot verdict failed for 0x{id:X8}: {ex.GetType().Name}: {ex.Message}");
            return "";
        }
    }

    /// <summary>The live appraisal through the host (tick thread).</summary>
    private sealed class HostItemInfoSource : IItemInfoSource
    {
        private readonly RynthCoreHost _host;
        private readonly uint _id;

        public HostItemInfoSource(RynthCoreHost host, uint id)
        {
            _host = host;
            _id = id;
            Name = host.TryGetObjectName(id, out string n) ? n : string.Empty;
            SpellIds = ReadSpells(host, id);
        }

        public string Name { get; }
        public IReadOnlyList<int> SpellIds { get; }

        public bool TryInt(uint key, out int value)
        {
            value = 0;
            return _host.HasGetObjectIntProperty && _host.TryGetObjectIntProperty(_id, key, out value);
        }

        public bool TryFloat(uint key, out double value)
        {
            value = 0;
            return _host.TryGetObjectDoubleProperty(_id, key, out value);
        }

        private static IReadOnlyList<int> ReadSpells(RynthCoreHost host, uint id)
        {
            if (!host.HasGetObjectSpellIds) return Array.Empty<int>();
            var buf = new uint[64];
            int n = host.GetObjectSpellIds(id, buf, buf.Length);
            if (n <= 0) return Array.Empty<int>();
            var list = new List<int>(n);
            for (int i = 0; i < n && i < buf.Length; i++) list.Add(unchecked((int)buf[i]));
            return list;
        }
    }
}
