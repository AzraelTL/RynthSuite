// ============================================================================
//  RynthSuite - Plugins/Shared/RynthIltHubApi.cs
//  The "RynthILT.Hub" interface: a server plugin's features as RynthAi's Mini
//  Remote shows them (RynthILT's Infinite Leaftide ones; any server plugin may serve
//  the same layout as "RynthServer.Hub", which RynthAi asks the active server's owner
//  for first). The Mini Remote stays RynthAi's (its window, tab bar, Remote, Pets and
//  Crafting tabs) and knows no server's content: the plugin supplies its tabs and the
//  Pets tab's sub-tabs, its sections drawn inside RynthAi's surfaces (Remote, Pets,
//  Options, menu), its feature bits, texts and mirrored settings, its "/ra" verbs and
//  the pet summon-from-empty gate. Field names say ILT for history; the meaning is
//  "the plugin's server" (IsIltWorld, GetForceIlt).
//  Compiled into both plugins (linked source), so the layout can't drift.
//
//  Rules (as the RynthAi.* interfaces, Shared/RynthAiScriptApi.cs):
//  - Append-only: new functions go at the end, and Version goes up.
//  - Strings are UTF-8, NUL-terminated.
//  - Threads: each function says where it may be called. The render-thread ones
//    draw into the caller's current ImGui window and context (the engine's cimgui,
//    shared by both plugins).
//  - Every function returns safely (no exception crosses the boundary), and returns
//    0 / "unknown" once RynthILT has shut down.
//  - The table is allocated once per loaded copy of RynthILT and never freed: its
//    functions stay callable from the render thread while plugins shut down (a
//    plugin DLL is never unloaded, see the engine's PluginManager.PluginsUnloaded).
//    Shutdown sets Version to 0; a caller re-queries (host GetPluginInterface)
//    when it sees that, after a plugin reload, or every few seconds.
// ============================================================================

using System.Runtime.InteropServices;

namespace RynthCore.Plugin.Shared;

/// <summary>Constants of the "RynthILT.Hub" interface.</summary>
public static class RynthIltHub
{
    /// <summary>The providing plugin's display name.</summary>
    public const string PluginName = "RynthILT";

    /// <summary>Tab flag: the Mini Remote shows this tab now (an ILT world, or forced, and the tab is usable).</summary>
    public const uint TabShown = 1;
    /// <summary>Tab flag: the server reports the tab's feature on (Character is always available).</summary>
    public const uint TabAvailable = 2;

    // Sections RynthAi draws through RenderSection (each draws nothing off ILT).
    /// <summary>Remote tab, Bank section: the ILT bank balances.</summary>
    public const string SectionRemoteBank = "remote-bank";
    /// <summary>Remote tab, Guardian line: the Temple guardian's last answer.</summary>
    public const string SectionRemoteGuardian = "remote-guardian";
    /// <summary>Quests tab: the "Quest bonus (/qb)" list.</summary>
    public const string SectionQuestBonus = "quests-qb";
    /// <summary>Pets tab: the /pets and /shinies logs.</summary>
    public const string SectionPetLogs = "pets-logs";
    /// <summary>
    /// Pets tab, Breeding sub-tab: the pet breeding tracker, the sub-tab's whole body (RynthILT 0.3.1+;
    /// older copies draw nothing). Since 2026-10-20 RynthAi shows it because GetSubTabs(TabParentPets)
    /// lists it (RynthILT: an ILT world unless FeaturePetBreeding is off); older RynthAi decided itself.
    /// </summary>
    public const string SectionPetBreeding = "pets-breeding";
    /// <summary>
    /// Pets tab, under the roster: the ACECustom pet charms (Summon Essence Refill and its "summon from
    /// empty" opt-in, Universal Summoning Mastery, the casting supplies warning). RynthILT 2026-10-20+;
    /// it was RynthAi's. Draws its own leading separator; nothing off ILT.
    /// </summary>
    public const string SectionPetCharms = "pets-charms";
    /// <summary>Pets tab, under the charms: ACECustom siphon-lens auto-capture (switch, lens, status). As SectionPetCharms.</summary>
    public const string SectionPetCapture = "pets-capture";

    /// <summary>GetSubTabs parent: RynthAi's Pets tab (its first sub-tab, the roster, is RynthAi's own).</summary>
    public const string TabParentPets = "pets";

