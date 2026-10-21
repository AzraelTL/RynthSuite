// IltT11.cs — ACECustom T11 (tier 11+ gear): the player's wield counters for RynthAi's loot rules, and
// "/ilt t11" (also "/ra t11", which RynthAi passes on).
// Moved out of RynthAi 2026-10-20 (RynthAiT11Command.cs and its "/aug" chat feed): the counters and the
// command are ACECustom's, so RynthILT reads them; RynthAi keeps the loot keys and the evaluator
// (Loot/T11ItemSupport.cs, users' .utl profiles use them) and gets the counters through RynthAi.Hub
// SetPlayerCounter.
//
//   * Counters: ACECustom doesn't send them at login ([SendOnLogin] is off), so they come from the
//     "/aug" reply (RynthILT's login probe asks for it on ILT), read by the shared T11AugReportParser.
//     The item-aug override (IltHubState.T11.ItemAugsOverride, "/ilt t11 augs <n|auto>") replaces the
//     /aug Item count. Each change is reported to RynthAi, and every ResendMs anyway (RynthAi may have
//     reloaded or logged in later); -1 = unknown.
//   * "/ilt t11 [status | augs <n|auto> | refresh | item [0xId]]": the counters, the override, a fresh
//     "/aug", and how the selected item's T11 text reads (tier, grade, modifiers, the loot keys' values).
// Ticks only on an ILT-like world (IltHubController): elsewhere RynthAi's counters stay unknown.
using System;
using System.Globalization;
using System.Linq;
using RynthCore.Loot.T11;

namespace RynthCore.Plugin.RynthILT.Hub;

internal sealed class IltT11 : IIltFeature
{
    private const long ResendMs = 5_000;

    // ACE string properties of an item's appraisal (RynthAi's StringValueKey).
    private const uint StrName = 1, StrUse = 14, StrLongDesc = 16;

    private readonly IltHubContext _ctx;
    private readonly T11AugReportParser _parser = new();
    private long _sentAt = long.MinValue;
    private bool _dirty = true;
    private int _overrideApplied = int.MinValue;

    public IltT11(IltHubContext ctx) => _ctx = ctx;

    /// <summary>The counters this character's "/aug" reply gave, with the override applied.</summary>
    public T11PlayerAugs Player { get; } = new();

    /// <summary>How many counters the last report to RynthAi carried (for the tests).</summary>
    public int LastReported { get; private set; }

    private IltT11State S => _ctx.State.T11;

    // ── IIltFeature ─────────────────────────────────────────────────────────

    public void Tick(long nowMs)
    {
        int ov = S.ItemAugsOverride;
        if (ov != _overrideApplied)
        {
            _overrideApplied = ov;
            Player.ItemAugOverride = ov >= 0 ? ov : null;
            _dirty = true;
        }
        if (!_dirty && _sentAt != long.MinValue && nowMs - _sentAt < ResendMs) return;
        Report();
        _sentAt = nowMs;
        _dirty = false;
    }

    /// <summary>The "/aug" reply's counter lines. Never eats (the login probe's capture decides that).</summary>
    public bool OnChat(string text)
    {
        if (_parser.Feed(text, IltHubContext.NowMs, Player)) _dirty = true;
        return false;
    }

    public void OnLogout()
    {
        Player.Clear();
        _dirty = true;
        _sentAt = long.MinValue;
    }

    /// <summary>Every counter to RynthAi (-1 for an unknown one). Pump thread.</summary>
    private void Report()
    {
        int n = 0;
        foreach ((T11Counter counter, _) in T11Catalog.CounterNames)
        {
            long? v = Player.Get(counter);
            if (_ctx.Ai.SetPlayerCounter(unchecked((uint)counter), v ?? -1)) n++;
        }
        LastReported = n;
    }

    // ── "/ilt t11 ..." ──────────────────────────────────────────────────────

