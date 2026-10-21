using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RynthCore.Engine.UI.Sections;
using RynthCore.Plugin.ServerPack;
using RynthCore.RynthAiTests.Harness;

namespace RynthServerPack.Tests;

// The shared hooks a server plugin's own features use (2026-10-20, when RynthOracle's and
// RynthInventory's Conquest and Aelrynth features moved into RynthConquest and RynthAelrynth):
// pages beside the server card, the plugin's own settings file (with a one-time migration), live
// numbers in the "RynthServer.Profile" JSON, and the own-reply window.
internal static class ServerPackHooksTests
{
    public static void Register(Runner r)
    {
        r.Add("hooks: page ids, surfaces and the surface a page is drawn under", PageNames);
        r.Add("hooks: bad, duplicate and surplus pages are left out", PageValidation);
        r.Add("hooks: pages are server-only primary views of the Server section, accepted by RynthCore's parser", PagesInManifest);
        r.Add("hooks: settings round-trip and keep values the plugin doesn't know", SettingsRoundTrip);
        r.Add("hooks: settings migration runs once, and a broken file means defaults", SettingsMigration);
        r.Add("hooks: profile values are published, validated and read back", ProfileValues);
        r.Add("hooks: the own-reply window opens, stretches and closes", ReplyWindow);
    }

    internal static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "RynthServerPack.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void PageNames()
    {
        Check.True(ServerPackPages.IsValidId("bank") && ServerPackPages.IsValidId("top-10"), "plain ids");
        Check.False(ServerPackPages.IsValidId("") || ServerPackPages.IsValidId("Bank") || ServerPackPages.IsValidId("a.b")
                    || ServerPackPages.IsValidId(new string('a', 17)), "empty, capitals, dots and long ids are refused");
        Check.Eq(ServerPackPages.SurfaceFor("conquest", "bank"), "section:server-conquest.bank", "surface");
        Check.Eq(ServerPackPages.PageIdOf("section:server-conquest.bank", "conquest"), "bank", "page from its surface");
        Check.Eq(ServerPackPages.PageIdOf("section:server-conquest.bank:popout", "conquest"), "bank", "popped out");
        Check.Null(ServerPackPages.PageIdOf("section:server-conquest", "conquest"), "the card is not a page");
        Check.Null(ServerPackPages.PageIdOf("section:server-aelrynth.top", "conquest"), "another server's page");
        Check.Null(ServerPackPages.PageIdOf("section:server-conquest.Bad!", "conquest"), "a malformed page id");
        Check.Null(ServerPackPages.PageIdOf(null, "conquest"), "null");
        string longest = ServerPackPages.SurfaceFor(new string('a', 32), new string('b', ServerPackPages.MaxIdLength));
        Check.True(longest.Length <= SectionManifestParser.MaxSurfaceLength, "the longest surface fits RynthCore's cap: " + longest.Length);
    }

    private static void PageValidation()
    {
        Action draw = () => { };
        var notes = new List<string>();
        var pages = new List<ServerPage>
        {
            new("one", "One", "", draw), new("one", "Again", "", draw), new("Two", "Two", "", draw),
            new("three", " ", "", draw), new("four", "Four", "tip", null!),
        };
        for (int i = 0; i < 8; i++) pages.Add(new ServerPage("p" + i, "Page " + i, "", draw));
        List<ServerPage> kept = ServerPackPages.Validate(pages, notes.Add);
        Check.Eq(kept.Count, ServerPackPages.MaxPages, "capped");
        Check.Eq(kept[0].Id, "one", "first kept");
        Check.Eq(kept.Count(p => p.Id == "one"), 1, "a duplicate id is dropped");
        Check.False(kept.Any(p => p.Id is "Two" or "three" or "four"), "bad id, no label, nothing to draw");
        Check.True(notes.Count == pages.Count - kept.Count, "every dropped page is reported: " + string.Join(" | ", notes));
        Check.Eq(ServerPackPages.Validate(null).Count, 0, "no pages");
        string label = new string('x', 60);
        Check.Eq(ServerPackPages.Validate(new[] { new ServerPage("x", label, "", draw) })[0].Label.Length, ServerPackPages.MaxLabelLength, "label capped");
    }

