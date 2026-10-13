using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RynthCore.Loot;
using RynthCore.Plugin.RynthAi;
using RynthCore.Plugin.RynthAi.LegacyUi;
using RynthCore.PluginCore;
using RynthCore.PluginSdk;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthCore.RynthAiHostTests.Tests;

// Corpse looting speed:
//  - the finished corpse is not closed when another one is next (ACE closes it when the next
//    one is used); the late close of the old corpse must not disturb the new open.
//  - a pickup that merges into a stack already in the pack is confirmed when that stack grows
//    (it never arrives as an object in the pack: it waited out PickupArriveMaxMs, 4 s).
// The loot clock (CorpseOpenController.CorpseClock) is moved by hand here.
internal static class CorpseCloseTests
{
    private const uint Player = 0x50000D21;
    private const uint CorpseA = 0x80262100;
    private const uint CorpseB = 0x80262200;
    private const uint GemA = 0x80262101;
    private const uint GemA2 = 0x80262102;
    private const uint GemB = 0x80262201;
    private const uint PackStack = 0x80262301;
    private const uint ItemTypeContainer = 0x200, ItemTypeGem = 0x800;
    private const uint LongDescKey = 16;
    private const uint StackSizeKey = 12;
    private const string Me = "Ilsa Corvane";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static (uint Cell, float X, float Y, float Z) Me0 => (0x1048002Au, 100f, 100f, 0f);

    public static void Register(Runner r)
    {
        r.Add("corpse close: the next corpse is opened without closing the finished one; its late close doesn't disturb the new open", NextCorpseWithoutClose);
        r.Add("corpse close: a held-back close is sent when no other corpse is opened in time, and when the macro stops", DeferredCloseFallback);
        r.Add("loot pickup: a pickup that merges into a pack stack is confirmed when the stack grows, not after 4 s", StackMergeConfirmed);
    }

    private static long _now;

    private static void Clocked(Action body)
    {
        var old = RynthAiPlugin.CorpseClock;
        _now = 50_000_000;
        RynthAiPlugin.CorpseClock = () => _now;
        try { body(); }
        finally { RynthAiPlugin.CorpseClock = old; }
    }

    private static void Corpse(uint id, float dx)
    {
        FakeHost.Names[id] = "Corpse of Olthoi Swarm Drone";
        FakeHost.ItemTypes[id] = ItemTypeContainer;
        FakeHost.Strings[(id, LongDescKey)] = $"Killed by {Me}.";
        FakeHost.NotAttackable.Add(id);
        FakeHost.Positions[id] = (Me0.Cell, Me0.X + dx, Me0.Y, Me0.Z);
    }

    private static void Gem(uint id, uint container)
    {
        FakeHost.Names[id] = "Black Opal";
        FakeHost.ItemTypes[id] = ItemTypeGem;
        FakeHost.Containers[id] = container;
        FakeHost.Appraised.Add(id);
    }

    private static void World(bool twoCorpses, bool secondGem = false)
    {
        FakeHost.Reset();
        FakeHost.SalvageCalls = true;          // UseObject + MoveItemExternal
        FakeHost.WeaponCalls = true;           // RequestId, HasAppraisalData
        FakeHost.CloseContainerCalls = true;   // CloseContainer + GetGroundContainerId
        FakeHost.Names[Player] = Me;
        FakeHost.Positions[Player] = Me0;
        FakeHost.PlayerPose = Me0;
        Corpse(CorpseA, 1.0f);
        Gem(GemA, CorpseA);
        if (secondGem) Gem(GemA2, CorpseA);
        if (twoCorpses)
        {
            Corpse(CorpseB, 1.5f);
            Gem(GemB, CorpseB);
        }
    }

    private sealed class Bot
    {
        public readonly RynthAiPlugin P = new();
        public readonly LegacyUiSettings S = new()
        {
            IsMacroRunning = true, EnableLooting = true, EnableCombat = false, LootOwnership = 0,
            LootContentSettleMs = 0, LootEmptyCorpseMs = 0, StopLootingWhenPackFull = false,
        };
        public readonly WorldObjectCache Cache;

        public Bot(RynthCoreHost host)
        {
            typeof(RynthPluginBase).GetMethod("Attach", Any)!.Invoke(P, new object[] { FakeHost.LastApi });
            var dash = (LegacyDashboardRenderer)RuntimeHelpers.GetUninitializedObject(typeof(LegacyDashboardRenderer));
            typeof(LegacyDashboardRenderer).GetField("_settings", Any)!.SetValue(dash, S);
            Set("_dashboard", dash);
            Cache = new WorldObjectCache(host);
            Cache.SetPlayerId(Player);
            Set("_objectCache", Cache);
            Set("_playerId", Player);
            Set("_vitals", new PlayerVitalsCache { MaxHealth = 100, CurrentHealth = 100, MaxStamina = 100, CurrentStamina = 100, MaxMana = 100, CurrentMana = 100 });

            string profile = Path.Combine(Program.TempRoot, "corpse-close-test.json");
            var p = new LootProfile { Name = "gems" };
            var rule = new LootRule { Name = "Gems", Action = LootAction.Keep };
            rule.Conditions.Add(new ObjectClassCondition { ObjectClass = AcObjectClass.Gem });
            p.Rules.Add(rule);
            p.Save(profile);
            S.CurrentLootPath = profile;
        }

