using System;
using System.IO;
using System.Linq;
using RynthCore.Engine.UI.Sections;
using RynthCore.Plugin.RynthConquest;
using RynthCore.Plugin.RynthOracle.Data;
using RynthCore.Plugin.ServerPack;
using RynthCore.RynthAiTests.Harness;

namespace RynthServerPack.Tests;

// RynthConquest's own features (Plugins\RynthCore.Plugin.RynthConquest\Conquest), moved out of
// RynthOracle on 2026-10-20: the reply parser (RynthOracle.Tests' old ConquestReplies), the
// own-reply hiding, the login /augs, the auto-deposit, the settings taken over from rynthoracle.json,
// the Void DoT rule it publishes, the custom quest list and its pages.
internal static class ConquestTests
{
    public static void Register(Runner r)
    {
        r.Add("conquest: the replies of /b, /augs, /enl augs, /bonus, /fship list and /top parse", Replies);
        r.Add("conquest: replies to RynthConquest's own requests are hidden, typed ones show", HideOwnReplies);
        r.Add("conquest: /augs once after login, then never again", LoginAugs);
        r.Add("conquest: the auto-deposit is off unless turned on, then once per ten-minute mark", AutoDeposit);
        r.Add("conquest: RynthOracle's settings are taken over once (defaults kept without them)", Migration);
        r.Add("conquest: the Void DoT rule for RynthOracle's timers", VoidRule);
        r.Add("conquest: the custom quest worklist and its status", CustomQuests);
        r.Add("conquest: its pages are Conquest-only views of the Server section", Pages);
    }

    private static void Replies()
    {
        var d = new ConquestData();
        long now = 1_000_000;
        d.RequestBank(now);
        Check.True(d.OnChatLine("[BANK] Your balances:", now, out bool ours) && ours, "bank heading (ours)");
        d.OnChatLine("[BANK] Pyreals: 1,039,678,533", now, out _);
        d.OnChatLine("[BANK] Event Tokens [Dragon Coins] (x): 12", now, out _);
        Check.True(d.Bank.Any(b => b.Name == "Pyreals" && b.Value.Contains("4,158 MMDs")), "pyreals with MMDs");
        Check.True(d.Bank.Any(b => b.Name == "Dragon Coins (x)"), "event token name");
        Check.False(d.OnChatLine("Bob says, \"[BANK] Pyreals: 999\"", now, out _), "a pasted balance is ignored");
        d.OnChatLine("[BANK] Pyreals: 5", now + 60_000, out bool late);
        Check.False(late, "a reply long after the request isn't ours (shows in chat)");

        d.RequestAugs(now);
        d.OnChatLine("Creature: 25", now, out _);
        d.OnChatLine("Duration: 3", now, out _);
        ConquestAug creature = d.Augs.First(a => a.Name == "Creature");
        Check.True(creature.Count == 25 && creature.NextLuminanceCost() == 6_229_500, $"Creature 25 next costs 6,229,500 ({creature.NextLuminanceCost()})");
        Check.True(d.DurationAugs == 3 && Math.Abs(d.VoidDurationMultiplier(5) - 2.15) < 1e-9, "void duration x(1 + 5*0.2 + 3*0.05)");

        d.RequestEnl(now);
        d.OnChatLine("Enlightenment Augmentations:", now, out _);
        d.OnChatLine("Skill Credits: 2 (10 max)", now, out _);
        d.OnChatLine("Damage: 5 (25 max)", now, out _);
        Check.True(d.EnlAugs.Count == 2 && d.EnlAugs[0].Name == "Damage", "enlightenment augs in display order");

        d.RequestBonus(now);
        d.OnChatLine("=== XP Bonuses ===", now, out _);
        d.OnChatLine("Quest Bonus: 14.18% (1,418 quests)", now, out _);
        d.OnChatLine("Total Bonus (Kills): 0.16%", now, out _);
        Check.True(d.Bonuses.First(b => b.Name == "Quest").Value == "14.18% (1,418 quests)" && d.Bonuses.First(b => b.Name == "Total (Kills)").Value == "0.16%", "bonuses");

        d.RequestFships(now);
        d.OnChatLine("Fellowships looking for members:", now, out _);
        d.OnChatLine("- Heroes (Leader: Bob) [3/14] @ Bur", now, out _);
        d.OnChatLine("- Quiet (Leader: Ann) [1/14]", now, out _);
        Check.True(d.Fellowships.Count == 2 && d.Fellowships[0].Location == "Bur" && d.Fellowships[1].MemberCount == 1, "fellowship list");

        ConquestBoard lvl = d.Boards.First(b => b.Key == "level");
        d.RequestBoard(lvl, now);
        d.OnChatLine("Top 2 Players by Level:", now, out _);
        d.OnChatLine("1: 300 - Alpha", now, out _);
        d.OnChatLine("2: 299 - Beta", now, out _);
        d.OnChatLine("57: 250 - Me (You)", now, out _);
        Check.True(lvl.Rows.Count == 2 && lvl.You != null && lvl.You.Rank == 57 && lvl.You.Name == "Me", "Conquest /top board");

        Check.Eq(ConquestData.WithdrawCommand(ConquestData.Withdrawable[ConquestData.DefaultWithdraw], "10"), "/bank withdraw n 10", "withdraw MMDs");
        Check.Null(ConquestData.WithdrawCommand(ConquestData.Withdrawable[0], "-3"), "withdraw refuses a bad amount");
        Check.Eq(ConquestData.TransferCommand(ConquestData.Transferable[2], "4", "Some One"), "/bank transfer p 1000000 \"Some One\"", "transfer MMD notes as pyreals");
        Check.Null(ConquestData.TransferCommand(ConquestData.Transferable[2], "1001", "X"), "transfer cap");
    }

