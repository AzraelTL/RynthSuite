// AelrynthPlugin.cs - RynthAelrynth's own features, through the shared server plugin's hooks
// (Plugins/Shared/ServerPack/ServerPackPlugin.cs). Aelrynth-specific code that RynthOracle and
// RynthInventory used to carry lives here since 2026-10-20 (Aelrynth/AelrynthFeature.cs says what),
// so a player without RynthAelrynth gets none of it, and no other server is asked for a vault.
using System;
using System.IO;
using RynthCore.Plugin.RynthAelrynth;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.ServerPack;

public sealed partial class ServerPackPlugin
{
    private readonly AelrynthFeature _aelrynth = new();
    private AelrynthPages? _aelrynthPages;
    // The last vault read, until RynthInventory took it (it may load after the read).
    private StorageFeedRecord? _vaultToHandOff;
    private long _nextVaultHandOffMs;
    private const long VaultHandOffRetryMs = 30_000;

    /// <summary>Where RynthOracle kept the reply-hiding choice before it moved here (read once).</summary>
    internal static string OracleSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "rynthoracle.json");

    partial void ServerPages(System.Collections.Generic.List<ServerPage> pages)
    {
        _aelrynthPages = new AelrynthPages(_aelrynth, () => Active, RunOnTick, Send,
            text => ChatLine($"[{RynthPluginInfo.Name}] {text}"), SaveAelrynthSettings);
        pages.AddRange(_aelrynthPages.Pages());
    }

    partial void ServerCommandVerbs(System.Collections.Generic.List<string> verbs) => verbs.Add("vault");

    partial void ServerInitialize()
    {
        _aelrynth.SendCommand = Send;
        if (AelrynthFeature.Migrate(Settings, OracleSettingsPath))
            LogTagged("Took over RynthOracle's reply-hiding choice from rynthoracle.json.");
        lock (_aelrynth.Gate) _aelrynth.LoadSettings(Settings);
    }

    private void SaveAelrynthSettings()
    {
        lock (_aelrynth.Gate) _aelrynth.SaveSettings(Settings);
    }

    partial void ServerLoginComplete()
    {
        lock (_aelrynth.Gate) _aelrynth.Reset();
        _vaultToHandOff = null;
        _aelrynthPages?.Reset();
    }

    partial void ServerLogout()
    {
        lock (_aelrynth.Gate) _aelrynth.Reset();
        _vaultToHandOff = null;
        _aelrynthPages?.Reset();
    }

    partial void ServerShutdown() => SaveAelrynthSettings();

    partial void ServerChatText(string text, int chatType, ref bool eat)
    {
        if (!Active) return;
        bool hide;
        lock (_aelrynth.Gate) hide = _aelrynth.OnChatLine(text, chatType, Environment.TickCount64);
        if (hide) eat = true;
    }

    partial void ServerTick(bool active)
    {
        if (!active || !Host.HasInvokeChatParser) return;
        long now = Environment.TickCount64;
        uint player = Host.HasGetPlayerId ? Host.GetPlayerId() : 0;
        if (player == 0) return;   // the vault belongs to the account; wait for the character
        string account = Host.TryGetAccountName(out string a) && a.Trim().Length > 0 ? a.Trim() : "unknown";
        StorageFeedRecord? read;
        lock (_aelrynth.Gate) read = _aelrynth.VaultTick(now, account);
        if (read != null) { _vaultToHandOff = read; _nextVaultHandOffMs = 0; }
        if (_vaultToHandOff != null && now >= _nextVaultHandOffMs) HandOffVault(now);
    }

    /// <summary>
    /// The vault to RynthInventory (StorageFeed), which files it with the character's storage. Kept and
    /// offered again now and then while RynthInventory isn't there to take it.
    /// </summary>
    private void HandOffVault(long now)
    {
        StorageFeedRecord? vault = _vaultToHandOff;
        if (vault == null) return;
        bool taken = Host.HasSendPluginCommand
                     && Host.SendPluginCommand(StorageFeed.TargetPlugin, StorageFeed.Action, StorageFeed.ToJson(vault));
        string note = taken
            ? $"{vault.Items.Count} item(s) handed to RynthInventory at {DateTime.Now:HH:mm}"
            : "RynthInventory isn't loaded (or is too old), so the vault isn't in Global Inventory";
        lock (_aelrynth.Gate) _aelrynth.VaultHandOff = note;
        if (taken) _vaultToHandOff = null;
        else _nextVaultHandOffMs = now + VaultHandOffRetryMs;
    }

    partial void ServerChatCommand(string[] parts, ref bool handled)
    {
        if (parts.Length < 2 || !parts[1].Equals("vault", StringComparison.OrdinalIgnoreCase)) return;
        handled = true;
        string tag = $"[{RynthPluginInfo.Name}] ";
        if (!Active) { ChatLine(tag + "The account vault is Aelrynth's; RynthCore doesn't see this world as Aelrynth."); return; }
        bool asked;
        string state;
        lock (_aelrynth.Gate)
        {
            asked = _aelrynth.VaultAskNow(Environment.TickCount64);
            state = _aelrynth.Vault.Describe();
        }
        ChatLine(tag + (asked ? "Reading the account vault (/mule-data)..." : "The vault is already being read: " + state));
    }

    partial void ServerInfoLines(Action<string> line)
    {
        lock (_aelrynth.Gate)
        {
            string handOff = _aelrynth.VaultHandOff.Length > 0 ? "; " + _aelrynth.VaultHandOff : "";
            line("Account vault: " + _aelrynth.Vault.Describe() + handOff + " (/rserver vault reads it now).");
            line("Own replies (/top, /trial) " + (_aelrynth.HideOwnReplies ? "hidden" : "shown") + ".");
        }
    }
}