    private static void PagesInManifest()
    {
        ServerPackInfo info = new("conquest", "Conquest", new[] { "acconquest.ddns.net" }, new[] { "Conquest" });
        var pages = ServerPackPages.Validate(new[]
        {
            new ServerPage("main", "Conquest", "Bank, augs and more", () => { }),
            new ServerPage("boards", "Leaderboards", "", () => { }),
        });
        string json = ServerPackModel.BuildSectionsJson(info, "RynthConquest", pages);
        SectionManifest m = SectionManifestParser.Parse("RynthConquest", json, out string error);
        Check.Eq(error, "", "RynthCore parses it");
        Check.Eq(m.Contributions.Length, 3, "the card and two pages");
        SectionContribution card = m.Contributions.Single(c => !c.Primary);
        Check.Eq(card.Surface, "section:server-conquest", "the card keeps its surface");
        SectionContribution[] views = m.Contributions.Where(c => c.Primary).ToArray();
        Check.True(views.All(c => c.Section == ServerPackModel.SectionId && c.Server == "conquest" && c.IsSurface),
                   "pages: primary views of the Server section, only on Conquest");
        Check.Eq(views[0].Surface, "section:server-conquest.main", "page surface");
        Check.Eq(views[0].Label, "Conquest", "page label");
        Check.Eq(views[0].Tooltip, "Bank, augs and more", "page tooltip");
        Check.Eq(views[1].Tooltip, "Leaderboards (RynthConquest)", "a default tooltip");
        Check.True(views[0].Order < views[1].Order, "pages keep their order");
        Check.True(views[0].Width > 0 && views[0].Height > 0, "pages say their size");

        // Without pages the manifest is byte for byte what it was.
        Check.Eq(ServerPackModel.BuildSectionsJson(info, "RynthConquest", null), ServerPackModel.BuildSectionsJson(info, "RynthConquest"),
                 "no pages: the old manifest");
        Check.Eq(SectionManifestParser.Parse("x", ServerPackModel.BuildSectionsJson(info, "RynthConquest"), out _).Contributions.Length, 1,
                 "no pages: only the card");
    }

