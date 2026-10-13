// IltSections.cs - The Infinite Leaftide tabs of RynthAi's Mini Remote and the Guardian panel.
// Originally SilentKelpie's ILT Hub sections (RynthAi IltHub/); moved to RynthILT 2026-10-13.
//
// Character, Banking, Gear, Charms and Games are Mini Remote tabs: RynthAi's MiniRemoteHud draws
// the tab bar (after its Remote, Quests and Pets tabs) and asks RynthILT for each tab's body
// (RynthILT.Hub GetTabs / RenderTab); "/ra hub open <section>" picks one. Guardian is a RynthCore
// ILT panel instead (the bar's ILT button, next to Bounties; "/rc guardian"), drawn by this
// plugin through RynthPluginRenderEmbed("guardian"). The Quests and Pets tabs are RynthAi's
// (every server: /myquests and summoning essences aren't ILT's).
using System;
using System.Numerics;

namespace RynthCore.Plugin.RynthILT.Hub;

/// <summary>ILT sections: the Mini Remote's ILT tabs, then the Guardian panel.</summary>
internal enum IltSection
{
    Character,
    Banking,
    Gear,
    Games,
    /// <summary>Registry charms (acquired / status / tier / server) and the Guardian Hand.</summary>
    Charms,
    // Keep Guardian last.
    Guardian,
}

internal static class IltSections
{
    /// <summary>The ILT tabs, in tab-bar order (RynthAi puts them after its own tabs).</summary>
    public static readonly IltSection[] Tabs =
    {
        IltSection.Character, IltSection.Banking, IltSection.Gear, IltSection.Charms, IltSection.Games,
    };

    /// <summary>The Mini Remote's own tab (the remote itself): its tab id.</summary>
    public const string RemoteTab = "";

    public static string Label(IltSection s) => s switch
    {
        IltSection.Character => "Character",
        IltSection.Banking => "Banking",
        IltSection.Gear => "Gear",
        IltSection.Games => "Games",
        IltSection.Charms => "Charms",
        IltSection.Guardian => "Guardian",
        _ => s.ToString(),
    };

    /// <summary>
    /// The section's tab id: saved as RynthAi's HudState.MiniRemoteTab and sent by the engine's
    /// dashboard ("/ra hub tab &lt;id&gt;", "/rc miniremote tab &lt;id&gt;"). TryParse reads it back.
    /// </summary>
    public static string TabId(IltSection s) => s switch
    {
        IltSection.Character => "character",
        IltSection.Banking => "banking",
        IltSection.Gear => "gear",
        IltSection.Games => "games",
        IltSection.Charms => "charms",
        IltSection.Guardian => "guardian",
        _ => s.ToString().ToLowerInvariant(),
    };

    /// <summary>True for the sections drawn as Mini Remote tabs (all but the Guardian panel).</summary>
    public static bool IsTab(IltSection s) => s != IltSection.Guardian;

    /// <summary>"quests" / "pets" (and their aliases): RynthAi's universal tabs, not ILT sections.</summary>
    public static bool IsUniversalTabName(string text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "quests" or "quest" or "pets" or "pet" => true,
        _ => false,
    };

    /// <summary>Parses a "/ra hub open" section name (a few aliases accepted).</summary>
    public static bool TryParse(string text, out IltSection section)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "character": case "char": case "rates": section = IltSection.Character; return true;
            case "banking": case "bank": section = IltSection.Banking; return true;
            case "gear": section = IltSection.Gear; return true;
            case "games": case "game": section = IltSection.Games; return true;
            case "charms": case "charm": section = IltSection.Charms; return true;
            case "guardian": case "temple": case "riddle": section = IltSection.Guardian; return true;
            default: section = IltSection.Character; return false;
        }
    }

    /// <summary>
    /// Size of a Mini Remote tab's body (the old section window's default size): the tab draws in a
    /// child this big, and the Mini Remote (or the engine window hosting it) fits to it.
    /// </summary>
    public static Vector2 TabSize(IltSection section) => section switch
    {
        IltSection.Character => new Vector2(520, 440),
        IltSection.Banking => new Vector2(560, 520),
        IltSection.Gear => new Vector2(560, 520),
        IltSection.Charms => new Vector2(560, 520),
        IltSection.Games => new Vector2(520, 440),
        _ => new Vector2(520, 440),
    };

    /// <summary>"show" / "hide" / "toggle" (anything else toggles) applied to the current state.</summary>
    public static bool Resolve(string mode, bool current) => (mode ?? string.Empty).ToLowerInvariant() switch
    {
        "show" or "on" or "open" => true,
        "hide" or "off" or "close" => false,
        _ => !current,
    };
}
