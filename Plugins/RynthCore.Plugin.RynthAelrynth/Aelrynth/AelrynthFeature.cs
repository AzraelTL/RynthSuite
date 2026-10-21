// AelrynthFeature.cs - What RynthAelrynth does on Aelrynth, without the host (AelrynthPlugin.cs wires it
// to the engine, AelrynthPages.cs draws it). Moved out of RynthOracle on 2026-10-20 so that only players
// who installed RynthAelrynth carry Aelrynth's features:
//   * reads the replies of Aelrynth's own commands (AelrynthBoards: /top, /top me, /trial) and hides
//     the replies to its own requests when HideOwnReplies is on;
//   * reads the account vault (VaultFeed: /mule-data, moved here from RynthInventory, which asked every
//     server for it) a few seconds after login and every ten minutes, and hands it to RynthInventory
//     through the generic storage feed (Plugins/Shared/StorageFeed.cs), so it shows in Global Inventory.
// Settings: rynthaelrynth.json (ServerPackSettings); "hide own replies" is taken over once from
// rynthoracle.json (it was RynthOracle's hideOwnQuestReplies).
// Threads: everything here runs under Gate (chat and tick from the pump, the pages on the render thread).
// Pure: RynthServerPack.Tests covers it.
using System;
using RynthCore.Plugin.ServerPack;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthAelrynth;

internal sealed class AelrynthFeature
{
    public const string SettingHideOwnReplies = "hideOwnReplies";
    /// <summary>Set once rynthoracle.json's setting was taken over (it's never read again).</summary>
    public const string MigrationMarker = "migratedFromRynthOracle";

    /// <summary>RynthAelrynth's pages (AelrynthPages draws them): id, label, tooltip.</summary>
    public static readonly (string Id, string Label, string Tooltip)[] PageList =
    {
        ("boards", "Leaderboards", "Aelrynth's /top leaderboards and /trial dungeon records (RynthAelrynth)."),
    };

    /// <summary>Held for everything below (pump and render thread).</summary>
    public readonly object Gate = new();
    public readonly AelrynthBoards Boards = new();

    /// <summary>Hide the replies to RynthAelrynth's own requests (a command typed by hand always shows).</summary>
    public bool HideOwnReplies = true;

    /// <summary>Sends a chat command as if typed (the vault's /mule-data); the plugin sets it (AelrynthPlugin).</summary>
    public Func<string, bool> SendCommand = _ => false;

    /// <summary>The account vault reader.</summary>
    public readonly VaultFeed Vault;
    private long _nextVaultAskMs;

    /// <summary>What happened to the last vault read on its way to RynthInventory ("" before one).</summary>
    public string VaultHandOff = string.Empty;

    public AelrynthFeature() => Vault = new VaultFeed(cmd => SendCommand(cmd));

    /// <summary>Takes over RynthOracle's reply-hiding choice once (default: hidden, as it was there).</summary>
    public static bool Migrate(ServerPackSettings settings, string oraclePath) => settings.MigrateOnce(MigrationMarker, s =>
    {
        if (ServerPackSettings.ReadBoolFrom(oraclePath, "hideOwnQuestReplies") is bool hide) s.SetBool(SettingHideOwnReplies, hide);
    });

    public void LoadSettings(ServerPackSettings s) => HideOwnReplies = s.GetBool(SettingHideOwnReplies, true);

    public void SaveSettings(ServerPackSettings s)
    {
        if (s.SetBool(SettingHideOwnReplies, HideOwnReplies)) s.Save();
    }

    /// <summary>Login and logout: the boards and the vault are read fresh.</summary>
    public void Reset()
    {
        Boards.Clear();
        Vault.Reset();
        _nextVaultAskMs = 0;
        VaultHandOff = string.Empty;
    }

    /// <summary>
    /// An incoming chat line on Aelrynth: true when it should be hidden. The vault's data lines (and
    /// the unknown-command reply to our own probe) always are; a leaderboard reply to our own request
    /// is when HideOwnReplies is on.
    /// </summary>
    public bool OnChatLine(string text, int chatType, long nowMs)
    {
        if (Vault.ShouldEat(text, chatType)) return true;
        return Boards.OnChatLine(text.TrimEnd('\r', '\n'), nowMs, out bool ours) && ours && HideOwnReplies;
    }

    /// <summary>
    /// The vault's timing (plugin tick, on Aelrynth): the first read <see cref="VaultFeed.FirstAskMs"/>
    /// after login, then every <see cref="VaultFeed.RefreshMs"/> unless the server has no vault.
    /// Returns the record to hand to RynthInventory when a read just finished, else null.
    /// </summary>
    public StorageFeedRecord? VaultTick(long nowMs, string account)
    {
        if (_nextVaultAskMs == 0) _nextVaultAskMs = nowMs + VaultFeed.FirstAskMs;
        else if (nowMs >= _nextVaultAskMs && Vault.Status != VaultFeed.State.Unsupported)
        {
            _nextVaultAskMs = nowMs + VaultFeed.RefreshMs;
            Vault.Ask(nowMs);
        }
        Vault.Tick(nowMs, account);
        return Vault.TakeChanged() ? Vault.Record : null;
    }

    /// <summary>"/rserver vault": a read now (even on a server that said it has none); the next timed one in ten minutes.</summary>
    public bool VaultAskNow(long nowMs)
    {
        _nextVaultAskMs = nowMs + VaultFeed.RefreshMs;
        return Vault.Ask(nowMs, force: true);
    }
}
