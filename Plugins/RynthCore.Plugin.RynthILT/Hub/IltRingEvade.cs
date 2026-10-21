// IltRingEvade.cs — Infinite Leaftide's Empyrean Handler lethal ring: hear it, and have RynthAi jump clear.
// Moved out of RynthAi's CombatManager 2026-10-20 (Silentkelpie's PR 12): the Handler is ACECustom
// content, so the detection and its setting are RynthILT's; RynthAi keeps the generic back-away it
// does on request (RynthAi.Hub BackAwayFrom: face the danger, run-back jump, hold attacks until the
// jump settles, re-engage the same target; only while its macro runs with combat on).
//
// ACECustom's Handler emote: "The handler briefly channels infused Empyrean hatred to release a lethal
// ring." The ring can't be melee-evaded; inside its cast radius the character dies. On that line, when
// an Empyrean Handler is near, ask RynthAi to back away from the nearest one (12 yards, or Ring Range
// when RynthAi's is larger). One request per HandlerRingCooldownMs.
// Setting: IltHubState.Combat.HandlerRingEvade (on by default; the ILT settings view, "/ilt settings").
using System;
using RynthCore.Loot;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthILT.Hub;

internal sealed class IltRingEvade : IIltFeature
{
    internal const string ChannelHint = "channels infused Empyrean hatred to release a lethal ring";
    internal const string ChannelStart = "The handler briefly channels";
    internal const string HandlerName = "Empyrean Handler";
    /// <summary>Yards: the Handler's lethal ring is treated as at least this wide (RynthAi widens it to Ring Range).</summary>
    internal const float DangerYards = 12f;
    /// <summary>Back-jump charge (ms): run-back + jump must clear about the ring's radius.</summary>
    internal const int JumpHoldMs = 750;
    internal const long CooldownMs = 4_000;

    private readonly IltHubContext _ctx;
    private long _lastRequestAt = long.MinValue;

    public IltRingEvade(IltHubContext ctx) => _ctx = ctx;

    /// <summary>What the last ring line did (for the log and the tests).</summary>
    public string LastOutcome { get; private set; } = string.Empty;

    /// <summary>
    /// True when <paramref name="text"/> is the Handler's channel line (not a player repeating it).
    /// The channel is an Emote (0x0C), which counts as player speech elsewhere, so it is anchored on
    /// the phrase that starts the line; mid-line only for a chat type that can't be player speech.
    /// </summary>
    internal static bool IsChannelLine(string? text, int chatType)
    {
        if (string.IsNullOrEmpty(text)) return false;
        bool anchored =
            ChatSource.StartsWithMarker(text, ChannelStart, StringComparison.OrdinalIgnoreCase)
            || (ChatSource.StartsWithMarker(text, "The handler", StringComparison.OrdinalIgnoreCase)
                && text.IndexOf(ChannelHint, StringComparison.OrdinalIgnoreCase) >= 0);
        if (anchored) return true;
        // Mid-line only when the type is clearly not player speech (some clients strip "The ").
        if (chatType is ChatSource.Speech or ChatSource.Tell or ChatSource.OutgoingTell or ChatSource.Channel
            or ChatSource.ChannelSend or ChatSource.Allegiance or ChatSource.Fellowship)
            return false;
        if (text.IndexOf(ChannelHint, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return text.IndexOf(" says,", StringComparison.OrdinalIgnoreCase) < 0
               && text.IndexOf(" tells you,", StringComparison.OrdinalIgnoreCase) < 0;
    }

    /// <summary>
    /// A chat line with its type (pump thread; IltHubController.OnChat, ILT only). On the Handler's
    /// channel line asks RynthAi to back away from the nearest Handler. Never eats the line.
    /// </summary>
    public void OnChatLine(string text, int chatType)
    {
        if (!_ctx.State.Combat.HandlerRingEvade || !IsChannelLine(text, chatType)) return;
        long now = IltHubContext.NowMs;
        if (_lastRequestAt != long.MinValue && now - _lastRequestAt < CooldownMs) return;

        if (!TryFindNearestHandler(out int handlerId, out double dist))
        {
            Outcome("Handler lethal-ring channel heard, but no Empyrean Handler is nearby: no jump.");
            return;
        }
        _lastRequestAt = now;
        int r = _ctx.Ai.BackAwayFrom(unchecked((uint)handlerId), DangerYards, JumpHoldMs);
        Outcome(r switch
        {
            RynthAiHub.BackAwayStarted => $"Empyrean Handler lethal ring: RynthAi backs away from it ({dist:0.0} m).",
            RynthAiHub.BackAwayOutside => $"Empyrean Handler lethal ring: the Handler is {dist:0.0} m away, outside the ring; staying put.",
            RynthAiHub.BackAwayUnknownObject => "Empyrean Handler lethal ring: RynthAi has no position for the Handler; no jump.",
            RynthAiHub.BackAwayJumpRefused => "Empyrean Handler lethal ring: in range, but RynthAi's back-jump did not start.",
            int.MinValue => "Empyrean Handler lethal ring: " + RynthAiClient.NotLoaded + " (the back-away is RynthAi's).",
            _ => "Empyrean Handler lethal ring: RynthAi isn't fighting (macro or combat off), so it doesn't take the keyboard.",
        });
    }

    private void Outcome(string text)
    {
        LastOutcome = text;
        RynthLog.Write(LogCat.IltHub, "[RingEvade] " + text);
    }

    /// <summary>The nearest Empyrean Handler the client knows (a creature with that in its name).</summary>
    private bool TryFindNearestHandler(out int id, out double dist)
    {
        id = 0;
        dist = double.MaxValue;
        var objects = _ctx.Inventory.Cache;
        int me = unchecked((int)_ctx.Inventory.PlayerId);
        if (me == 0) return false;
        foreach (var wo in objects.GetLandscape())
        {
            if (wo == null || wo.ObjectClass != AcObjectClass.Monster) continue;
            if (wo.Name.IndexOf(HandlerName, StringComparison.OrdinalIgnoreCase) < 0) continue;
            double d = objects.Distance(me, wo.Id);
            if (d >= dist) continue;
            dist = d;
            id = wo.Id;
        }
        return id != 0;
    }

    // ── IIltFeature ─────────────────────────────────────────────────────────

    public void Tick(long nowMs) { }

    /// <summary>Unused: the controller passes the chat type (OnChatLine).</summary>
    public bool OnChat(string text) => false;

    public void OnLogout() => _lastRequestAt = long.MinValue;
}
