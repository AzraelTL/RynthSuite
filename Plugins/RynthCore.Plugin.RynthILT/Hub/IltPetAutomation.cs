// IltPetAutomation.cs — ACECustom's parts of RynthAi's Pets tab: the pet charms, the "summon from an
// empty essence" refill path and siphon-lens auto-capture.
// Moved out of RynthAi's MiniRemote/PetsTab.cs 2026-10-20 (originally SilentKelpie's IltPets, ILT
// Hub): summoning is every server's, so the roster, healing and cosmetic pets stay RynthAi's, but
// these three are ACECustom content and live here now.
//
//   * Pet charms (section "pets-charms"): Summon Essence Refill (the server refills an empty essence
//     from banked pyreals) and Universal Summoning Mastery, read from the charm's appraisal text and
//     the player bools the server stamps, plus the "no spell components" warning when the server
//     requires them. The refill opt-in feeds RynthAi's PetManager through RynthILT.Hub
//     AllowSummonOnEmpty (cached here every tick; an unknown state never spends pyreals).
//   * Auto siphon capture (section "pets-capture", ACECustom MonsterCapture): when enabled, uses the
//     chosen lens on the nearest attackable creature within 5 m once it is <=20% health or under
//     20 HP (Asheron's Lens skips the health gate). Resonance still needs a prior failed capture on
//     the server. Bounded per creature and overall (PetCaptureThrottle).
// Ticks only on an ILT-like world (IltHubController); the sections draw nothing elsewhere.
using System;
using ImGuiNET;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthILT.Hub;

internal sealed class IltPetAutomation : IIltFeature
{
    private const long CaptureThinkMs = 250;
    private const long CaptureCooldownMs = 2500;
    private const long CaptureFailBackoffMs = 8000;
    private const long CharmScanMs = 2000;
    private const long GateMs = 500;

    private readonly IltHubContext _ctx;
    private readonly PetCaptureThrottle _captureThrottle = new();
    private long _lastCaptureThink;
    private long _captureCooldownUntil;
    private long _lastCharmScan;
    private long _lastGate;

    // Read by the render thread and (AllowSummonOnEmpty) by RynthAi's pump.
    private volatile string _captureStatus = "off";
    private volatile bool _uiRefillActive;
    private volatile bool _uiMasteryActive;
    private volatile bool _uiHasCasting = true;
    private volatile bool _allowSummonOnEmpty;

    // The server's health updates (RynthPluginOnUpdateHealth), for creatures the client has no vitals for.
    private const int MaxHealthEntries = 512;
    private readonly object _healthGate = new();
    private readonly System.Collections.Generic.Dictionary<int, float> _healthRatios = new();

    public IltPetAutomation(IltHubContext ctx) => _ctx = ctx;

    private IltPetState S => _ctx.State.Pet;

    /// <summary>The auto-capture line under the switch (render thread).</summary>
    public string CaptureStatus => _captureStatus;

    /// <summary>
    /// RynthILT.Hub AllowSummonOnEmpty: the last tick's answer, false off an ILT world. Any thread.
    /// </summary>
    public bool AllowSummonOnEmpty => _ctx.Options.IsIltLikeWorld && _allowSummonOnEmpty;

    // ── Charms / gates ──────────────────────────────────────────────────────

    /// <summary>Summon Essence Refill charm is carried and switched on (or the server stamped the player bool).</summary>
    public bool RefillCharmActive()
    {
        var inv = _ctx.Inventory;
        if (inv.PlayerBool(IltInventory.BoolPyrealRefillActive)) return true;
        var charm = inv.FindByWcid(IltInventory.WcidSummonRefillCharm);
        return charm != null && inv.CharmStatus(charm) == IltTri.On;
    }

    public bool MasteryCharmActive()
    {
        if (_ctx.Options.IsOff(IltFeature.UniversalMastery)) return false;
        var inv = _ctx.Inventory;
        if (inv.PlayerBool(IltInventory.BoolUniversalMasteryActive)) return true;
        var charm = inv.FindByWcid(IltInventory.WcidUniversalMasteryCharm);
        return charm != null && inv.CharmStatus(charm) == IltTri.On;
    }