    private static void SettingsRoundTrip()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "rynthtest.json");
            File.WriteAllText(path, "{ \"future\": {\"a\":[1,2]}, \"flag\": false, \"name\": \"x\" }");
            var s = new ServerPackSettings(path);
            s.Load();
            Check.Eq(s.LastError, "", "read");
            Check.False(s.GetBool("flag", true), "a stored false");
            Check.True(s.GetBool("missing", true), "a missing value falls back");
            Check.True(s.GetBool("name", true), "a value of another type falls back");
            Check.Eq(s.GetString("name", "?"), "x", "text");
            Check.True(s.SetBool("flag", true), "a change says so");
            Check.False(s.SetBool("flag", true), "the same value is no change");
            s.SetString("quote", "a \"b\" \\ c");
            Check.True(s.Save(), "saved");

            var again = new ServerPackSettings(path);
            again.Load();
            Check.True(again.GetBool("flag", false), "the change was kept");
            Check.Eq(again.GetString("quote", ""), "a \"b\" \\ c", "text escaped and read back");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Check.Eq(doc.RootElement.GetProperty("future").GetProperty("a").GetArrayLength(), 2, "an unknown value is written back as it was");
            Check.False(File.Exists(path + ".tmp"), "no temporary file left");
            Check.True(ServerPackSettings.DefaultPath("RynthConquest").EndsWith(Path.Combine("RynthCore", "rynthconquest.json"), StringComparison.Ordinal),
                       "%APPDATA%\\RynthCore\\<plugin>.json");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void SettingsMigration()
    {
        string dir = TempDir();
        try
        {
            string old = Path.Combine(dir, "old.json"), mine = Path.Combine(dir, "mine.json");
            File.WriteAllText(old, "{ \"moved\": true, \"text\": \"t\" }");
            Check.Eq(ServerPackSettings.ReadBoolFrom(old, "moved"), (bool?)true, "the old home's value");
            Check.Null(ServerPackSettings.ReadBoolFrom(old, "text"), "not a true/false value");
            Check.Null(ServerPackSettings.ReadBoolFrom(old, "absent"), "missing value");
            Check.Null(ServerPackSettings.ReadBoolFrom(Path.Combine(dir, "nope.json"), "moved"), "missing file");

            var s = new ServerPackSettings(mine);
            s.Load();
            int runs = 0;
            Action<ServerPackSettings> migrate = t => { runs++; if (ServerPackSettings.ReadBoolFrom(old, "moved") is bool b) t.SetBool("moved", b); };
            Check.True(s.MigrateOnce("migratedFromOld", migrate), "runs the first time");
            Check.True(s.GetBool("moved", false), "imported");
            File.WriteAllText(old, "{ \"moved\": false }");
            var later = new ServerPackSettings(mine);
            later.Load();
            Check.False(later.MigrateOnce("migratedFromOld", migrate), "never again (the marker was saved)");
            Check.True(later.GetBool("moved", false), "later changes to the old home don't come over");
            Check.Eq(runs, 1, "ran once");

            File.WriteAllText(mine, "{ not json");
            var broken = new ServerPackSettings(mine);
            broken.Load();
            Check.True(broken.LastError.Length > 0 && broken.GetBool("moved", true), "a broken file reads as defaults");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void ProfileValues()
    {
        var v = new ServerProfileValues();
        int r0 = v.Revision;
        Check.True(v.Set("voidDotDurationScale", 1.35), "set");
        Check.True(v.Revision > r0, "a change bumps the revision");
        int r1 = v.Revision;
        Check.True(v.Set("voidDotDurationScale", 1.35) && v.Revision == r1, "the same value is no change");
        Check.False(v.Set("bad key", 1) || v.Set("", 1) || v.Set("x", double.NaN) || v.Set("y", double.PositiveInfinity), "bad keys and numbers");

        ServerPackInfo info = new("conquest", "Conquest", new[] { "h" }, new[] { "Conquest" });
        string json = ServerPackProfile.BuildJson(info, "RynthConquest", "0.2.0", true, "Conquest", v.Snapshot(out int rev));
        Check.Eq(rev, v.Revision, "the snapshot's revision");
        Check.Near(RynthCore.Plugin.Shared.ServerProfileView.Value(json, "voidDotDurationScale", 1), 1.35, 1e-12, "a reader gets the value");
        Check.Eq(RynthCore.Plugin.Shared.ServerProfileView.Value(json, "other", 7), 7.0, "a missing value falls back");
        Check.Eq(RynthCore.Plugin.Shared.ServerProfileView.Value("", "voidDotDurationScale", 1), 1.0, "no profile falls back");
        Check.Eq(RynthCore.Plugin.Shared.ServerProfileView.Value("{\"values\":{\"k\":\"2\"}}", "k", 1), 1.0, "text is not a number");
        Check.Eq(RynthCore.Plugin.Shared.ServerProfileView.Value("{broken", "k", 3), 3.0, "broken JSON falls back");
        using (JsonDocument doc = JsonDocument.Parse(ServerPackProfile.BuildJson(info, "RynthConquest", "0.2.0", true, "Conquest")))
            Check.Eq(doc.RootElement.GetProperty("values").EnumerateObject().Count(), 0, "no values: an empty object");

        Check.True(v.Remove("voidDotDurationScale") && !v.Remove("voidDotDurationScale"), "remove");
        v.Set("a", 1);
        int r2 = v.Revision;
        v.Clear();
        Check.True(v.Revision > r2 && v.Snapshot(out _).Length == 0, "clear (logout)");
    }

    private static void ReplyWindow()
    {
        var req = new ServerChatRequest();
        Check.False(req.Awaiting(0), "nothing asked");
        req.Sent(1000);
        Check.True(req.Awaiting(1000 + ServerChatRequest.WindowMs - 1), "inside the window");
        Check.False(req.Awaiting(1000 + ServerChatRequest.WindowMs), "the window closes");
        req.Sent(1000);
        req.Extend(5500);
        Check.True(req.Awaiting(7000), "a reply line stretches it");
        req.Clear();
        Check.False(req.Awaiting(1001), "cleared");
    }
}
