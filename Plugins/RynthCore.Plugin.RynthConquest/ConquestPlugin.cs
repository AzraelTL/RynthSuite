// ConquestPlugin.cs - RynthConquest's own features, through the shared server plugin's hooks
// (Plugins/Shared/ServerPack/ServerPackPlugin.cs). Everything Conquest-specific that RynthOracle used to
// carry lives here since 2026-10-20 (Conquest/ConquestFeature.cs says what), so a player without
// RynthConquest gets none of it, the automatic /bank deposit included.
using System;
using System.IO;
using RynthCore.Plugin.RynthConquest;

namespace RynthCore.Plugin.ServerPack;

public sealed partial class ServerPackPlugin
{
    private readonly ConquestFeature _conquest = new();
    private ConquestPages? _conquestPages;
    private long _nextConquestCharacterMs;

    /// <summary>Where RynthOracle kept the Conquest settings before they moved here (read once).</summary>
    internal static string OracleSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthoracle.json");

    partial void ServerPages(System.Collections.Generic.List<ServerPage> pages)
    {
        _conquestPages = new ConquestPages(_conquest, () => Active, RunOnTick, Send,
            text => ChatLine($"[{RynthPluginInfo.Name}] {text}"), SaveConquestSettings);
        pages.AddRange(_conquestPages.Pages());
    }

    partial void ServerInitialize()
    {
        if (ConquestFeature.Migrate(Settings, OracleSettingsPath))
            LogTagged("Took over RynthOracle's Conquest settings (auto-deposit, hiding own replies) from rynthoracle.json.");
        lock (_conquest.Gate) _conquest.LoadSettings(Settings);
    }

    private void SaveConquestSettings()
    {
        lock (_conquest.Gate) _conquest.SaveSettings(Settings);
    }

    partial void ServerLoginComplete()
    {
        lock (_conquest.Gate) _conquest.Login(Environment.TickCount64);
        _nextConquestCharacterMs = 0;
        _conquestPages?.Reset();
    }

    partial void ServerLogout()
    {
        lock (_conquest.Gate) _conquest.Logout();
        _conquestPages?.Reset();
    }

    partial void ServerShutdown() => SaveConquestSettings();

    partial void ServerChatText(string text, int chatType, ref bool eat)
    {
        if (!Active) return;
        bool hide;
        lock (_conquest.Gate) hide = _conquest.OnChatLine(text, Environment.TickCount64);
        if (hide) eat = true;
    }

    partial void ServerChatBar(string text)
    {
        // The player's own /myquests: its reply shows (QuestFlagStore tells it from RynthConquest's).
        if (text.Equals("/myquests", StringComparison.OrdinalIgnoreCase)) _conquest.Flags.NoteTypedCommand();
    }

    partial void ServerTick(bool active)
    {
        if (!active) return;
        long now = Environment.TickCount64;
        string? augs, deposit;
        lock (_conquest.Gate)
        {
            _conquest.Flags.Tick(now);
            augs = _conquest.LoginAugsDue(now);
            deposit = _conquest.AutoDepositDue(DateTime.Now, now);
        }
        if (augs != null) Send(augs);
        if (deposit != null && Send(deposit)) ChatLine($"[{RynthPluginInfo.Name}] Auto-deposited to the bank.");

        if (now < _nextConquestCharacterMs) return;
        _nextConquestCharacterMs = now + ConquestFeature.CharacterReadMs;
        ReadConquestCharacter();
    }

    /// <summary>Level, XP, enlightenment and Archmage's Endurance (the Void DoT rule), then the rule for RynthOracle.</summary>
    private void ReadConquestCharacter()
    {
        uint player = Host.HasGetPlayerId ? Host.GetPlayerId() : 0;
        if (player == 0) return;
        int level = Host.TryGetObjectIntProperty(player, 25, out int l) ? l : 0;               // PropertyInt Level
        int enl = Host.TryGetObjectIntProperty(player, 390, out int e) ? e : 0;                // Enlightenment
        int archmage = Host.TryGetObjectIntProperty(player, 238, out int a) ? a : 0;           // AugmentationIncreasedSpellDuration
        long xp = Host.HasGetObjectQuadProperty && Host.TryGetObjectQuadProperty(player, 1, out long x) ? x : 0; // TotalExperience
        double scale;
        lock (_conquest.Gate)
        {
            _conquest.Level = level;
            _conquest.Enlightenment = enl;
            _conquest.ArchmagesEndurance = archmage;
            _conquest.TotalXp = xp;
            _conquest.CharacterKnown = level > 0;
            scale = _conquest.VoidScale;
        }
        SetProfileValue(ConquestFeature.VoidScaleKey, scale);
    }

    partial void ServerInfoLines(Action<string> line)
    {
        lock (_conquest.Gate)
        {
            line($"Auto-deposit {(_conquest.AutoDeposit ? "on" : "off")}; own replies {(_conquest.HideOwnReplies ? "hidden" : "shown")}"
                 + $"; Void DoT duration x{_conquest.VoidScale:0.##} ({_conquest.Data.DurationAugs} Duration augs).");
        }
    }
}
