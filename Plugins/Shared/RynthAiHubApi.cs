// ============================================================================
//  RynthSuite - Plugins/Shared/RynthAiHubApi.cs
//  The "RynthAi.Hub" interface: what RynthILT (the Infinite Leaftide features)
//  borrows from RynthAi. The Mini Remote that hosts the ILT tabs, the /myquests
//  tracker (the Temple attribute tracker reads its flags), spell names (the dispel
//  keeper), VTank loot-profile matching (equipment suits) and the VTank .usd
//  importer (which edits RynthAi's own rules; drawn in RynthILT's Gear tab), and the
//  Mini Remote's session stats (the Character tab's rates count the same session).
//  Compiled into both plugins (linked source), so the layout can't drift.
//
//  Rules: as RynthILT.Hub (Shared/RynthIltHubApi.cs): append-only, UTF-8 strings,
//  each function says its thread, no exception crosses, the table lives as long as
//  the RynthAi copy (never freed; Version 0 after Shutdown), callers re-query.
//  RynthILT works without RynthAi: every feature that needs it says so.
// ============================================================================

using System.Runtime.InteropServices;

namespace RynthCore.Plugin.Shared;

/// <summary>Constants of the "RynthAi.Hub" interface.</summary>
public static class RynthAiHub
{
    /// <summary>The providing plugin's display name.</summary>
    public const string PluginName = "RynthAi";

    /// <summary>RenderSection: the VTank .usd settings importer (RynthAi's monster / weapon / consumable rules).</summary>
    public const string SectionVTankImport = "vtank-import";
}

/// <summary>One /myquests flag as RynthAi's quest tracker has it. Blittable.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct QuestFlagV1
{
    public int Solves;
    /// <summary>-1 unlimited (as the server prints it).</summary>
    public int MaxSolves;
    /// <summary>Last completion, UTC ticks (DateTime.Ticks); 0 = never / unknown.</summary>
    public long CompletedOnUtcTicks;
    /// <summary>Repeat time in seconds (0 = none).</summary>
    public long RepeatSeconds;
}

/// <summary>The quest tracker's state. Blittable.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct QuestStatusV1
{
    /// <summary>1 when the server said /myquests is disabled.</summary>
    public int Disabled;
    /// <summary>Flags cached.</summary>
    public int Count;
    /// <summary>Bumped each time a refresh ends (0 = none yet this session).</summary>
    public int Generation;
    /// <summary>1 while a refresh is running.</summary>
    public int Refreshing;
}

/// <summary>The Mini Remote's session stats (RynthTracker's SessionStats code). Blittable.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SessionStatsV1
{
    /// <summary>Session start, UTC ticks (DateTime.Ticks); 0 = no session yet.</summary>
    public long StartUtcTicks;
    public double SessionSeconds;
    public long Xp;
    public double XpPerHour;
    public long Lum;
    public double LumPerHour;
    public long LumLast5Min;
    /// <summary>1 when the pass-up figures below are tracked.</summary>
    public int PassUpTracked;
    public long LumPassUp;
    public double PassUpPerHour;
    public long Kills;
    public double KillsPerHour;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct RynthAiHubApiV1
{
    public const string InterfaceName = "RynthAi.Hub";
    public const uint InterfaceVersion = 1;

    public uint Version;      // InterfaceVersion of the table RynthAi filled; 0 once it has shut down
    public uint StructSize;   // sizeof(RynthAiHubApiV1) as RynthAi built it

    /// <summary>
    /// MiniRemoteTab(tabId, mode, reply, replyCapacity): puts the Mini Remote on a tab (tabId "" =
    /// its Remote tab; mode "show" / "hide" / "toggle", or "select" to switch tabs without showing
    /// it). Writes the chat-ready reply (UTF-8, truncated to capacity). 1 = done, 0 = no Mini Remote
    /// yet (before login). Any thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, byte*, byte*, int, int> MiniRemoteTab;

    /// <summary>MiniRemoteVisible(): 1 while the Mini Remote is on screen. Any thread.</summary>
    public delegate* unmanaged[Cdecl]<int> MiniRemoteVisible;

    /// <summary>QuestFlag(key, flag): 1 and the flag filled when /myquests has it (case-insensitive key). Pump thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, QuestFlagV1*, int> QuestFlag;

    /// <summary>QuestStatus(status): fills the tracker state; 0 when there is no tracker (before login). Pump thread.</summary>
    public delegate* unmanaged[Cdecl]<QuestStatusV1*, int> QuestStatus;

    /// <summary>
    /// QuestRefresh(reply, replyCapacity): a quiet /myquests refresh, with the Quests tab's one-a-minute
    /// cooldown. Writes what happened ("Refreshing quests...", "Wait 40s ..."). 1 = sent. Pump thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, int, int> QuestRefresh;

    /// <summary>SpellName(spellId, buffer, capacity): the spell's name from RynthAi's spell table; its length, 0 unknown. Any thread.</summary>
    public delegate* unmanaged[Cdecl]<uint, byte*, int, int> SpellName;

    /// <summary>
    /// LootKeeps(utlPath, ids, count, keep, error, errorCapacity): evaluates the VTank loot profile at
    /// utlPath (.utl) against each object id; keep[i] = 1 when that item's first matching rule is Keep.
    /// 1 = evaluated; 0 = the profile couldn't be read (error says why). Pump thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, uint*, int, byte*, byte*, int, int> LootKeeps;

    /// <summary>RenderSection(id): draws a RynthAiHub.Section* into the current window; 1 = drawn. Render thread.</summary>
    public delegate* unmanaged[Cdecl]<byte*, int> RenderSection;

    /// <summary>SessionStats(stats): the Mini Remote's session (XP, luminance, kills); 0 before login. Any thread.</summary>
    public delegate* unmanaged[Cdecl]<SessionStatsV1*, int> SessionStats;

    /// <summary>SessionReset(): starts the Mini Remote's session over (as its Reset XP button). Any thread.</summary>
    public delegate* unmanaged[Cdecl]<void> SessionReset;
}
