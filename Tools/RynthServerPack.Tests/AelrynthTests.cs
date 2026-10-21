using System;
using System.IO;
using System.Linq;
using RynthCore.Engine.UI.Sections;
using RynthCore.Plugin.RynthAelrynth;
using RynthCore.Plugin.ServerPack;
using RynthCore.RynthAiTests.Harness;

namespace RynthServerPack.Tests;

// RynthAelrynth's own features (Plugins\RynthCore.Plugin.RynthAelrynth\Aelrynth), moved out of
// RynthOracle on 2026-10-20: the /top and /trial reply parser (RynthOracle.Tests' old
// AelrynthReplies), the own-reply hiding, the setting taken over from rynthoracle.json and its page.
internal static class AelrynthTests
{
    public static void Register(Runner r)
    {
        r.Add("aelrynth: the replies of /top, /top me and /trial parse", Replies);
        r.Add("aelrynth: replies to RynthAelrynth's own requests are hidden, typed ones show", HideOwnReplies);
        r.Add("aelrynth: RynthOracle's reply-hiding choice is taken over once", Migration);
        r.Add("aelrynth: its Leaderboards page is an Aelrynth-only view of the Server section", Pages);
    }

    private static void Replies()
    {
        var a = new AelrynthBoards();
        long now = 2_000_000;
        a.RequestTop("", now);
        Check.True(a.OnChatLine("Leaderboards - 37 characters, refreshed 4 min ago. /top <board> for the top ten, /top me for yourself.", now, out bool ours) && ours, "overview header");
        a.OnChatLine("  kills             Creature kills: Buffy, 1,234,567", now, out _);
        a.OnChatLine("  pk                PK kills: nobody yet", now, out _);
        Check.True(a.Overview.Count == 2 && a.Overview[0].Key == "kills" && a.Overview[0].Leader == "Buffy" && a.Overview[0].Value == "1,234,567"
                   && a.Overview[1].Leader == "nobody yet", "overview rows");

        a.RequestTop("kills", now);
        a.OnChatLine("Creature kills - top 10 of 37:", now, out _);
        a.OnChatLine("    1. Buffy                             1,234,567  <- you", now, out _);
        a.OnChatLine("    2. Dargoth                             987,654", now, out _);
        a.OnChatLine("    3. Sal                                     500  12 kills per death", now, out _);
        Check.True(a.BoardTitle == "Creature kills" && a.BoardRows.Count == 3 && a.BoardRows[0].IsYou && a.BoardRows[1].Value == "987,654"
                   && a.BoardRows[2].Extra == "12 kills per death", "board rows: " + string.Join(" | ", a.BoardRows.Select(r => $"{r.Rank} {r.Name} {r.Value} [{r.Extra}] {r.IsYou}")));

        a.RequestTop("me", now);
        a.OnChatLine("Buffy on the boards:", now, out _);
        a.OnChatLine("  Creature kills               #1 of 37, 1,234,567", now, out _);
        a.OnChatLine("  PK kills                     -", now, out _);
        Check.True(a.Standings.Count == 2 && a.Standings[0].Rank == 1 && a.Standings[1].Rank == 0, "/top me");

        Check.False(a.OnChatLine("    9. Nobody   12", now + 30_000, out _), "an entry long after its header is ignored");

        a.RequestTrial("", now);
        a.OnChatLine("Trials - a fresh personal copy of a listed dungeon, entered through its portal, is a timed course: ...", now, out _);
        a.OnChatLine("  The Citadel Below - Trial (02F0): tier 0 12:34 by Buffy | you: tier 0 12:34 | this week: Buffy 12:34 (tier 0)", now, out _);
        a.OnChatLine("      your next attempt here: in 12 min", now, out _);
        a.OnChatLine("Trophy: the week's fastest clear of each dungeon, any tier - a trophy and 1,000 experience, Sunday 20:00 (2026-W40). /trial <name> for the top 10 of one dungeon.", now, out _);
        Check.True(a.Trials.Count == 1 && a.Trials[0].Landblock == "02F0" && a.TrialLines.Count == 3, $"/trial overview ({a.Trials.Count} dungeons, {a.TrialLines.Count} lines)");

        a.RequestTrial("citadel", now);
        a.OnChatLine("The Citadel Below - Trial (02F0) - top 10, best per character, any tier:", now, out _);
        a.OnChatLine("   1. 12:34  Buffy (tier 0, 105 kills, 3 Oct)", now, out _);
        a.OnChatLine("  you: tier 0 12:34 (105 kills)", now, out _);
        Check.True(a.TrialRows.Count == 1 && a.TrialRows[0].Name == "Buffy" && a.TrialRows[0].Value == "12:34" && a.TrialNotes.Count == 1, "/trial dungeon top 10");
        Check.False(a.OnChatLine("Bob tells you, \"hello\"", now, out _), "ordinary chat is never taken");
    }

