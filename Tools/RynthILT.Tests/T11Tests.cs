using System;
using System.IO;
using System.Linq;
using RynthCore.Loot.T11;
using RynthCore.Plugin.RynthILT;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// T11 (ACECustom tier 11+ gear) moved from RynthAi on 2026-10-20 (Hub/IltT11.cs): RynthILT reads the
// "/aug" reply's wield counters, applies its item-aug override and reports them to RynthAi's loot rules
// (RynthAi.Hub SetPlayerCounter); "/ilt t11" (and RynthAi's "/ra t11", passed on) shows and sets them.
internal static class T11Tests
{
    private const uint Player = 0x50000E01;

    public static void Register(Runner r)
    {
        r.Add("t11 (RynthILT): the /aug reply's counters go to RynthAi, with the item-aug override applied", ReportsCounters);
        r.Add("t11 (RynthILT): /ilt t11 and /ra t11 are RynthILT's verbs; augs sets the override", Commands);
        r.Add("t11 (RynthILT): off ILT nothing is read or reported", DormantOffIlt);
        r.Add("migration: the RynthAi profile's T11 item-aug override comes over once", ImportsOverride);
    }

    private static IltHubController NewHub(string world, string folder)
    {
        FakeHost.Reset();
        FakeAiHub.Reset();
        FakeHost.WorldName = world;
        var host = FakeHost.Create(Player);
        var ai = new RynthAiClient(host);
        ai.UseTableForTests(FakeAiHub.Table);
        string dir = Path.Combine(Program.TempRoot, "t11-" + folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var hub = new IltHubController(host, dir, new IltSettings(), ai);
        hub.OnLoginComplete("Augger", forceFromMigration: false);
        return hub;
    }

    private static void ReportsCounters()
    {
        var hub = NewHub("InfiniteLeaftide", "report");
        hub.T11.Tick(Environment.TickCount64);
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], -1L, "unknown before /aug (reported as -1)");
        Check.Eq(hub.T11.LastReported, T11Catalog.CounterNames.Length, "every counter is reported");

        hub.OnChat("Advanced Augmentation Levels:");
        hub.OnChat("Item:2,400");
        hub.OnChat("Triune Weave: 5 (adds 5 to your aug counts)");
        hub.T11.Tick(Environment.TickCount64);
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], 2400L, "Item: 2,400 reported");
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.TriuneWeave], 5L, "Triune Weave: 5 reported");

        hub.State.T11.ItemAugsOverride = 2500;
        hub.T11.Tick(Environment.TickCount64);
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], 2500L, "the override replaces /aug's item count");
        hub.State.T11.ItemAugsOverride = -1;
        hub.T11.Tick(Environment.TickCount64);
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], 2400L, "auto again: /aug's count");

        hub.OnLogout();
        hub.T11.Tick(Environment.TickCount64);
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], -1L, "after logout: unknown again");
    }

    private static void Commands()
    {
        Check.True(RynthILTPlugin.IsIltVerb(new[] { "t11" }), "/ra t11 is RynthILT's (RynthAi passes it on)");
        Check.True(RynthILTPlugin.IsIltVerb(new[] { "t11", "augs", "5" }), "with its arguments");
        Check.True(RynthILTPlugin.IsIltVerb(new[] { "ub", "qblist" }) && RynthILTPlugin.IsIltVerb(new[] { "ub", "augs" }),
                   "ub-IT's /ub qblist and /ub augs (ACECustom commands RynthAi passes on) are RynthILT's");
        Check.False(RynthILTPlugin.IsIltVerb(new[] { "ub", "wear" }) || RynthILTPlugin.IsIltVerb(new[] { "ub" }), "no other /ub verb");

        var hub = NewHub("InfiniteLeaftide", "cmd");
        FakeHost.Chat.Clear();
        hub.T11.HandleCommand(new[] { "augs", "3,000" });
        Check.Eq(hub.State.T11.ItemAugsOverride, 3000, "augs 3,000 fixes the override");
        Check.Eq(FakeAiHub.Counters[(uint)T11Counter.ItemAugmentations], 3000L, "and reports it at once");
        hub.T11.HandleCommand(new[] { "augs", "auto" });
        Check.Eq(hub.State.T11.ItemAugsOverride, -1, "augs auto clears it");
        FakeHost.Chat.Clear();
        hub.T11.HandleCommand(Array.Empty<string>());
        Check.True(FakeHost.Chat.Any(l => l.Contains("=== T11 loot")), "no argument: the status");
        Check.True(FakeHost.Chat.Any(l => l.Contains("Item Augmentations") || l.Contains("unknown")), "lists the counters");
        FakeHost.Chat.Clear();
        hub.T11.HandleCommand(new[] { "augs", "-5" });
        Check.True(FakeHost.Chat.Any(l => l.Contains("Usage")), "a bad count: usage");
    }

    private static void DormantOffIlt()
    {
        var off = NewHub("Aeshnidae", "off");
        off.OnChat("Advanced Augmentation Levels:");
        off.OnChat("Item:2,400");
        for (int i = 0; i < 3; i++) off.Tick();
        Check.Eq(FakeAiHub.Counters.Count, 0, "off ILT: no counter is reported to RynthAi");
        Check.True(off.T11.Player.Get(T11Counter.ItemAugmentations) == null, "nor read");
    }

    private static void ImportsOverride()
    {
        string legacy = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Augger");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Default.json"), "{ \"T11ItemAugsOverride\": 2750 }");
        var state = new IltHubState();
        Check.Eq(state.T11.ItemAugsOverride, -1, "automatic by default");
        IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Augger");
        Check.Eq(state.T11.ItemAugsOverride, 2750, "the RynthAi profile's override came over");
    }
}