    /// <summary>"/ilt t11 [status | augs &lt;n|auto&gt; | refresh | item [0xId]]" (args after "t11"). Pump thread.</summary>
    public void HandleCommand(string[] args)
    {
        string sub = args.Length >= 1 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "":
            case "status":
                PrintStatus();
                break;
            case "augs":
                SetOverride(args.Length >= 2 ? args[1] : string.Empty);
                break;
            case "refresh":
                if (!_ctx.Host.HasInvokeChatParser) { _ctx.Chat("[RynthILT] This engine can't send chat commands."); break; }
                _ctx.Host.InvokeChatParser("/aug");
                _ctx.Chat("[RynthILT] T11: sent /aug - the counters update from its reply.");
                break;
            case "item":
                PrintItem(args.Length >= 2 ? args[1] : null);
                break;
            default:
                _ctx.Chat("[RynthILT] Usage: /ilt t11 [augs <n|auto> | refresh | item [0xId]]  (also /ra t11)");
                break;
        }
    }

    /// <summary>The counters the "T11: Can Wield" loot key compares wield gates against.</summary>
    private void PrintStatus()
    {
        string updated = Player.UpdatedUtc is DateTime t ? t.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) : "never";
        _ctx.Chat($"[RynthILT] === T11 loot (last /aug read: {updated}) ===");
        foreach ((T11Counter counter, string name) in T11Catalog.CounterNames)
        {
            long? v = Player.Get(counter);
            string source = counter == T11Counter.ItemAugmentations && S.ItemAugsOverride >= 0 ? " (override)" : string.Empty;
            _ctx.Chat($"[RynthILT]   {name}: {(v.HasValue ? v.Value.ToString("N0", CultureInfo.CurrentCulture) : "unknown")}{source}");
        }
        if (Player.UpdatedUtc == null && S.ItemAugsOverride < 0)
            _ctx.Chat("[RynthILT]   Unknown counters make \"T11: Can Wield\" -1. Use /ilt t11 refresh, or /ilt t11 augs <n>.");
        if (!_ctx.Ai.Available)
            _ctx.Chat("[RynthILT]   (The loot rules are RynthAi's; it isn't loaded, so nothing uses these yet.)");
    }

    /// <summary>"/ilt t11 augs &lt;n|auto&gt;": the item-aug override (-1 = read from /aug), saved with the Hub's state.</summary>
    private void SetOverride(string arg)
    {
        int value;
        if (arg.Equals("auto", StringComparison.OrdinalIgnoreCase) || arg == "-1")
            value = -1;
        else if (!int.TryParse(arg.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            _ctx.Chat("[RynthILT] Usage: /ilt t11 augs <n|auto>  (n = your item augmentation count)");
            return;
        }
        S.ItemAugsOverride = value;
        Tick(IltHubContext.NowMs);   // applied and reported now
        _ctx.Chat(value < 0
            ? "[RynthILT] T11: item augmentations now come from /aug."
            : $"[RynthILT] T11: item augmentations fixed at {value:N0} for loot rules.");
    }

    /// <summary>"/ilt t11 item [0xId]": the parsed T11 text of an item (the selected item by default).</summary>
    private void PrintItem(string? idArg)
    {
        var host = _ctx.Host;
        uint id = 0;
        if (!string.IsNullOrEmpty(idArg))
            uint.TryParse(idArg.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? idArg[2..] : idArg,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
        if (id == 0 && host.HasGetSelectedItemId) id = host.GetSelectedItemId();
        if (id == 0) { _ctx.Chat("[RynthILT] T11: select an item first (or pass 0xId)."); return; }

        int iid = unchecked((int)id);
        var inv = _ctx.Inventory;
        WorldObject? item = inv.Cache[iid];
        string name = item?.Name ?? string.Empty;
        if (item == null) { _ctx.Chat($"[RynthILT] T11: the client doesn't know 0x{id:X8}."); return; }
        if (name.Length == 0) name = inv.Str(item, StrName);
        var server = new T11ServerProps(inv.Int(item, (uint)T11Catalog.PropZcTier, 0),
                                        inv.Int(item, (uint)T11Catalog.PropWeaponAugScaleTier, 0),
                                        inv.Int(item, (uint)T11Catalog.PropWeaponAugScaleQuality, -1));
        T11ItemInfo info = T11ItemInfo.Parse(name, inv.Str(item, StrLongDesc), inv.Str(item, StrUse), server);
        if (!info.IsT11)
        {
            bool appraised = !host.HasHasAppraisalData || host.HasAppraisalData(id);
            _ctx.Chat(appraised
                ? $"[RynthILT] T11: {name} is not a T11 item."
                : $"[RynthILT] T11: {name} isn't appraised yet - ID it and try again.");
            return;
        }

        _ctx.Chat($"[RynthILT] === T11: {name} (0x{id:X8}) ===");
        _ctx.Chat($"[RynthILT]   Tier {(info.TierIsExact ? info.Tier.ToString(CultureInfo.InvariantCulture) : "~" + info.Tier.ToString(CultureInfo.InvariantCulture))}"
            + (info.Grade != null ? $", grade {info.Grade} ({info.DamagePercent}% of max damage)" : string.Empty)
            + (info.WeaponQuality >= 0 ? $", quality {info.WeaponQuality}/1000" : string.Empty)
            + (info.GearGrade != null ? $", gear grade {info.GearGrade} ({info.GearGradeLines} lines)" : string.Empty)
            + (info.ZoneLocked ? ", zone locked" : string.Empty));
        if (info.PropertySlots >= 0 || info.Tainted)
            _ctx.Chat("[RynthILT]   Properties "
                + (info.PropertySlots < 0 ? "?" : info.PropertySlotCap > 0 ? $"{info.PropertySlots} of {info.PropertySlotCap}" : info.PropertySlots.ToString(CultureInfo.InvariantCulture))
                + (info.Tainted ? ", tainted (bags no longer work)" : string.Empty));
        foreach (T11WieldGate g in info.WieldGates)
            _ctx.Chat($"[RynthILT]   Wield requires {g.Amount:N0} {T11Catalog.CounterName(g.Counter)}");
        foreach (T11Modifier m in info.Modifiers)
        {
            string band = m.Min.HasValue && m.Max.HasValue ? $" [{m.Min}-{m.Max}] roll {m.RollPercent}%" : string.Empty;
            string marks = (m.Tinkered != 0 ? $" ({m.Tinkered:+#;-#} tinkered)" : string.Empty)
                + (m.BuiltIn ? " (built-in)" : string.Empty) + (m.Locked ? " (locked)" : string.Empty);
            _ctx.Chat($"[RynthILT]   Mod {m.Name} {(m.IsSlotSpecial ? "(special)" : "+" + m.Value)}{band}{marks}");
        }
        foreach ((string proc, double chance) in info.Procs)
            _ctx.Chat($"[RynthILT]   Cast on strike: {proc} ({chance.ToString("0.#", CultureInfo.CurrentCulture)}%)");

        int canWield = T11Keys.TryGetValue(info, T11Keys.CanWield, Player, out int cw) ? cw : -1;
        _ctx.Chat($"[RynthILT]   Can wield: {(canWield == 1 ? "yes" : canWield == 0 ? "no" : "unknown (no /aug read, or not appraised)")}"
            + $", modifier ratings {info.ModifierRatingTotal}, average roll {info.AverageRollPercent}%");
        string keys = string.Join(", ", T11Keys.Names()
            .Where(k => k.Id < T11Keys.ModifierBase)
            .Select(k => $"{k.Name.Replace("T11: ", string.Empty)}={(T11Keys.TryGetValue(info, k.Id, Player, out int v) ? v : 0)}"));
        _ctx.Chat($"[RynthILT]   Loot keys (RynthAi's loot rules read the same): {keys}");
    }
}
