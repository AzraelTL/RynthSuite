using System;
using System.IO;
using System.Linq;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// The Empyrean Handler's lethal ring (Hub/IltRingEvade.cs), RynthAi's CombatManager's until 2026-10-20
// (Silentkelpie's PR 12). The line "The handler briefly channels infused Empyrean hatred to release a
// lethal ring." on an ILT world, with a Handler near, makes RynthILT ask RynthAi to back away from it
// (RynthAi.Hub BackAwayFrom, 12 yards, a 750 ms jump). The setting (on by default) is RynthILT's now,
// copied once from the RynthAi profile.
internal static class RingEvadeTests
{
    private const uint Player = 0x50000B01;
    private const uint Handler = 0x80000B02;
    private const uint Lb = 0xA9B40000;
    private const string RingLine = "The handler briefly channels infused Empyrean hatred to release a lethal ring.";

    public static void Register(Runner r)
    {
        r.Add("ring evade (RynthILT): the Handler's line, and not a player's copy of it", ChannelLine);
        r.Add("ring evade (RynthILT): on ILT with a Handler near, RynthAi is asked to back away (once per cooldown)", AsksRynthAi);
        r.Add("ring evade (RynthILT): not with the setting off, no Handler near, off ILT, or without RynthAi", NotThese);
        r.Add("migration: the RynthAi profile's Handler ring evade comes over once (on by default)", ImportsSetting);
    }

    private static IltHubController NewHub(string world, string folder, bool handler = true, bool withAi = true, string handlerName = "Empyrean Handler")
    {
        FakeHost.Reset();
        FakeAiHub.Reset();
        FakeHost.WorldName = world;
        FakeHost.ContentsCalls = true;
        FakeHost.LiveObjectsCalls = true;
        FakeHost.Names[Player] = "Evader";
        FakeHost.Positions[Player] = (Lb | 0x1C, 96f, 96f, 0f);
        if (handler)
        {
            FakeHost.Names[Handler] = handlerName;
            FakeHost.ItemTypes[Handler] = 0x10;
            FakeHost.Positions[Handler] = (Lb | 0x1C, 96f, 101f, 0f);
        }
        var host = FakeHost.Create(Player);
        var ai = new RynthAiClient(host);
        if (withAi) ai.UseTableForTests(FakeAiHub.Table);
        string dir = Path.Combine(Program.TempRoot, "ring-" + folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var hub = new IltHubController(host, dir, new IltSettings(), ai);
        hub.OnLoginComplete("Evader", forceFromMigration: false);
        return hub;
    }

    private static void ChannelLine()
    {
        Check.True(IltRingEvade.IsChannelLine(RingLine, ChatSource.Emote), "the Handler's emote");
        Check.True(IltRingEvade.IsChannelLine("handler briefly channels infused Empyrean hatred to release a lethal ring.", 0x00),
                   "mid-line on a system line (a client that strips 'The ')");
        Check.False(IltRingEvade.IsChannelLine("Someone says, \"" + RingLine + "\"", ChatSource.Speech), "a player saying it");
        Check.False(IltRingEvade.IsChannelLine("Bob tells you, \"" + RingLine + "\"", 0x00), "a tell repeating it");
        Check.False(IltRingEvade.IsChannelLine("The handler waves.", ChatSource.Emote), "another Handler emote");
    }

    private static void AsksRynthAi()
    {
        var hub = NewHub("InfiniteLeaftide", "asks");
        hub.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 1, "one back-away request");
        var (id, radius, hold) = FakeAiHub.BackAways[0];
        Check.Eq(id, Handler, "away from the Handler");
        Check.Eq(radius, IltRingEvade.DangerYards, "12 yards (RynthAi widens it to Ring Range)");
        Check.Eq(hold, IltRingEvade.JumpHoldMs, "a 750 ms jump");
        Check.True(hub.RingEvade.LastOutcome.Contains("backs away"), "logged");
        hub.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 1, "a second line inside the cooldown asks nothing");

        var outside = NewHub("InfiniteLeaftide", "outside");
        FakeAiHub.BackAwayResult = RynthAiHub.BackAwayOutside;
        outside.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 1, "RynthAi decides the radius");
        Check.True(outside.RingEvade.LastOutcome.Contains("outside"), "and its answer is logged");

        var notFighting = NewHub("InfiniteLeaftide", "notnow");
        FakeAiHub.BackAwayResult = RynthAiHub.BackAwayNotNow;
        notFighting.OnChat(RingLine, ChatSource.Emote);
        Check.True(notFighting.RingEvade.LastOutcome.Contains("isn't fighting"), "RynthAi not fighting: it says so");
    }

    private static void NotThese()
    {
        var off = NewHub("InfiniteLeaftide", "setting-off");
        off.State.Combat.HandlerRingEvade = false;
        off.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 0, "setting off: nothing asked");

        var none = NewHub("InfiniteLeaftide", "none", handlerName: "Drudge Skulker");
        none.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 0, "no Empyrean Handler near: nothing asked");

        var said = NewHub("InfiniteLeaftide", "said");
        said.OnChat("Someone says, \"" + RingLine + "\"", ChatSource.Speech);
        Check.Eq(FakeAiHub.BackAways.Count, 0, "a player's copy: nothing asked");

        var retail = NewHub("Aeshnidae", "retail");
        retail.OnChat(RingLine, ChatSource.Emote);
        Check.Eq(FakeAiHub.BackAways.Count, 0, "off ILT: nothing asked");

        var noAi = NewHub("InfiniteLeaftide", "noai", withAi: false);
        noAi.OnChat(RingLine, ChatSource.Emote);
        Check.True(noAi.RingEvade.LastOutcome.Contains("RynthAi"), "without RynthAi: said in the log, no crash");
    }

    private static void ImportsSetting()
    {
        Check.True(new IltHubState().Combat.HandlerRingEvade, "on by default");
        string legacy = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Evader");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "active_profile.txt"), "Farm");
        File.WriteAllText(Path.Combine(legacy, "Farm.json"), "{ \"HandlerRingEvade\": false, \"MonsterRange\": 30 }");
        var state = new IltHubState();
        IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Evader");
        Check.False(state.Combat.HandlerRingEvade, "the active profile had it off: off here too");

        string legacy2 = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Oldie");
        Directory.CreateDirectory(legacy2);
        File.WriteAllText(Path.Combine(legacy2, "Default.json"), "{ \"MonsterRange\": 30 }");
        var old = new IltHubState();
        IltMigration.ImportRynthAiSettings(old, "InfiniteLeaftide", "Oldie");
        Check.True(old.Combat.HandlerRingEvade, "a profile from before the setting: on (the default)");
    }
}