    /// <summary>
    /// Summon from an empty essence (spends banked pyreals) only when the player opted in, the refill
    /// charm is active, and the server has positively confirmed the feature: either the feature check
    /// reported pet_refill on, or the server stamped the player's refill bool. Pump thread.
    /// </summary>
    internal bool ComputeAllowSummonOnEmpty()
    {
        if (!S.RefillFromEmpty || !_ctx.Options.IsIltLikeWorld || !RefillCharmActive()) return false;
        bool confirmed = _ctx.Options.IsOn(IltFeature.PetRefill)
                         || _ctx.Inventory.PlayerBool(IltInventory.BoolPyrealRefillActive);
        if (!confirmed) RynthLog.Trace(LogCat.IltPets, "summon-on-empty refused: pet_refill not confirmed by the server");
        return confirmed;
    }

    // ── IIltFeature ─────────────────────────────────────────────────────────

    public void Tick(long nowMs)
    {
        if (nowMs - _lastGate >= GateMs)
        {
            _lastGate = nowMs;
            _allowSummonOnEmpty = ComputeAllowSummonOnEmpty();
        }
        if (nowMs - _lastCharmScan >= CharmScanMs)
        {
            _lastCharmScan = nowMs;
            // Host reads (and appraisal requests) happen here, never on the render thread.
            _uiRefillActive = RefillCharmActive();
            _uiMasteryActive = MasteryCharmActive();
            _uiHasCasting = _ctx.Inventory.HasCastingSupplies();
        }
        if (nowMs - _lastCaptureThink >= CaptureThinkMs)
        {
            _lastCaptureThink = nowMs;
            TickCapture(nowMs);
        }
    }

    /// <summary>ACECustom MonsterCapture replies: back off after a refuse so lenses aren't burned. Never eats.</summary>
    public bool OnChat(string text)
    {
        if (!S.AutoCapture || string.IsNullOrEmpty(text)) return false;
        long now = IltHubContext.NowMs;
        if (PetCaptureLines.IsSuccess(text))
        {
            _captureStatus = "captured";
            _captureThrottle.RecordSuccess();
            _captureCooldownUntil = now + CaptureCooldownMs;
        }
        else if (ContainsAny(text,
                "too strong",
                "blind rage",
                "cannot be siphoned",
                "cannot be captured",
                "No creatures nearby",
                "already dead",
                "already being captured",
                "hasn't failed a capture",
                "echo has faded",
                "Failed to consume"))
        {
            _captureStatus = "refused: " + Truncate(text, 48);
            _captureCooldownUntil = now + CaptureFailBackoffMs;
        }
        else if (ContainsAny(text, "siphon failed", "lens shatters", "resisted your capture", "resonance fades"))
        {
            _captureStatus = "failed (creature may enrage)";
            _captureCooldownUntil = now + CaptureFailBackoffMs;
        }
        return false;
    }

    /// <summary>A creature's health ratio from the server (RynthPluginOnUpdateHealth; any thread).</summary>
    public void NoteHealth(uint objectId, float ratio)
    {
        if (objectId == 0 || float.IsNaN(ratio)) return;
        lock (_healthGate)
        {
            if (_healthRatios.Count >= MaxHealthEntries) _healthRatios.Clear();
            _healthRatios[unchecked((int)objectId)] = Math.Clamp(ratio, 0f, 1f);
        }
    }

    /// <summary>The last health ratio the server sent for a creature, -1 when none.</summary>
    private float HealthRatio(int id)
    {
        lock (_healthGate) return _healthRatios.TryGetValue(id, out float v) ? v : -1f;
    }

    public void OnLogout()
    {
        lock (_healthGate) _healthRatios.Clear();
        _captureCooldownUntil = 0;
        _captureStatus = "off";
        _captureThrottle.Reset();
        _allowSummonOnEmpty = false;
    }

