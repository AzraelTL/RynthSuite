// ServerData.cs - What is known about Aelrynth (RynthAelrynth). Identity (id, hosts, world names) is in the csproj.
// Aelrynth isn't in the public server list; this comes from RynthCore's own Aelrynth support
// (Compatibility/AelrynthWire.cs, Net/AelrynthVaultWire.cs) and RynthOracle's quest pack.
namespace RynthCore.Plugin.ServerPack;

internal static class ServerData
{
    public static ServerPackInfo Create() =>
        new(RynthPluginInfo.ServerId, RynthPluginInfo.ServerName, RynthPluginInfo.ServerHosts, RynthPluginInfo.ServerWorldNames)
        {
            Port = 9020,
            Emulator = "ACE",
            Ruleset = "PvE",
            Summary = "Aelrynth (aelrynth.com), an ACE server with its own mods.",
            Facts = new[]
            {
                "Bank mod: Radiance and Luminance banking (RynthCore reads its totals).",
                "Skill Mastery mod (RynthCore shows mastery progress).",
                "Account vault for mules (RynthCore reads its contents; RynthAelrynth puts it in Global Inventory, /rserver vault).",
                "RynthOracle has an Aelrynth quest pack (quests.aelrynth.csv).",
                "RynthAelrynth's Leaderboards page (Server section, or /rserver): the /top boards and /trial dungeon records.",
                "The world was named Aeshnidae before; both names count.",
            },
            Website = "https://aelrynth.com",
            Source = "RynthCore's Aelrynth support and RynthOracle, 2026-10-13",
        };
}
