// ServerPackPages.cs - Pages a server plugin adds beside its server card (RynthConquest's Conquest and
// Leaderboards pages, RynthAelrynth's Leaderboards, ...).
//
// A server's own features need somewhere to show. The ServerPackPlugin.ServerPages hook lists them
// once, at construction; the shared code then
//   * declares each one in the sections manifest as a primary view of the shared "Server" section,
//     shown only while this plugin's server is active (contribution "server": <id>), so RynthCore's
//     Mini Remote gives it a sub-tab next to the server card;
//   * draws it through RynthPluginRenderEmbed under the surface "section:server-<id>.<page>"
//     (":popout" added in a popped-out window), into the engine's current window and context;
//   * shows the same pages as tabs of the standalone "/rserver" window, after the card.
// Draw runs on the render thread: a page reads its state under its own lock and queues anything it
// sends for the plugin tick (ServerPackPlugin.RunOnTick).
// Pure (no ImGui, no host calls): RynthServerPack.Tests covers it.
using System;
using System.Collections.Generic;

namespace RynthCore.Plugin.ServerPack;

/// <summary>
/// One page a server plugin adds. <paramref name="Id"/> is 1-16 of a-z, 0-9 and '-' (part of the
/// surface name); <paramref name="Draw"/> draws the body into the current ImGui window (render thread).
/// </summary>
internal sealed record ServerPage(string Id, string Label, string Tooltip, Action Draw);

internal static class ServerPackPages
{
    /// <summary>At most this many pages per plugin (the Mini Remote shows them as one row of tabs).</summary>
    public const int MaxPages = 6;
    /// <summary>The longest page id (keeps "section:server-&lt;32-char id&gt;.&lt;page&gt;" under the engine's 64).</summary>
    public const int MaxIdLength = 16;
    /// <summary>The engine caps contribution labels at 48 characters.</summary>
    public const int MaxLabelLength = 48;
    /// <summary>Page views open at this size in the Mini Remote (it fits the window to the view).</summary>
    public const int Width = 540, Height = 520;

    /// <summary>True for a page id: 1-16 of a-z, 0-9 and '-'.</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength) return false;
        foreach (char c in id)
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-')) return false;
        return true;
    }

    /// <summary>"section:server-conquest.bank": the page's surface.</summary>
    public static string SurfaceFor(string serverId, string pageId) => ServerPackModel.SurfaceFor(serverId) + "." + pageId;

    /// <summary>
    /// The page id a surface names for this server ("section:server-conquest.bank" -> "bank"), or
    /// null for the card, another server's surface or anything else. ":popout" is ignored.
    /// </summary>
    public static string? PageIdOf(string? surface, string serverId)
    {
        if (string.IsNullOrEmpty(surface)) return null;
        string name = surface.EndsWith(":popout", StringComparison.Ordinal) ? surface[..^":popout".Length] : surface;
        string prefix = ServerPackModel.SurfaceFor(serverId) + ".";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string id = name[prefix.Length..];
        return IsValidId(id) ? id : null;
    }

    /// <summary>
    /// The pages kept from what a plugin listed: valid, unique ids, a label, at most
    /// <see cref="MaxPages"/>; each one dropped is reported to <paramref name="log"/>.
    /// </summary>
    public static List<ServerPage> Validate(IEnumerable<ServerPage>? pages, Action<string>? log = null)
    {
        var kept = new List<ServerPage>();
        if (pages == null) return kept;
        foreach (ServerPage p in pages)
        {
            if (p == null) continue;
            string why = !IsValidId(p.Id) ? "its id isn't 1-16 of a-z, 0-9 and '-'"
                : string.IsNullOrWhiteSpace(p.Label) ? "it has no label"
                : p.Draw == null ? "it has nothing to draw"
                : kept.Exists(k => k.Id == p.Id) ? "another page has its id"
                : kept.Count >= MaxPages ? $"a plugin has at most {MaxPages} pages"
                : string.Empty;
            if (why.Length > 0) { log?.Invoke($"Page '{p.Id}' left out: {why}."); continue; }
            string label = p.Label.Trim();
            kept.Add(p with { Label = label.Length <= MaxLabelLength ? label : label[..MaxLabelLength], Tooltip = p.Tooltip ?? string.Empty });
        }
        return kept;
    }
}
