using System;
using System.IO;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// The Vaulted Dungeons keeper's master switch moved from RynthAi's profile to RynthILT on 2026-10-20
// (IltHubState.VaultedDungeonsEnabled, the ILT settings view). RynthAi mirrors it into the engine's
// Settings > Navigation row through RynthILT.Hub GetSetting / SetSetting, which offer it on ILT only.
internal static class VaultedSwitchTests
{
    public static void Register(Runner r)
    {
        r.Add("vaulted switch: RynthILT's own (off by default), what the keeper reads, offered to the engine row on ILT only", OwnSwitch);
        r.Add("migration: the RynthAi profile's Vaulted Dungeons switch comes over once", ImportsSwitch);
    }

    private static IltHubController NewHub(string world, string folder)
    {
        FakeHost.Reset();
        FakeHost.WorldName = world;
        var host = FakeHost.Create(0x50000C01);
        string dir = Path.Combine(Program.TempRoot, "vswitch-" + folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var hub = new IltHubController(host, dir, new IltSettings(), new RynthAiClient(host));
        hub.OnLoginComplete("Vaulter", forceFromMigration: false);
        return hub;
    }

    private static void OwnSwitch()
    {
        var hub = NewHub("InfiniteLeaftide", "own");
        Check.False(hub.State.VaultedDungeonsEnabled, "off by default");
        Check.False(hub.VaultedDungeons.MasterEnabled(), "the keeper reads RynthILT's own switch (no RynthAi needed)");
        Check.False(hub.SectionAvailable(IltSection.VaultedDungeons), "no tab while off");
        Check.Eq(hub.GetSetting(RynthIltHub.SettingVaultedDungeons), 0, "offered to the engine row: off");
        Check.True(hub.SetSetting(RynthIltHub.SettingVaultedDungeons, 1), "the engine row (through RynthAi) sets it");
        Check.True(hub.State.VaultedDungeonsEnabled && hub.VaultedDungeons.MasterEnabled(), "on");
        Check.True(hub.SectionAvailable(IltSection.VaultedDungeons), "the tab shows on ILT");
        Check.Eq(hub.GetSetting("no-such-setting"), -1, "an unknown key isn't offered");
        Check.False(hub.SetSetting("no-such-setting", 1), "nor taken");

        var off = NewHub("Aeshnidae", "off");
        Check.Eq(off.GetSetting(RynthIltHub.SettingVaultedDungeons), -1, "off ILT: not offered (the engine row hides)");
    }

    private static void ImportsSwitch()
    {
        string legacy = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Vaulter");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Default.json"), "{ \"EnableVaultedDungeons\": true }");
        var state = new IltHubState();
        var lines = IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Vaulter");
        Check.True(state.VaultedDungeonsEnabled, "on in the RynthAi profile: on here");
        Check.True(lines.Exists(l => l.Contains("Vaulted Dungeons master switch on")), "logged");
        state.VaultedDungeonsEnabled = false;
        IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Vaulter");
        Check.False(state.VaultedDungeonsEnabled, "copied once: RynthILT's later change stays");
    }
}
