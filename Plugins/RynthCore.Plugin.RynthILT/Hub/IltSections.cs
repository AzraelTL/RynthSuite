// IltSections.cs - The Infinite Leaftide tabs of RynthAi's Mini Remote and the Guardian panel.
// Originally SilentKelpie's ILT Hub sections (RynthAi IltHub/); moved to RynthILT 2026-10-13.
//
// Banking, Gear, Charms, Games and Vaulted Dungeons are Mini Remote tabs: RynthAi's MiniRemoteHud draws the tab bar
// (after its Remote, Pets and Crafting tabs) and asks RynthILT for each tab's body (RynthILT.Hub
// GetTabs / RenderTab); "/ra hub open <section>" picks one, and with RynthCore's sections rail
// RynthAi lists them as "section:<tab>" views (Inventory). Guardian is a RynthCore ILT panel
// instead (the bar's ILT button, next to Bounties; "/rc guardian"), drawn by this plugin through
// RynthPluginRenderEmbed("guardian"). The Pets tab is RynthAi's (every server: summoning essences
// aren't ILT's); quests are RynthOracle's.
//
// Character was a Mini Remote tab until 2026-10-13. It moved out:
//   the character overview and session -> RynthOracle's Character tab ("/ro character");
//   the ILT session rates               -> RynthCore's Mini Remote "Session Stats" section
//                                          (RynthILT's own surface "section:ilt-rates");
//   the force-server override           -> RynthCore Options > Diagnostics only.
// Its names still parse, so old links and commands land on the new home (MovedCommand). Charms and
// Banking are Infinite Leaftide's own screens and stay RynthILT's tabs.
using System;
using System.Numerics;

namespace RynthCore.Plugin.RynthILT.Hub;

/// <summary>ILT sections: the Mini Remote's ILT tabs, the one that moved out, then the Guardian panel.</summary>
internal enum IltSection
{
    /// <summary>Moved out (2026-10-13): RynthOracle's Character tab and the Session Stats section.</summary>
    Character,
    Banking,
    Gear,
    Games,
    /// <summary>
    /// Vaulted Dungeons keeper (portal-recall gem + kill-client). A Mini Remote tab on ILT worlds only,
    /// and only while RynthAi's "Vaulted Dungeons" master switch (Settings > Navigation) is on.
    /// </summary>
    VaultedDungeons,
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
        IltSection.Banking, IltSection.Gear, IltSection.Charms, IltSection.Games, IltSection.VaultedDungeons,
    };

    /// <summary>The Mini Remote's own tab (the remote itself): its tab id.</summary>
    public const string RemoteTab = "";

    /// <summary>RynthOracle's Character tab: the character overview and this session.</summary>
    public const string OracleCharacterCommand = "/ro character";

    public static string Label(IltSection s) => s switch
    {
        IltSection.Character => "Character",
        IltSection.Banking => "Banking",
        IltSection.Gear => "Gear",
        IltSection.Games => "Games",
        IltSection.VaultedDungeons => "Vaulted Dungeons",
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
        IltSection.VaultedDungeons => "vaulted",
        IltSection.Charms => "charms",
        IltSection.Guardian => "guardian",
        _ => s.ToString().ToLowerInvariant(),
    };

    /// <summary>True for the sections drawn as Mini Remote tabs (Banking, Gear, Charms, Games, Vaulted Dungeons).</summary>
    public static bool IsTab(IltSection s) => s is IltSection.Banking or IltSection.Gear or IltSection.Charms or IltSection.Games
        or IltSection.VaultedDungeons;

    /// <summary>
    /// Where a section that moved out of the Mini Remote lives now, as the chat command that opens
    /// it; null for a section that is still RynthILT's (a tab or the Guardian panel).
    /// </summary>
    public static string? MovedCommand(IltSection s) => s switch
    {
        IltSection.Character => OracleCharacterCommand,
        _ => null,
    };

    /// <summary>"quests" / "pets" (and their aliases): not ILT sections (RynthAi's Pets tab, RynthOracle's quests).</summary>
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
            case "vaulted": case "vaulteddungeons": case "vaulted-dungeons":
                section = IltSection.VaultedDungeons; return true;
            case "charms": case "charm": section = IltSection.Charms; return true;
            case "guardian": case "temple": case "riddle": section = IltSection.Guardian; return true;
            default: section = IltSection.Gear; return false;
        }
    }

    /// <summary>
    /// Size of a Mini Remote tab's body (the old section window's default size): the tab draws in a
    /// child this big, and the Mini Remote (or the engine window hosting it) fits to it.
    /// </summary>
    public static Vector2 TabSize(IltSection section) => section switch
    {
        IltSection.Banking => new Vector2(560, 520),
        IltSection.Gear => new Vector2(560, 520),
        IltSection.Charms => new Vector2(560, 520),
        IltSection.Games => new Vector2(520, 440),
        IltSection.VaultedDungeons => new Vector2(360, 420),
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
