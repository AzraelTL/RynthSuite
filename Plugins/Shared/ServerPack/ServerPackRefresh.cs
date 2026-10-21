// ServerPackRefresh.cs - The Refresh button a server page starts with, and the status line beside it
// (as RynthOracle's pages have it): the page's own command is asked for again, refused for a few
// seconds after it went out (so the button can't spam the server), and the line waits for the reply
// ("Waiting...", then "Updated hh:mm:ss", or "No reply yet" after a while).
// Render thread: Draw runs inside the page's own lock; the page's refresh callback only decides and
// queues what to send (ServerPackPlugin.RunOnTick).
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.ServerPack;

/// <summary>What a Refresh did, and (when it asked the server) how to tell the reply has landed.</summary>
internal sealed record ServerRefreshResult(string What, Func<bool>? Done = null);

internal sealed class ServerRefreshRow
{
    public const long CooldownMs = 3000;
    public const long ReplyWaitMs = 15_000;
    public const string TooSoon = "That was just asked for; wait a few seconds before asking again.";

    private static readonly Vector4 Yellow = new(0.95f, 0.85f, 0.35f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.40f, 1f);

    private readonly Dictionary<string, long> _sent = new();
    private string _scope = string.Empty, _what = string.Empty;
    private Func<bool>? _done;
    private bool _doneSeen;
    private long _atMs;
    private DateTime _atLocal, _doneLocal;

    /// <summary>The same request (by <paramref name="key"/>) twice within <see cref="CooldownMs"/> is refused.</summary>
    public bool CoolingDown(string key, long nowMs)
    {
        if (_sent.TryGetValue(key, out long t) && nowMs - t < CooldownMs) return true;
        _sent[key] = nowMs;
        return false;
    }

    /// <summary>Forgets the status line and the cooldowns (logout).</summary>
    public void Reset()
    {
        _sent.Clear();
        _scope = _what = string.Empty;
        _done = null;
    }

    /// <summary>
    /// The row: Refresh (tooltip <paramref name="help"/>) and, for the page that last refreshed
    /// (<paramref name="scope"/>), what happened.
    /// </summary>
    public void Draw(string scope, string help, Func<ServerRefreshResult> refresh)
    {
        long now = Environment.TickCount64;
        if (ImGui.SmallButton("Refresh##refresh_" + scope))
        {
            ServerRefreshResult r;
            try { r = refresh(); }
            catch (Exception ex) { r = new ServerRefreshResult("Couldn't refresh: " + ex.Message); }
            _scope = scope;
            _what = r.What;
            _done = r.Done;
            _doneSeen = false;
            _atMs = now;
            _atLocal = DateTime.Now;
        }
        if (help.Length > 0 && ImGui.IsItemHovered()) SafeImGui.SetTooltip(help);
        if (_scope != scope || _what.Length == 0) return;

        ImGui.SameLine();
        if (_done != null && !_doneSeen && _done())
        {
            _doneSeen = true;
            _doneLocal = DateTime.Now;
        }
        if (_done != null && !_doneSeen)
        {
            if (now - _atMs > ReplyWaitMs) SafeImGui.TextColored(Red, "No reply yet. Press Refresh to ask again.");
            else SafeImGui.TextColored(Yellow, _what + " Waiting for the reply...");
        }
        else if (_done != null)
            SafeImGui.TextDisabled($"Updated {_doneLocal:HH:mm:ss}. {_what}");
        else
            SafeImGui.TextDisabled($"Refreshed {_atLocal:HH:mm:ss}. {_what}");
    }
}