    private static void HideOwnReplies()
    {
        var f = new ConquestFeature();
        f.Login(0);
        long now = 100_000;
        f.Data.RequestBank(now);
        Check.True(f.OnChatLine("[BANK] Pyreals: 1,000", now), "a reply to RynthConquest's /b is hidden");
        Check.False(f.OnChatLine("[BANK] Pyreals: 1,000", now + 60_000), "the same reply long after (typed /b) shows");
        Check.False(f.OnChatLine("Bob says, \"hello\"", now), "ordinary chat shows");
        f.HideOwnReplies = false;
        f.Data.RequestBank(now);
        Check.False(f.OnChatLine("[BANK] Pyreals: 1,000", now), "with hiding off, own replies show too");
        f.HideOwnReplies = true;

        // /myquests: only RynthConquest's own request's reply is hidden; the flags are read either way.
        Check.True(f.Flags.Request(_ => true, now), "own /myquests sent");
        Check.True(f.OnChatLine("assaultonaimaru - 1 solves (1719432000)\"x\" -1 0", now), "its reply is hidden");
        f.Flags.Tick(now + 2000);
        Check.True(f.Flags.TryGet("assaultonaimaru", out _), "and read");
        Check.False(f.OnChatLine("ghostdencompleted - 1 solves (1719432000)\"x\" -1 0", now + 3000), "a reply nobody here asked for shows");
        Check.True(f.Data.AutoDeposit(now).Length > 0 && f.OnChatLine("Deposited 12 items.", now), "the auto-deposit's reply is hidden");
    }

    private static void LoginAugs()
    {
        var f = new ConquestFeature();
        f.Login(1000);
        Check.Null(f.LoginAugsDue(1000 + ConquestFeature.AugsReadDelayMs), "not before the delay");
        Check.Eq(f.LoginAugsDue(1001 + ConquestFeature.AugsReadDelayMs), "/augs", "/augs after it");
        Check.Null(f.LoginAugsDue(5_000_000), "only once");
        f.Login(9_000_000);
        Check.Eq(f.LoginAugsDue(9_000_001 + ConquestFeature.AugsReadDelayMs), "/augs", "again for the next login");
    }

    private static void AutoDeposit()
    {
        var f = new ConquestFeature();
        f.Login(0);
        var at = new DateTime(2026, 10, 20, 14, 0, 5, DateTimeKind.Local);
        Check.False(f.AutoDeposit, "off by default (as it was in RynthOracle)");
        Check.Null(f.AutoDepositDue(at, 0), "off: no deposit at :00");
        f.AutoDeposit = true;
        Check.Eq(f.AutoDepositDue(at, 0), "/bank deposit", "on: /bank deposit at :00");
        Check.Null(f.AutoDepositDue(at.AddSeconds(30), 0), "once per mark");
        Check.Null(f.AutoDepositDue(at.AddMinutes(5), 0), "not at :05");
        Check.Eq(f.AutoDepositDue(at.AddMinutes(10), 0), "/bank deposit", "again at :10");
        long block = -1;
        Check.True(ConquestFeature.DepositDue(new DateTime(2026, 1, 1, 0, 20, 0), ref block), "the schedule: :20");
        Check.False(ConquestFeature.DepositDue(new DateTime(2026, 1, 1, 0, 21, 0), ref block), "the schedule: not :21");
    }

