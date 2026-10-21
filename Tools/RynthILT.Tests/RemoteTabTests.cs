using System;
using System.IO;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// The ILT parts of RynthAi's Remote tab moved to RynthILT on 2026-10-20: the coin / pyreal lines under
// RynthAi's Stats ("remote-stats"), the session report's tail (GetText "session-report"), the Bank and
// Guardian sections' switches (RynthILT's own now, drawn in RynthAi's Options popup, "remote-options"),
// and the coin session restarting with RynthAi's shared session instead of RynthAi sending "rates reset".
internal static class RemoteTabTests
{
    public static void Register(Runner r)
    {
        r.Add("remote tab (RynthILT): its texts for RynthAi (server id, the session report's coins) and none off ILT", Texts);
        r.Add("remote tab (RynthILT): Bank / Guardian follow RynthILT's own switches; the stats and options draw nothing off ILT", Switches);
        r.Add("remote tab (RynthILT): the coins start over when RynthAi's shared session restarts", FollowsSession);
        r.Add("migration: RynthAi's huds.json Bank / Guardian switches come over once", ImportsSwitches);
    }

    private static IltHubController NewHub(string world, string folder)
    {
        FakeHost.Reset();
        FakeAiHub.Reset();
        FakeHost.WorldName = world;
        var host = FakeHost.Create(0x50000D21);
        var ai = new RynthAiClient(host);
        ai.UseTableForTests(FakeAiHub.Table);
        string dir = Path.Combine(Program.TempRoot, "remote-" + folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var hub = new IltHubController(host, dir, new IltSettings(), ai);
        hub.OnLoginComplete("Remoter", forceFromMigration: false);
        return hub;
    }

    private static void Texts()
    {
        var on = NewHub("InfiniteLeaftide", "texts-on");
        Check.Eq(on.GetText(RynthIltHub.TextServerId), "ilt", "its tabs belong to RynthCore's 'ilt'");
        string tail = on.GetText(RynthIltHub.TextSessionReport);
        Check.True(tail.StartsWith(", Coins ") && tail.Contains("/h, Pyr "), "the session report's tail: coins and pyreals per hour");
        Check.Eq(on.GetText("no-such-text"), string.Empty, "an unknown id: nothing");

        var off = NewHub("Aeshnidae", "texts-off");
        Check.Eq(off.GetText(RynthIltHub.TextSessionReport), string.Empty, "off ILT: no coins in RynthAi's report");
    }

    private static void Switches()
    {
        var on = NewHub("InfiniteLeaftide", "switches");
        Check.True(on.State.Remote.ShowBank && on.State.Remote.ShowGuardian, "both on by default");
        on.State.Remote.ShowBank = false;
        on.State.Remote.ShowGuardian = false;
        Check.False(on.RenderSection(RynthIltHub.SectionRemoteBank), "Bank switched off: nothing drawn");
        Check.False(on.RenderSection(RynthIltHub.SectionRemoteGuardian), "Guardian switched off: nothing drawn");

        var off = NewHub("Aeshnidae", "switches-off");
        Check.False(off.RenderSection(RynthIltHub.SectionRemoteStats) || off.RenderSection(RynthIltHub.SectionRemoteOptions)
                    || off.RenderSection(RynthIltHub.SectionRemoteBank) || off.RenderSection(RynthIltHub.SectionRemoteGuardian),
                    "off ILT: none of the Remote tab's ILT parts draws (checked before any ImGui call)");
    }

    private static void FollowsSession()
    {
        var hub = NewHub("InfiniteLeaftide", "session");
        long t1 = DateTime.UtcNow.AddMinutes(-10).Ticks;
        FakeAiHub.SessionStartTicks = t1;
        long now = Environment.TickCount64;
        hub.Rates.Tick(now);
        DateTime first = hub.Rates.SessionStartUtc;
        System.Threading.Thread.Sleep(30);
        hub.Rates.Tick(now + 100);
        Check.Eq(hub.Rates.SessionStartUtc, first, "the same shared session: no restart");
        FakeAiHub.SessionStartTicks = DateTime.UtcNow.Ticks;   // RynthAi's Reset XP
        hub.Rates.Tick(now + 200);
        Check.True(hub.Rates.SessionStartUtc > first, "RynthAi's session restarted: the coins start over too");
    }

    private static void ImportsSwitches()
    {
        string legacy = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Remoter");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Default.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, IltMigration.LegacyHudsFile), "{ \"ShowMiniRemote\": true, \"MiniShowBank\": false }");
        var state = new IltHubState();
        IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Remoter");
        Check.False(state.Remote.ShowBank, "Bank was hidden in RynthAi: hidden here");
        Check.True(state.Remote.ShowGuardian, "Guardian wasn't in the file: the default (shown)");
    }
}
