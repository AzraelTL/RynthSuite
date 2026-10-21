using System.Collections.Generic;
using RynthCore.Plugin.RynthAelrynth;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiTests.Harness;

namespace RynthServerPack.Tests;

// The Aelrynth account vault: /mule-data answers, one "~ael1" line a page (Aeshnidae.Mule MuleData.cs).
// Moved with the feed from RynthInventory.Tests on 2026-10-20: RynthAelrynth reads the vault now and
// hands it to RynthInventory through the storage feed (Plugins/Shared/StorageFeed.cs).
internal static class VaultTests
{
    public static void Register(Runner r)
    {
        r.Add("vault: pages are asked one after the other and filed as one record", Pages);
        r.Add("vault: other plugins' lines and players' imitations are left alone", Imitations);
        r.Add("vault: a server without the command is asked once", Unsupported);
        r.Add("vault: loading, changed and silent vaults are never filed half or empty", Retries);
        r.Add("vault: RynthAelrynth asks after login, every ten minutes and on /rserver vault", Schedule);
        r.Add("vault: the record reaches RynthInventory's format intact (plain ASCII)", HandOff);
    }

    private const string A = "{\"o\":2147483649,\"w\":100,\"n\":\"Acid Assagai\",\"i\":100667000,\"u\":100667001,\"v\":100667002,\"s\":3,\"m\":100,\"y\":1,\"e\":1}";
    private const string B = "{\"o\":2147483650,\"w\":101,\"n\":\"Pyreal\",\"i\":100667003,\"y\":8192}";

    private static string VaultPage(int page, int pages, int count, string items, bool ready = true, string why = "") =>
        ready
            ? "~ael1 {\"t\":\"mule\",\"ready\":true,\"page\":" + page + ",\"pages\":" + pages + ",\"count\":" + count + ",\"max\":1000,\"items\":[" + items + "]}"
            : "~ael1 {\"t\":\"mule\",\"ready\":false,\"why\":\"" + why + "\"}";

    private static (VaultFeed Feed, List<string> Sent) NewVault()
    {
        var sent = new List<string>();
        return (new VaultFeed(t => { sent.Add(t); return true; }), sent);
    }

    private static void Pages()
    {
        (VaultFeed v, List<string> sent) = NewVault();
        Check.True(v.Ask(1000), "ask starts");
        Check.Eq(sent.Count == 1 ? sent[0] : "?", "/mule-data", "page 1 asked first");
        Check.True(v.ShouldEat(VaultPage(1, 2, 2, A)), "page 1 is eaten");
        v.Tick(1100, "Tom");
        Check.Eq(sent.Count == 2 ? sent[1] : "?", "/mule-data 2", "page 2 asked when page 1 came");
        Check.Null(v.Record, "nothing filed before the last page");
        Check.True(v.ShouldEat(VaultPage(2, 2, 2, B)), "page 2 is eaten");
        v.Tick(1200, "Tom");
        StorageFeedRecord? r = v.Record;
        Check.True(r != null && v.TakeChanged() && !v.TakeChanged(), "filed once");
        Check.Eq(r?.Items.Count ?? -1, 2, "both pages' items");
        Check.Eq(r?.Id ?? 0, VaultFeed.VaultId("tom"), "id follows the account, case-blind");
        Check.True(StorageFeed.IsStandInId(r?.Id ?? 0), "id is a stand-in, far from real objects");
        Check.Eq(r?.Label ?? "", "Account vault", "label");
        Check.Eq(r?.Name ?? "", "Aelrynth vault", "name");
        StorageFeedItem a = r!.Items[0];
        Check.Eq(a.Name, "Acid Assagai", "name"); Check.Eq(a.Icon, 100667000u, "icon"); Check.Eq(a.Underlay, 100667001u, "underlay");
        Check.Eq(a.Overlay, 100667002u, "overlay"); Check.Eq(a.Stack, 3, "stack"); Check.Eq(a.MaxStack, 100, "max stack");
        Check.Eq(a.ItemType, 1u, "item type");
        Check.Eq(r.Items[1].Stack, 1, "no stack field is a stack of 1");
        Check.Eq(v.Status, VaultFeed.State.Idle, "idle again");
        Check.Eq(VaultFeed.VaultId("Tom"), 0xFF000000u | (FnvOf("tom") & 0x00FFFFFFu), "the id RynthInventory used before the move (no second vault)");
    }