    private static void HideOwnReplies()
    {
        var f = new AelrynthFeature();
        long now = 3_000_000;
        f.Boards.RequestTop("", now);
        Check.True(f.OnChatLine("Leaderboards - 3 characters, refreshed just now. /top <board> ...", 0, now), "a reply to RynthAelrynth's /top is hidden");
        Check.True(f.OnChatLine("  kills             Creature kills: Buffy, 12", 0, now), "and its rows");
        Check.False(f.OnChatLine("Leaderboards - 3 characters, refreshed just now. /top <board> ...", 0, now + 60_000), "a typed /top's reply shows");
        Check.False(f.OnChatLine("  kills             Creature kills: Ann, 14", 0, now + 60_000), "and its rows");
        Check.True(f.Boards.Overview.Count == 1 && f.Boards.Overview[0].Leader == "Ann", "read either way");
        f.HideOwnReplies = false;
        f.Boards.RequestTrial("", now);
        Check.False(f.OnChatLine("Trials - a fresh personal copy of a listed dungeon ...", 0, now), "with hiding off, own replies show too");
        Check.False(f.OnChatLine("Bob says, \"hi\"", 0, now), "ordinary chat shows");
        f.Reset();
        Check.True(f.Boards.Overview.Count == 0 && f.Boards.Trials.Count == 0, "login/logout clears the boards");
    }

    private static void Migration()
    {
        string dir = ServerPackHooksTests.TempDir();
        try
        {
            string oracle = Path.Combine(dir, "rynthoracle.json"), mine = Path.Combine(dir, "rynthaelrynth.json");
            File.WriteAllText(oracle, "{ \"hideOwnQuestReplies\": false }");
            var s = new ServerPackSettings(mine);
            s.Load();
            Check.True(AelrynthFeature.Migrate(s, oracle), "taken over the first time");
            var f = new AelrynthFeature();
            f.LoadSettings(s);
            Check.False(f.HideOwnReplies, "the player's choice (shown) came over");
            Check.False(AelrynthFeature.Migrate(s, oracle), "never twice");
            var fresh = new ServerPackSettings(Path.Combine(dir, "fresh.json"));
            fresh.Load();
            AelrynthFeature.Migrate(fresh, Path.Combine(dir, "none.json"));
            var g = new AelrynthFeature();
            g.LoadSettings(fresh);
            Check.True(g.HideOwnReplies, "default: hidden");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void Pages()
    {
        var pages = ServerPackPages.Validate(AelrynthFeature.PageList.Select(p => new ServerPage(p.Id, p.Label, p.Tooltip, () => { })));
        Check.Eq(pages.Count, 1, "one page");
        ServerPackInfo info = new("aelrynth", "Aelrynth", new[] { "aelrynth.com" }, new[] { "Aelrynth*", "Aeshnidae*" });
        SectionManifest m = SectionManifestParser.Parse("RynthAelrynth", ServerPackModel.BuildSectionsJson(info, "RynthAelrynth", pages), out string error);
        Check.Eq(error, "", "RynthCore parses the manifest");
        SectionContribution view = m.Contributions.Single(c => c.Primary);
        Check.True(view.Server == "aelrynth" && view.Label == "Leaderboards" && view.Surface == "section:server-aelrynth.boards",
                   "Leaderboards, only while Aelrynth is active");
    }
}