    /// <summary>
    /// GetSetting / SetSetting key: the Vaulted Dungeons keeper's master switch (0 off, 1 on; RynthILT's
    /// per-character setting since 2026-10-20, RynthAi's profile's before). RynthAi mirrors it into the
    /// engine's Settings > Navigation row; -1 (no row) off ILT.
    /// </summary>
    public const string SettingVaultedDungeons = "vaulted-dungeons";
    /// <summary>Mini Remote menu, under Infinite Leaftide: why no ILT feature is on, the Force-ILT override, Refresh.</summary>
    public const string SectionIltStatus = "ilt-status";
    /// <summary>Mini Remote menu without RynthCore's server list: RynthILT's own Infinite Leaftide items.</summary>
    public const string SectionServerMenu = "server-menu";
    /// <summary>
    /// Remote tab, Stats section, under RynthAi's per-hour lines: the server's own session rates (RynthILT:
    /// Enlightened Coins and pyreals per hour). RynthILT 2026-10-20+; RynthAi drew them from GetRates before.
    /// </summary>
    public const string SectionRemoteStats = "remote-stats";
    /// <summary>
    /// The Remote tab's Options popup, under RynthAi's own section switches: the server's Remote sections'
    /// switches (RynthILT: Bank, Guardian answer; saved by RynthILT, which then draws or skips SectionRemoteBank /
    /// SectionRemoteGuardian). RynthILT 2026-10-20+; they were RynthAi's huds.json flags.
    /// </summary>
    public const string SectionRemoteOptions = "remote-options";

    /// <summary>GetText id: the tail RynthAi adds to its one-line session report ("" or ", Coins 1.2k/h, Pyr 4.5m/h").</summary>
    public const string TextSessionReport = "session-report";
    /// <summary>GetText id: the RynthCore server id this plugin's tabs and menu entries belong to ("ilt").</summary>
    public const string TextServerId = "server-id";

    /// <summary>Feature keys RynthAi reads (GetFeature).</summary>
    public const string FeatureQuests = "quests";
    public const string FeaturePetRefill = "pet_refill";
    public const string FeatureUniversalMastery = "universal_mastery";
    /// <summary>ACECustom siphon lenses (monster capture into a pet essence).</summary>
    public const string FeatureMonsterCapture = "monster_capture";
    public const string FeatureRequireComps = "require_spell_comps";
    /// <summary>Pet breeding (the Pets tab's Breeding sub-tab is hidden while the server reports it off).</summary>
    public const string FeaturePetBreeding = "pet_breeding";
}