    private static void Migration()
    {
        string dir = ServerPackHooksTests.TempDir();
        try
        {
            string oracle = Path.Combine(dir, "rynthoracle.json"), mine = Path.Combine(dir, "rynthconquest.json");
            File.WriteAllText(oracle, "{ \"hideOwnQuestReplies\": false, \"conquestAutoDeposit\": true, \"tabName\": \"Conquest\" }");
            var s = new ServerPackSettings(mine);
            s.Load();
            Check.True(ConquestFeature.Migrate(s, oracle), "taken over the first time");
            var f = new ConquestFeature();
            f.LoadSettings(s);
            Check.True(f.AutoDeposit, "auto-deposit came over (on)");
            Check.False(f.HideOwnReplies, "hiding came over (off)");

            // Turned off here later: a newer rynthoracle.json never turns it back on.
            f.AutoDeposit = false;
            f.SaveSettings(s);
            var again = new ServerPackSettings(mine);
            again.Load();
            Check.False(ConquestFeature.Migrate(again, oracle), "never taken over twice");
            var g = new ConquestFeature();
            g.LoadSettings(again);
            Check.False(g.AutoDeposit, "the player's choice here stands");

            // A new player (no RynthOracle settings): the defaults.
            string fresh = Path.Combine(dir, "fresh.json");
            var n = new ServerPackSettings(fresh);
            n.Load();
            Check.True(ConquestFeature.Migrate(n, Path.Combine(dir, "missing.json")), "runs (nothing to take)");
            var h = new ConquestFeature();
            h.LoadSettings(n);
            Check.True(!h.AutoDeposit && h.HideOwnReplies, "defaults: auto-deposit off, own replies hidden");
            Check.True(new ServerPackSettings(fresh) is var reread && Reload(reread) && reread.GetBool(ConquestFeature.MigrationMarker, false),
                       "the marker is saved");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static bool Reload(ServerPackSettings s) { s.Load(); return true; }

    private static void VoidRule()
    {
        var f = new ConquestFeature();
        f.Login(0);
        Check.Eq(f.VoidScale, 1.0, "no augs, no Archmage's Endurance: x1");
        f.ArchmagesEndurance = 5;
        f.Data.RequestAugs(0);
        f.Data.OnChatLine("Duration: 3", 0, out _);
        Check.Near(f.VoidScale, 2.15, 1e-9, "x(1 + 5*0.2 + 3*0.05)");
        Check.Eq(ConquestFeature.VoidScaleKey, "voidDotDurationScale", "the profile value RynthOracle reads");
    }

    private static void CustomQuests()
    {
        Check.Eq(CustomQuestList.All.Count, 6, "six custom quests (customquests.csv, embedded)");
        Check.True(CustomQuestList.All.All(q => q.Server == "Conquest" && q.Flag.Length > 0 && q.Flag == q.Flag.ToLowerInvariant()), "Conquest rows, flags in lower case");
        var flags = new QuestFlagStore();
        CustomQuest darsh = CustomQuestList.All.First(q => q.Flag == "assaultonaimaru");
        DateTime now = DateTime.UtcNow;
        Check.False(darsh.IsComplete(flags, now), "not done without the flag");
        Check.Eq(darsh.Status(flags, now), "ready", "ready without the flag");
        long done = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        flags.OnChatLine($"assaultonaimaru - 1 solves ({done})\"x\" -1 3600", 0);
        flags.Tick(5000);
        Check.True(darsh.IsComplete(flags, now), "a repeatable on cooldown counts as done");
        Check.True(darsh.Status(flags, now) is not ("ready" or "completed") && darsh.Status(flags, now).Contains('m'), "its status is the time left: " + darsh.Status(flags, now));
    }

    private static void Pages()
    {
        var pages = ServerPackPages.Validate(ConquestFeature.PageList.Select(p => new ServerPage(p.Id, p.Label, p.Tooltip, () => { })));
        Check.Eq(pages.Count, ConquestFeature.PageList.Length, "every page is valid");
        Check.True(pages.Select(p => p.Label).SequenceEqual(new[] { "Conquest", "Leaderboards" }), "the Conquest and Leaderboards pages");
        ServerPackInfo info = new("conquest", "Conquest", new[] { "acconquest.ddns.net" }, new[] { "Conquest", "ConquestAC" });
        SectionManifest m = SectionManifestParser.Parse("RynthConquest", ServerPackModel.BuildSectionsJson(info, "RynthConquest", pages), out string error);
        Check.Eq(error, "", "RynthCore parses the manifest");
        SectionContribution[] views = m.Contributions.Where(c => c.Primary).ToArray();
        Check.Eq(views.Length, 2, "two pages in the Mini Remote");
        Check.True(views.All(c => c.Server == "conquest"), "shown only while Conquest is active");
        Check.True(views.Select(c => c.Surface).SequenceEqual(new[] { "section:server-conquest.main", "section:server-conquest.boards" }), "their surfaces");
    }
}