        public void Set(string f, object? v) => typeof(RynthAiPlugin).GetField(f, Any)!.SetValue(P, v);
        public T Get<T>(string f) => (T)typeof(RynthAiPlugin).GetField(f, Any)!.GetValue(P)!;
        public void Tick(BotActivity act = BotActivity.Looting)
        {
            Set("_activity", act);
            typeof(RynthAiPlugin).GetMethod("TickCorpseOpening", Any)!.Invoke(P, null);
        }
        public void TickFor(long ms, BotActivity act = BotActivity.Looting)
        {
            long end = _now + ms;
            while (_now < end) { _now += 20; Tick(act); }
        }
        public void Create(params uint[] ids)
        {
            foreach (uint id in ids) Cache.OnCreateObject(id);
            Cache.Tick();
        }
        public bool Completed(uint corpse) =>
            Get<Dictionary<int, long>>("_completedCorpses").ContainsKey(unchecked((int)corpse));
    }

    private static bool Logged(string part) => FakeHost.Logs.Any(l => l.Contains(part, StringComparison.Ordinal));

    /// <summary>Opens corpse A (the server's ViewContents) and loots its gem into the pack until A completes.</summary>
    private static void LootCorpseA(Bot bot)
    {
        bot.Tick();
        Check.True(FakeHost.Uses.Contains(CorpseA), "corpse A is opened");
        FakeHost.UseDone(0);
        FakeHost.GroundContainer = CorpseA;
        bot.P.OnViewObjectContents(CorpseA);
        for (int i = 0; i < 200 && !FakeHost.ExternalMoves.Any(m => m.Id == GemA); i++) { _now += 20; bot.Tick(); }
        Check.True(FakeHost.ExternalMoves.Any(m => m.Id == GemA), "the gem's move is sent");
        FakeHost.Containers[GemA] = Player;
        for (int i = 0; i < 200 && !bot.Completed(CorpseA); i++) { _now += 20; bot.Tick(); }
        Check.True(bot.Completed(CorpseA), "corpse A is completed");
    }

    private static void NextCorpseWithoutClose() => Clocked(() =>
    {
        World(twoCorpses: true);
        var host = FakeHost.Create(Player);
        var bot = new Bot(host);
        bot.Create(CorpseA, CorpseB, GemA, GemB);
        LootCorpseA(bot);

        Check.Eq(FakeHost.Closes.Count, 0, "corpse B is next: no close is sent for A");
        Check.True(Logged("no close sent (the server closes it when the next corpse is used)"), "and that is logged");

        // The next corpse's open goes out at once: nothing waits for a close answer.
        long doneAt = _now;
        for (int i = 0; i < 10 && !FakeHost.Uses.Contains(CorpseB); i++) { _now += 20; bot.Tick(); }
        Check.True(FakeHost.Uses.Contains(CorpseB), "corpse B is opened");
        Check.True(_now - doneAt < 200, $"within {_now - doneAt} ms of A completing (the close answer wait was 500 ms)");
        Check.Eq(FakeHost.Closes.Count, 0, "still no close for A");
        Check.True(Logged($"0x{CorpseA:X8} is left for the server to close with this open"), "the open of B leaves A to the server");

        // The server opens B; the client still shows A for a moment (its close not in yet).
        FakeHost.UseDone(0);
        bot.P.OnViewObjectContents(CorpseB);
        for (int i = 0; i < 5; i++) { _now += 20; bot.Tick(); }
        Check.Eq(bot.Get<int>("_openedContainerId"), unchecked((int)CorpseB), "B stays the open corpse while the client still shows A");
        // A's close lands (CloseGroundContainer A), then the client shows B.
        bot.P.OnStopViewingObjectContents(CorpseA);
        FakeHost.GroundContainer = CorpseB;
        for (int i = 0; i < 5; i++) { _now += 20; bot.Tick(); }
        Check.Eq(bot.Get<int>("_openedContainerId"), unchecked((int)CorpseB), "A's close doesn't touch B");
        Check.False(Logged($"container 0x{CorpseB:X8} closed before completion"), "B is not taken as closed");
        Check.Eq(FakeHost.Uses.Count(u => u == CorpseB), 1, "B is opened once (a second use would close it on ACE)");
        Check.Eq(FakeHost.Closes.Count, 0, "and A was never closed by RynthAi");
    });