/// <summary>One ILT tab as GetTabs hands it out. Blittable.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IltTabV1
{
    /// <summary>The tab id (UTF-8, NUL-terminated): saved as the Mini Remote's tab, "/ra hub open &lt;id&gt;".</summary>
    public fixed byte Id[24];
    /// <summary>The tab bar label (UTF-8, NUL-terminated).</summary>
    public fixed byte Label[32];
    /// <summary>The size of the tab body (the child it draws in).</summary>
    public float Width;
    public float Height;
    /// <summary>RynthIltHub.Tab* bits.</summary>
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct RynthIltHubApiV1
{
    public const string InterfaceName = "RynthILT.Hub";
    public const uint InterfaceVersion = 1;

    /// <summary>
    /// The same table under a server-neutral name (2026-10-20): RynthAi asks the engine's active server's
    /// owner plugin for it first, so any server plugin can offer the Mini Remote tabs, sections and verbs
    /// in this layout. RynthILT serves both names.
    /// </summary>
    public const string NeutralInterfaceName = "RynthServer.Hub";

    /// <summary>
    /// The table's size up to and including Command, as every RynthILT since the interface's first
    /// release fills it. Readers check StructSize against this (not sizeof, which grows as functions
    /// are appended) and check for the appended functions with <see cref="Has"/>.
    /// </summary>
    public static uint BaseStructSize => (uint)(2 * sizeof(uint) + 9 * sizeof(nint));

    /// <summary>Whether a table RynthILT built is big enough to hold the function at <paramref name="offset"/>.</summary>
    public static bool Has(RynthIltHubApiV1* t, int offset) => t != null && t->StructSize >= (uint)(offset + sizeof(nint));

    /// <summary>Byte offset of the n-th function appended after the base table (0 = AllowSummonOnEmpty).</summary>
    private static int Appended(int n) => (int)BaseStructSize + n * sizeof(nint);

    /// <summary>Offset of <see cref="AllowSummonOnEmpty"/> (for <see cref="Has"/>).</summary>
    public static int AllowSummonOnEmptyOffset => Appended(0);
    /// <summary>Offset of <see cref="GetSubTabs"/>.</summary>
    public static int GetSubTabsOffset => Appended(1);
    /// <summary>Offset of <see cref="GetSetting"/>.</summary>
    public static int GetSettingOffset => Appended(2);
    /// <summary>Offset of <see cref="SetSetting"/>.</summary>
    public static int SetSettingOffset => Appended(3);
    /// <summary>Offset of <see cref="GetText"/>.</summary>
    public static int GetTextOffset => Appended(4);

    public uint Version;      // InterfaceVersion of the table RynthILT filled; 0 once it has shut down
    public uint StructSize;   // sizeof(RynthIltHubApiV1) as RynthILT built it

    /// <summary>IsIltWorld(): 1 when this world is Infinite Leaftide / ACECustom (or forced). Any thread.</summary>
    public delegate* unmanaged[Cdecl]<int> IsIltWorld;

    /// <summary>GetFeature(key): the server's bit for a feature, -1 unknown, 0 off, 1 on (-1 off ILT). Any thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, int> GetFeature;

    /// <summary>GetRates(coinsPerHour, pyrealsPerHour): ILT session rates; 0 (nothing written) off ILT. Any thread.</summary>
    public delegate* unmanaged[Cdecl]<double*, double*, int> GetRates;

    /// <summary>GetForceIlt(): this character's "Treat this world as ILT" override (1 on). Any thread.</summary>
    public delegate* unmanaged[Cdecl]<int> GetForceIlt;

    /// <summary>SetForceIlt(on): sets that override. Any thread (applied and saved on RynthILT's next tick).</summary>
    public delegate* unmanaged[Cdecl]<int, void> SetForceIlt;

    /// <summary>
    /// GetTabs(tabs, capacity): every ILT tab in tab-bar order with its flags. Writes up to
    /// capacity, returns how many there are (tabs null: just the count). Render thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IltTabV1*, int, int> GetTabs;

    /// <summary>RenderTab(id): draws that tab's body into the current (child) window; 1 = drawn. Render thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, int> RenderTab;

    /// <summary>RenderSection(id): draws a RynthIltHub.Section* into the current window; 1 = drawn. Render thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, int> RenderSection;

    /// <summary>
    /// Command(text): one of the ILT "/ra" verbs, as typed after "/ra" ("hub bank", "hub attr run",
    /// "guardian handin", "bounty show", "rates reset"). RynthILT answers in chat itself.
    /// 1 = handled, 0 = not an ILT verb. Pump thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, int> Command;

    // ── Appended 2026-10-20 (check with Has before calling) ─────────────────

    /// <summary>
    /// AllowSummonOnEmpty(): 1 when a pet may be summoned from an EMPTY essence because the server
    /// refills it (ACECustom's Summon Essence Refill charm is on, the player opted in, and the server
    /// confirmed the feature). RynthAi's PetManager hook asks this; 0 off ILT and whenever unsure (an
    /// unknown state never spends pyreals). Cached by RynthILT's tick. Any thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<int> AllowSummonOnEmpty;

    /// <summary>
    /// GetSubTabs(parent, tabs, capacity): the sub-tabs RynthILT adds to one of RynthAi's tabs
    /// (parent RynthIltHub.TabParentPets: the Pets tab's Breeding). Each tab's Id is a section id
    /// drawn with RenderSection; Width / Height are 0 (the parent's size). Writes up to capacity,
    /// returns how many (tabs null: the count); 0 off ILT or for an unknown parent. Render thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, IltTabV1*, int, int> GetSubTabs;

    /// <summary>
    /// GetSetting(key): one of the server plugin's settings that another plugin shows (RynthIltHub.Setting*;
    /// RynthAi mirrors them into the engine's Settings window). The value, or -1 when the plugin has no
    /// such setting to offer now (unknown key, before login, off ILT). Any thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, int> GetSetting;

    /// <summary>SetSetting(key, value): changes that setting (saved by RynthILT); 1 = taken, 0 = not ours / not now. Any thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, int, int> SetSetting;

    /// <summary>
    /// GetText(id, buffer, capacity): a short text RynthAi shows (RynthIltHub.Text*), UTF-8, truncated to
    /// capacity. Returns its length; 0 for nothing (unknown id, off ILT). Any thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, byte*, int, int> GetText;
}