    private static uint FnvOf(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h;
    }

    private static void Imitations()
    {
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            Check.False(v.ShouldEat("~ael1 {\"t\":\"inspect\",\"ok\":true}"), "the Companion's line stays visible");
            Check.False(v.ShouldEat("Welcome to Aelrynth"), "ordinary chat stays visible");
            Check.False(v.ShouldEat(null), "null is fine");
        }
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            Check.False(v.ShouldEat("Bob says, \"" + VaultPage(1, 1, 1, A) + "\"", 2), "spoken data line is not ours");
            Check.False(v.ShouldEat("Bob " + VaultPage(1, 1, 1, A), 12), "an emoted data line is not ours");
            Check.False(v.ShouldEat("Bob " + VaultPage(1, 1, 1, A)), "a data line not at the start is not ours, type unknown");
            Check.False(v.ShouldEat(VaultPage(1, 1, 1, A), 3), "a data line in a tell is not ours");
            Check.False(v.ShouldEat("Bob tells you, \"Unknown command: mule-data\"", 3), "a told unknown-command line is not ours");
            Check.False(v.ShouldEat("Unknown command: mule-data", 2), "unknown-command in speech is not ours");
            Check.False(v.ShouldEat("Unknown command: mule-data-ish"), "another command's refusal is not ours");
            Check.True(v.ShouldEat("Unknown command: mule-data", 15), "ACE's real reply (Help) is ours");
        }
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            Check.True(v.ShouldEat("  " + VaultPage(1, 1, 1, A), 0), "the server's line (Broadcast), leading space allowed");
        }
        {
            (VaultFeed v, _) = NewVault();
            Check.True(v.ShouldEat(VaultPage(1, 1, 1, A)), "a line typed by hand while we are not asking is eaten");
            v.Tick(0, "tom");
            Check.Null(v.Record, "but ignored when we did not ask");
        }
    }

    private static void Unsupported()
    {
        {
            (VaultFeed v, List<string> sent) = NewVault();
            v.Ask(0);
            Check.True(v.ShouldEat("Unknown command: /mule-data"), "the unknown-command reply is eaten while we wait");
            v.Tick(100, "tom");
            Check.Eq(v.Status, VaultFeed.State.Unsupported, "unsupported");
            Check.False(v.Ask(200), "not asked again");
            Check.Eq(sent.Count, 1, "one probe, no more");
            Check.True(v.Ask(300, force: true), "/rserver vault asks anyway");
        }
        {
            (VaultFeed v, _) = NewVault();
            Check.False(v.ShouldEat("Unknown command: /mule-data"), "an unknown-command line we did not provoke is left alone");
        }
    }

    private static void Retries()
    {
        // A vault the server is still loading is asked again, and never recorded as empty.
        {
            (VaultFeed v, List<string> sent) = NewVault();
            v.Ask(0);
            v.ShouldEat(VaultPage(1, 1, 0, "", ready: false, why: "loading"));
            v.Tick(10, "tom");
            Check.Eq(v.Status, VaultFeed.State.Waiting, "waiting");
            v.Tick(2000, "tom");
            Check.Eq(sent.Count, 1, "not before the retry delay");
            v.Tick(3500, "tom");
            Check.Eq(sent.Count, 2, "asked again after it");
            Check.Null(v.Record, "never recorded");
            for (int i = 0; i < 10; i++)
            {
                v.ShouldEat(VaultPage(1, 1, 0, "", ready: false, why: "loading"));
                v.Tick(4000 + i * 4000, "tom");
            }
            Check.True(v.Status != VaultFeed.State.Asking || sent.Count < 12, "it gives up after a few tries");
            Check.Null(v.Record, "still never recorded");
        }
        // A vault that changed between pages is read again, not filed half and half.
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            v.ShouldEat(VaultPage(1, 2, 3, A));
            v.Tick(10, "tom");
            v.ShouldEat(VaultPage(2, 2, 3, B));   // 2 read, the server says 3
            v.Tick(20, "tom");
            Check.Null(v.Record, "a mismatched read is not filed");
            Check.Eq(v.Status, VaultFeed.State.Waiting, "and is retried");
        }
        // Silence is an idle feed, not a stuck one.
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            v.Tick(20_000, "tom");
            Check.Eq(v.Status, VaultFeed.State.Idle, "no answer in time");
            Check.True(v.Ask(21_000), "can ask again");
        }
        // Garbage inside a mule line does not throw.
        {
            (VaultFeed v, _) = NewVault();
            v.Ask(0);
            v.ShouldEat("~ael1 {\"t\":\"mule\",\"ready\":tru");
            v.Tick(5, "tom");
            Check.True(v.Record == null && v.Status == VaultFeed.State.Idle, "bad JSON drops the read");
        }
    }

    private static void Schedule()
    {
        var sent = new List<string>();
        var f = new AelrynthFeature { SendCommand = t => { sent.Add(t); return true; } };
        f.Reset();
        long login = 50_000;
        Check.Null(f.VaultTick(login, "tom"), "nothing at login");
        Check.Eq(sent.Count, 0, "not asked at once");
        f.VaultTick(login + VaultFeed.FirstAskMs - 1, "tom");
        Check.Eq(sent.Count, 0, "not before the first delay");
        f.VaultTick(login + VaultFeed.FirstAskMs, "tom");
        Check.Eq(sent.Count == 1 ? sent[0] : "?", "/mule-data", "asked after the first delay");
        Check.True(f.OnChatLine(VaultPage(1, 1, 1, A), 0, login + VaultFeed.FirstAskMs + 10), "its reply is always hidden");
        StorageFeedRecord? read = f.VaultTick(login + VaultFeed.FirstAskMs + 20, "tom");
        Check.True(read != null && read.Items.Count == 1, "the finished read is handed back once");
        Check.Null(f.VaultTick(login + VaultFeed.FirstAskMs + 30, "tom"), "and only once");
        f.VaultTick(login + VaultFeed.FirstAskMs + VaultFeed.RefreshMs, "tom");
        Check.Eq(sent.Count, 2, "read again ten minutes later");
        f.OnChatLine(VaultPage(1, 1, 1, A), 0, login + VaultFeed.FirstAskMs + VaultFeed.RefreshMs + 1);
        f.VaultTick(login + VaultFeed.FirstAskMs + VaultFeed.RefreshMs + 2, "tom");
        Check.True(f.VaultAskNow(login + 700_000) && sent.Count == 3, "/rserver vault reads it now");

        // A server without the command: one probe per login, then quiet.
        var g = new AelrynthFeature { SendCommand = t => { sent.Add(t); return true; } };
        g.Reset();
        int before = sent.Count;
        g.VaultTick(0, "tom");
        g.VaultTick(VaultFeed.FirstAskMs, "tom");
        g.OnChatLine("Unknown command: mule-data", 0, VaultFeed.FirstAskMs + 1);
        g.VaultTick(VaultFeed.FirstAskMs + 2, "tom");
        g.VaultTick(VaultFeed.FirstAskMs + 3 * VaultFeed.RefreshMs, "tom");
        Check.Eq(sent.Count - before, 1, "no vault on the server: asked once, then never on a timer");
        g.Reset();
        Check.Eq(g.Vault.Status, VaultFeed.State.Idle, "the next login asks again");
    }

    private static void HandOff()
    {
        var items = new List<StorageFeedItem>
        {
            new(2147483649u, 100, "Acid \"Assagai\" \\ é", 100667000, 100667001, 100667002, 3, 100, 1),
            new(2147483650u, 101, "Pyreal", 100667003, 0, 0, 1, 0, 8192),
        };
        var rec = new StorageFeedRecord(VaultFeed.VaultId("tom"), VaultFeed.VaultName, VaultFeed.Label, items);
        string json = StorageFeed.ToJson(rec);
        bool ascii = true;
        foreach (char c in json) if (c > 0x7e || c < 0x20) ascii = false;
        Check.True(ascii, "plain ASCII (the broker carries ANSI)");
        StorageFeedRecord? back = StorageFeed.Parse(json, out string error);
        Check.Eq(error, "", "parsed");
        Check.True(back != null && back.Id == rec.Id && back.Name == rec.Name && back.Label == rec.Label && back.Items.Count == 2, "the record");
        Check.Eq(back!.Items[0], items[0], "an item, name escapes included");
        Check.Eq(back.Items[1], items[1], "another");
        Check.Eq(StorageFeed.TargetPlugin + "/" + StorageFeed.Action, "RynthInventory/storage", "sent to RynthInventory's \"storage\" command");
    }
}
