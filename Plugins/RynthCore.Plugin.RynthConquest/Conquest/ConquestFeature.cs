// ConquestFeature.cs - What RynthConquest does on Conquest, without the host (ConquestPlugin.cs wires it
// to the engine, ConquestPages.cs draws it). Moved out of RynthOracle on 2026-10-20 so that only players
// who installed RynthConquest get Conquest's features, and the automatic /bank deposit with them:
//   * reads the replies of Conquest's own commands (ConquestData: /b, /augs, /enl augs, /bonus,
//     /fship list, /top) and the quest flags (/myquests, for the custom quest worklist), and hides
//     the replies to its own requests when HideOwnReplies is on;
//   * reads the advanced augmentations once after login (the Void DoT rule needs the Duration augs);
//   * deposits everything at each ten-minute mark when AutoDeposit is on (off unless the player
//     turned it on, in RynthOracle before or here);
//   * works out the Void DoT duration rule for this character, which RynthOracle's Void timers read
//     from the server profile ("voidDotDurationScale") instead of knowing about Conquest.
// Settings: rynthconquest.json (ServerPackSettings), taken over once from rynthoracle.json.
// Threads: everything here runs under Gate. Chat and tick come from the pump, the pages draw on the
// render thread holding it too; nothing in here calls the host.
// Pure: RynthServerPack.Tests covers it.
using System;
using RynthCore.Plugin.RynthOracle.Data;
using RynthCore.Plugin.ServerPack;

namespace RynthCore.Plugin.RynthConquest;

internal sealed class ConquestFeature
{
    public const string SettingAutoDeposit = "autoDeposit";
    public const string SettingHideOwnReplies = "hideOwnReplies";
    /// <summary>Set once rynthoracle.json's Conquest settings were taken over (they're never read again).</summary>
    public const string MigrationMarker = "migratedFromRynthOracle";
    /// <summary>The profile value RynthOracle's Void timers apply ("RynthServer.Profile" values).</summary>
    public const string VoidScaleKey = "voidDotDurationScale";
    /// <summary>After login, before the hidden /augs (RynthOracle's old timing: its quest read plus 4 s).</summary>
    public const long AugsReadDelayMs = 10_000;
    /// <summary>The character's level, XP and augmentation are read this often.</summary>
    public const long CharacterReadMs = 1000;

    /// <summary>RynthConquest's pages (ConquestPages draws them): id, label, tooltip.</summary>
    public static readonly (string Id, string Label, string Tooltip)[] PageList =
    {
        ("main", "Conquest", "Conquest: bank and auto-deposit, augmentations, levels past 275, fellowships and custom quests (RynthConquest)."),
        ("boards", "Leaderboards", "Conquest's /top boards (RynthConquest)."),
    };

    /// <summary>Held for everything below (pump and render thread).</summary>
    public readonly object Gate = new();
    public readonly ConquestData Data = new();
    public readonly QuestFlagStore Flags = new();

    /// <summary>"/bank deposit" at every ten-minute mark (upstream's auto-deposit). Off by default.</summary>
    public bool AutoDeposit;
    /// <summary>Hide the replies to RynthConquest's own requests (a command typed by hand always shows).</summary>
    public bool HideOwnReplies = true;

    // The character (ConquestPlugin reads them every CharacterReadMs).
    public bool CharacterKnown;
    public int Level, Enlightenment, ArchmagesEndurance;
    public long TotalXp;

    private long _loginMs;
    private bool _augsAsked;
    private long _lastDepositBlock = -1;

    /// <summary>
    /// Takes over the settings RynthOracle had for Conquest (rynthoracle.json at <paramref name="oraclePath"/>),
    /// once per settings file; a value that isn't there keeps this plugin's default (auto-deposit off,
    /// hiding on, as they were in RynthOracle). True when it ran now.
    /// </summary>
    public static bool Migrate(ServerPackSettings settings, string oraclePath) => settings.MigrateOnce(MigrationMarker, s =>
    {
        if (ServerPackSettings.ReadBoolFrom(oraclePath, "conquestAutoDeposit") is bool deposit) s.SetBool(SettingAutoDeposit, deposit);
        if (ServerPackSettings.ReadBoolFrom(oraclePath, "hideOwnQuestReplies") is bool hide) s.SetBool(SettingHideOwnReplies, hide);
    });

    public void LoadSettings(ServerPackSettings s)
    {
        AutoDeposit = s.GetBool(SettingAutoDeposit, false);
        HideOwnReplies = s.GetBool(SettingHideOwnReplies, true);
    }

    public void SaveSettings(ServerPackSettings s)
    {
        bool changed = s.SetBool(SettingAutoDeposit, AutoDeposit);
        changed |= s.SetBool(SettingHideOwnReplies, HideOwnReplies);
        if (changed) s.Save();
    }

    /// <summary>A character logged in (or the plugin loaded in game): everything is read fresh.</summary>
    public void Login(long nowMs)
    {
        _loginMs = nowMs;
        _augsAsked = false;
        Data.Clear();
        Flags.Clear();
        CharacterKnown = false;
        Level = Enlightenment = ArchmagesEndurance = 0;
        TotalXp = 0;
    }

    public void Logout()
    {
        Data.Clear();
        Flags.Clear();
        CharacterKnown = false;
    }

    /// <summary>
    /// An incoming chat line on Conquest. True when it should be hidden: a reply to one of
    /// RynthConquest's own requests, with HideOwnReplies on.
    /// </summary>
    public bool OnChatLine(string text, long nowMs)
    {
        string line = text.TrimEnd('\r', '\n');
        MyQuestsLine kind = Flags.OnChatLine(line, nowMs);
        if (kind != MyQuestsLine.None)
            return Flags.AwaitingOurs && HideOwnReplies && kind is MyQuestsLine.Quest or MyQuestsLine.Empty;
        return Data.OnChatLine(line, nowMs, out bool ours) && ours && HideOwnReplies;
    }

    /// <summary>The once-after-login /augs: the command to send now, or null.</summary>
    public string? LoginAugsDue(long nowMs)
    {
        if (_augsAsked || nowMs - _loginMs <= AugsReadDelayMs) return null;
        _augsAsked = true;
        return Data.RequestAugs(nowMs);
    }

    /// <summary>The automatic deposit: "/bank deposit" when one is due now, else null.</summary>
    public string? AutoDepositDue(DateTime local, long nowMs)
        => AutoDeposit && DepositDue(local, ref _lastDepositBlock) ? Data.AutoDeposit(nowMs) : null;

    /// <summary>
    /// True once in each ten-minute block, at its first minute (:00, :10, :20 ... local time), as
    /// upstream does; <paramref name="lastBlock"/> remembers the block already done.
    /// </summary>
    public static bool DepositDue(DateTime local, ref long lastBlock)
    {
        long block = local.Ticks / TimeSpan.TicksPerMinute / 10;
        if (local.Minute % 10 != 0 || block == lastBlock) return false;
        lastBlock = block;
        return true;
    }

    /// <summary>This character's Void DoT duration multiplier on Conquest (Archmage's Endurance and the Duration augs).</summary>
    public double VoidScale => Data.VoidDurationMultiplier(ArchmagesEndurance);
}