    private static bool ContainsAny(string text, params string[] parts)
    {
        foreach (string p in parts)
            if (text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    // ── Auto siphon capture (ACECustom MonsterCapture) ───────────────────────

    /// <summary>
    /// When Auto Capture is on, use the chosen siphon lens on the nearest attackable creature within
    /// 5 m once ACECustom's health gate is met (≤20% or under 20 HP; Asheron's skips it). The server
    /// picks the nearest creature itself, so this fires only when that nearest candidate is already
    /// weak enough: a full-health neighbour closer than the wounded target won't waste a lens.
    /// </summary>
    internal void TickCapture(long now)
    {
        if (!S.AutoCapture)
        {
            _captureStatus = "off";
            return;
        }
        if (_ctx.Options.IsOff(RynthIltHub.FeatureMonsterCapture))
        {
            _captureStatus = "monster capture disabled on this server";
            return;
        }
        if (now < _captureCooldownUntil)
        {
            long left = (_captureCooldownUntil - now + 999) / 1000;
            if (!_captureStatus.StartsWith("waiting", StringComparison.Ordinal))
                _captureStatus = $"waiting {left}s";
            return;
        }
        var inv = _ctx.Inventory;
        if (inv.IsBusy) { _captureStatus = "busy"; return; }

        var lens = PetCaptureRules.LensAt(S.CaptureLens);
        var lensItem = inv.FindByWcid(lens.Wcid);
        if (lensItem == null)
        {
            _captureStatus = $"no {lens.Name} carried";
            return;
        }

        if (!TryFindNearestCaptureTarget(out WorldObject? target, out double distM, out uint curHp, out uint maxHp))
        {
            _captureStatus = "no attackable creature within 5 m";
            return;
        }

        if (!PetCaptureRules.InRange(distM))
        {
            _captureStatus = $"{target!.Name} at {distM:0.0} m (need <={PetCaptureRules.CaptureRangeM:0} m)";
            return;
        }

        if (!PetCaptureRules.CanAttempt(lens, curHp, maxHp))
        {
            float pct = maxHp > 0 ? curHp * 100f / maxHp : -1f;
            _captureStatus = pct >= 0
                ? $"{target!.Name} at {pct:0}% / {curHp} HP (need <={PetCaptureRules.CaptureMaxHealthFraction:P0} or <{PetCaptureRules.CaptureMaxHealthPoints} HP)"
                : $"{target!.Name}: health unknown";
            return;
        }

        if (!_captureThrottle.MayAttempt(target!.Id, now))
        {
            _captureStatus = $"{target.Name}: {PetCaptureThrottle.MaxTriesPerTarget} tries, no capture; left alone for {_captureThrottle.BlockedSecondsLeft(target.Id, now)}s";
            return;
        }

        if (!inv.Use(lensItem))
        {
            _captureStatus = "UseObject refused";
            return;
        }
        _captureThrottle.RecordAttempt(target.Id, now);
        if (_captureThrottle.Exhausted)
        {
            // Backstop: this many lenses with no recognized capture means something is off. Stop, don't keep burning them.
            S.AutoCapture = false;
            RynthLog.Write(LogCat.IltPets,
                $"auto-capture: {PetCaptureThrottle.MaxTriesWithoutSuccess} lens uses without a capture reply; auto-capture switched off");
        }

        float shown = maxHp > 0 ? curHp * 100f / maxHp : 0f;
        _captureStatus = $"using {lens.Name} on {target.Name} ({shown:0}% / {curHp} HP)";
        _captureCooldownUntil = now + CaptureCooldownMs;
        RynthLog.Write(LogCat.IltPets,
            $"auto-capture: {lens.Name} -> 0x{target.Id:X8} '{target.Name}' d={distM:0.0}m hp={curHp}/{maxHp}");
    }

    /// <summary>
    /// Nearest attackable landscape creature within capture range, with vitals from the host when
    /// available, else the server's last health ratio (fraction-only gate). A creature with neither
    /// is skipped: no health, no capture decision.
    /// </summary>
    private bool TryFindNearestCaptureTarget(out WorldObject? best, out double bestDist, out uint curHp, out uint maxHp)
    {
        best = null;
        bestDist = double.MaxValue;
        curHp = maxHp = 0;
        var host = _ctx.Host;
        var objects = _ctx.Inventory.Cache;
        uint playerId = _ctx.Inventory.PlayerId;
        if (playerId == 0) return false;
        int pid = unchecked((int)playerId);
        string ownPrefix = _ctx.CharName.Length > 0 ? _ctx.CharName + "'s " : string.Empty;

        foreach (var wo in objects.GetLandscape())
        {
            if (wo == null || wo.Id == pid) continue;
            if (wo.ObjectClass != RynthCore.Loot.AcObjectClass.Monster) continue;
            if (ownPrefix.Length > 0 && wo.Name.StartsWith(ownPrefix, StringComparison.Ordinal)) continue;
            // Skip corpses / non-attackable when the host can tell.
            if (host.HasObjectIsAttackable && !host.ObjectIsAttackable(unchecked((uint)wo.Id))) continue;

            double d = objects.Distance(pid, wo.Id);
            if (d < 0 || d > PetCaptureRules.CaptureRangeM || d >= bestDist) continue;

            if (!TryReadCreatureVitals(wo.Id, out uint h, out uint mh)) continue;
            if (h == 0) continue; // dead

            best = wo;
            bestDist = d;
            curHp = h;
            maxHp = mh;
        }
        return best != null;
    }

    /// <summary>
    /// Absolute vitals via TryGetTargetVitals when bound; else the health ratio as a 0-1000 scale so
    /// only the fraction half of ACECustom's gate can fire.
    /// </summary>
    private bool TryReadCreatureVitals(int id, out uint cur, out uint max)
    {
        cur = max = 0;
        var host = _ctx.Host;
        if (host.HasGetTargetVitals
            && host.TryGetTargetVitals(unchecked((uint)id), out uint h, out uint mh, out _, out _, out _, out _)
            && mh > 0)
        {
            cur = h;
            max = mh;
            return true;
        }
        float ratio = HealthRatio(id);
        if (ratio < 0f) return false;
        // Synthetic scale: ratio 0.20 -> 200/1000 so IsWeakEnough's fraction half matches.
        max = 1000;
        cur = (uint)Math.Clamp((int)Math.Round(ratio * 1000f), 1, 1000);
        return true;
    }

    // ── UI (render thread): the Pets tab's ACECustom sections ────────────────

    /// <summary>The pet charms and the refill opt-in (section "pets-charms").</summary>
    public void RenderCharms()
    {
        ImGui.Separator();
        SafeImGui.TextColored(IltStyle.ColTeal, "Pet charms");
        var o = _ctx.Options;
        bool refillOff = o.IsOff(IltFeature.PetRefill);
        ImGui.TextUnformatted("Summon Essence Refill: " + (refillOff ? "disabled on this server" : _uiRefillActive ? "ON" : "off / not carried"));
        ImGui.BeginDisabled(refillOff);
        bool usePath = S.RefillFromEmpty;
        if (ImGui.Checkbox("Summon from empty essences while the refill charm is ON (spends banked pyreals)", ref usePath))
            S.RefillFromEmpty = usePath;
        ImGui.EndDisabled();
        ImGui.TextUnformatted("Universal Summoning Mastery: "
            + (o.IsOff(IltFeature.UniversalMastery) ? "disabled on this server" : _uiMasteryActive ? "ON" : "off / not carried"));
        if (!o.IsOff(IltFeature.RequireComps) && !_uiHasCasting)
            SafeImGui.TextColored(IltStyle.ColAmber, "No spell components or Infinite Casting Stone carried.");
    }

    /// <summary>Siphon-lens auto-capture: enable + which lens to use (section "pets-capture").</summary>
    public void RenderCapture()
    {
        ImGui.Separator();
        SafeImGui.TextColored(IltStyle.ColTeal, "Monster capture");
        bool captureOff = _ctx.Options.IsOff(RynthIltHub.FeatureMonsterCapture);
        ImGui.BeginDisabled(captureOff);
        bool en = S.AutoCapture;
        if (ImGui.Checkbox("Auto-capture when a creature is weak enough", ref en))
        {
            S.AutoCapture = en;
            _ctx.Post(_captureThrottle.Reset);
        }
        if (ImGui.IsItemHovered())
        {
            SafeImGui.SetTooltip(
                "Uses the selected siphon lens on the nearest attackable creature within 5 m once it is\n" +
                "at or below 20% health or under 20 HP (ACECustom MonsterCapture). Asheron's Lens skips\n" +
                "the health gate. Resonance Lens still needs a prior failed capture on the server.\n" +
                "Off by default. Stops on its own after 5 tries on one creature (it is left alone for 2 min)\n" +
                "or 15 lens uses in a row with no capture.");
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        int lens = Math.Clamp(S.CaptureLens, 0, PetCaptureRules.LensNames.Length - 1);
        if (ImGui.Combo("##petcapturelens", ref lens, PetCaptureRules.LensNames, PetCaptureRules.LensNames.Length))
            S.CaptureLens = lens;
        if (ImGui.IsItemHovered())
            SafeImGui.SetTooltip("Which siphon lens to use. Must be carried in your pack.");
        ImGui.EndDisabled();
        if (captureOff)
            SafeImGui.TextDisabled("Monster capture is disabled on this server.");
        else
            SafeImGui.TextDisabled("Status: " + _captureStatus);
    }
}
