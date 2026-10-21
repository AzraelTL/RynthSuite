// IltSectionsManifest.cs - What RynthILT adds to RynthCore's Mini Remote sections (RynthPluginGetSectionsJson,
// schema 1: UI/Sections/SectionManifest.cs in RynthCore).
//
//   always (Infinite Leaftide views, shown while RynthCore treats this world as ILT)
//     Session Stats ("char")  the ILT session extras            section:ilt-rates
//     Fellowship              ILT's /fship commands              section:ilt-fellowship
//     Quests (RynthILT's own) the /qb list                       section:ilt-questbonus
//   while the Hub shows them (an ILT world; each once the server reports its feature)
//     Inventory               Gear, Charms, Banking              section:gear|charms|banking
//     Games (RynthILT's own)  Games                              section:games
//     Nav                     Vaulted Dungeons (master switch)   section:vaulted
// The Mini Remote tabs were listed by RynthAi, which hosted them, until 2026-10-20; RynthILT lists and
// draws them itself now (RynthPluginRenderEmbed "section:<tab>"), so RynthAi's manifest names no
// server's tabs. Their names stay the same, so "/rc miniremote tab charms|banking" finds them.
// Pure: the tabs come in as a list, so the tests build it offline.
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using RynthCore.Plugin.RynthILT.Hub;

namespace RynthCore.Plugin.RynthILT;

internal static class IltSectionsManifest
{
    /// <summary>RynthCore's id for Infinite Leaftide.</summary>
    public const string Server = "ilt";

    /// <summary>One Mini Remote tab as the Hub has it now.</summary>
    internal readonly record struct Tab(string Id, float Width, float Height, bool Shown, bool Available);

    private const string NotReportedNote = "Shows once the server reports this ILT feature on (the ILT settings can ask again).";

    /// <summary>Where each tab goes: section, label, order, tooltip. A tab not listed here isn't offered.</summary>
    internal static readonly Dictionary<string, (string Section, string Label, int Order, string Tooltip)> Placement = new()
    {
        ["gear"] = ("inventory", "Gear", 210, "ILT gear tools (RynthILT)."),
        ["charms"] = ("inventory", "Charms", 220, "ILT charms and Guardian Hands: acquired, status, tier, toggles (RynthILT)."),
        ["banking"] = ("inventory", "Banking", 230, "Your ILT bank: balances, deposits, withdrawals and transfers (RynthILT)."),
        ["games"] = ("games", "Games", 100, "ILT games (RynthILT)."),
        // Listed only while the Hub shows the tab: the master switch is on, on an ILT world.
        ["vaulted"] = ("nav", "Vaulted Dungeons", 220,
            "Keep the character in one Vaulted Dungeon (RynthILT; master switch: /ilt settings or Settings > Navigation)."),
    };

    /// <summary>The surface of a tab's view ("section:gear").</summary>
    public static string SurfaceOf(string tabId) => "section:" + tabId;

    /// <summary>The tab a "section:&lt;tab&gt;" surface draws, when it names one placed here.</summary>
    public static bool TryTabOf(string surface, out IltSection section)
    {
        section = IltSection.Gear;
        if (!surface.StartsWith("section:", System.StringComparison.Ordinal)) return false;
        string id = surface.Substring("section:".Length);
        if (!Placement.ContainsKey(id) || !IltSections.TryParse(id, out section) || !IltSections.IsTab(section)) return false;
        return IltSections.TabId(section) == id;
    }

    /// <summary>The Hub's tabs now (empty without a Hub). Render thread.</summary>
    public static List<Tab> TabsOf(IltHubController? hub)
    {
        var list = new List<Tab>();
        if (hub == null) return list;
        foreach (IltSection s in IltSections.Tabs)
        {
            var size = IltSections.TabSize(s);
            list.Add(new Tab(IltSections.TabId(s), size.X, size.Y, hub.TabShown(s), hub.SectionAvailable(s)));
        }
        return list;
    }

    /// <summary>The manifest JSON (UTF-8 text, schema 1).</summary>
    public static string Build(IReadOnlyList<Tab> tabs)
    {
        using var stream = new MemoryStream(4096);
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteStartArray("sections");
            Section(w, "quests", "Quests", 1160);
            Section(w, "games", "Games", 1150);
            w.WriteEndArray();
            w.WriteStartArray("contributions");
            View(w, "char", "ILT session rates (RynthILT)", RynthILTPlugin.RatesSurface, primary: false, 0, 220, enabled: true, note: "",
                 "Enlightened Coins, pyreals and item conversions this session, on the same clock as RynthAi's session stats (RynthILT).", 120);
            View(w, "fellowship", "ILT fellowship (RynthILT)", RynthILTPlugin.FellowshipSurface, primary: false, 0, 110, enabled: true, note: "",
                 "Infinite Leaftide's /fship commands: create, invite, dismiss, invite your landblock, list nearby, leave, disband (RynthILT; also /ilt fellow).", 120);
            View(w, "quests", "Quest bonus (/qb)", RynthILTPlugin.QuestBonusSurface, primary: true, 520, 440, enabled: true, note: "",
                 "Infinite Leaftide's quest bonus list: completed and incomplete /qb flags (RynthILT; the quest list, timers and rewards are RynthOracle's, /ro).", 100);
            foreach (Tab t in tabs)
            {
                if (!t.Shown || !Placement.TryGetValue(t.Id, out var place)) continue;
                View(w, place.Section, place.Label, SurfaceOf(t.Id), primary: true, t.Width, t.Height, t.Available,
                     t.Available ? string.Empty : NotReportedNote, place.Tooltip, place.Order);
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Section(Utf8JsonWriter w, string id, string label, int order)
    {
        w.WriteStartObject();
        w.WriteString("id", id);
        w.WriteString("label", label);
        w.WriteNumber("order", order);
        w.WriteEndObject();
    }

    private static void View(Utf8JsonWriter w, string section, string label, string surface, bool primary, float width, float height,
                             bool enabled, string note, string tooltip, int order)
    {
        w.WriteStartObject();
        w.WriteString("section", section);
        w.WriteString("server", Server);
        w.WriteString("label", label);
        w.WriteString("surface", surface);
        w.WriteBoolean("primary", primary);
        if (width > 0) w.WriteNumber("width", width);
        if (height > 0) w.WriteNumber("height", height);
        w.WriteBoolean("enabled", enabled);
        if (note.Length > 0) w.WriteString("note", note);
        if (tooltip.Length > 0) w.WriteString("tooltip", tooltip);
        w.WriteNumber("order", order);
        w.WriteEndObject();
    }
}