    private static void DeferredCloseFallback() => Clocked(() =>
    {
        // Corpse B in range, but combat holds the tick: no open of B, so A's close goes after the hold limit.
        World(twoCorpses: true);
        var host = FakeHost.Create(Player);
        var bot = new Bot(host);
        bot.Create(CorpseA, CorpseB, GemA, GemB);
        LootCorpseA(bot);
        Check.Eq(FakeHost.Closes.Count, 0, "held back at first");
        bot.TickFor(RynthAiPlugin.DeferredCloseMaxMs - 200, BotActivity.Combat);
        Check.Eq(FakeHost.Closes.Count, 0, "still held just under the limit");
        bot.TickFor(400, BotActivity.Combat);
        Check.Eq(string.Join(",", FakeHost.Closes.Select(c => $"{c:X8}")), $"{CorpseA:X8}", "no other corpse opened in time: A is closed, once");

        // The macro stops while a close is held back: it goes at once.
        World(twoCorpses: true);
        var host2 = FakeHost.Create(Player);
        var bot2 = new Bot(host2);
        bot2.Create(CorpseA, CorpseB, GemA, GemB);
        LootCorpseA(bot2);
        Check.Eq(FakeHost.Closes.Count, 0, "held back");
        bot2.S.IsMacroRunning = false;
        _now += 20; bot2.Tick(BotActivity.Idle);
        Check.Eq(string.Join(",", FakeHost.Closes.Select(c => $"{c:X8}")), $"{CorpseA:X8}", "macro stopped: A is closed");

        // Only corpse A: closed at once on completion, as before.
        World(twoCorpses: false);
        var host3 = FakeHost.Create(Player);
        var bot3 = new Bot(host3);
        bot3.Create(CorpseA, GemA);
        LootCorpseA(bot3);
        Check.Eq(string.Join(",", FakeHost.Closes.Select(c => $"{c:X8}")), $"{CorpseA:X8}", "the last corpse is closed at once");
    });

    private static void StackMergeConfirmed() => Clocked(() =>
    {
        World(twoCorpses: false);
        Gem(PackStack, Player);
        FakeHost.Ints[(PackStack, StackSizeKey)] = 3;
        FakeHost.Ints[(GemA, StackSizeKey)] = 1;
        var host = FakeHost.Create(Player);
        var bot = new Bot(host);
        bot.Create(CorpseA, GemA, PackStack);
        bot.Tick();
        FakeHost.UseDone(0);
        FakeHost.GroundContainer = CorpseA;
        bot.P.OnViewObjectContents(CorpseA);
        for (int i = 0; i < 200 && !FakeHost.ExternalMoves.Any(m => m.Id == GemA); i++) { _now += 20; bot.Tick(); }
        Check.True(FakeHost.ExternalMoves.Any(m => m.Id == GemA), "the gem's move is sent");
        long sentAt = _now;

        // The server merges it: the corpse item is removed (still known to the client's cache, in no
        // container), and the pack stack grows from 3 to 4.
        FakeHost.Containers[GemA] = 0;
        FakeHost.Ints[(PackStack, StackSizeKey)] = 4;
        for (int i = 0; i < 400 && !Logged("pickup confirmed for"); i++) { _now += 20; bot.Tick(); }
        Check.True(Logged($"pickup confirmed for 0x{GemA:X8}; merged into the stack 0x{PackStack:X8} in the pack"),
            "confirmed as a merge into the pack stack: " + (FakeHost.Logs.LastOrDefault(l => l.Contains("pickup confirmed")) ?? "(none)"));
        Check.True(_now - sentAt < RynthAiPlugin.PickupArriveMaxMs, $"confirmed {_now - sentAt} ms after the send, under the 4 s limit");

        // A same-name stack that did not grow: not a merge, waits for the limit as before.
        World(twoCorpses: false);
        Gem(PackStack, Player);
        FakeHost.Ints[(PackStack, StackSizeKey)] = 3;
        var host2 = FakeHost.Create(Player);
        var bot2 = new Bot(host2);
        bot2.Create(CorpseA, GemA, PackStack);
        bot2.Tick();
        FakeHost.UseDone(0);
        FakeHost.GroundContainer = CorpseA;
        bot2.P.OnViewObjectContents(CorpseA);
        for (int i = 0; i < 200 && !FakeHost.ExternalMoves.Any(m => m.Id == GemA); i++) { _now += 20; bot2.Tick(); }
        long sent2 = _now;
        FakeHost.Containers[GemA] = 0;   // gone from the corpse, the pack stack unchanged
        for (int i = 0; i < 400 && !Logged("pickup confirmed for"); i++) { _now += 20; bot2.Tick(); }
        Check.True(_now - sent2 >= RynthAiPlugin.PickupArriveMaxMs, $"no stack grew: confirmed only at the limit ({_now - sent2} ms)");
    });
}
