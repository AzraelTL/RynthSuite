using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RynthCore.Plugin.RynthOracle;
using RynthCore.Plugin.RynthOracle.Data;
using RynthCore.Plugin.RynthOracle.Ui;
using RynthCore.Plugin.RynthOracle.Views;
using IltBreedingView = RynthCore.Plugin.Shared.IltBreedingView;
using IltGuardianView = RynthCore.Plugin.Shared.IltGuardianView;

namespace RynthOracle.Tests
{
    // The 0.3.0 port of SilentKelpie's RynthOracle 0.6.0 (RynthSuite PR 12): the merged Quests list
    // (Legendary, Markers, Dynamic Stamps and Temple guardians), Go to, the Give Parchment gate, the two
    // CSV loaders, the RynthILT profile and the Charms / Pet Breeding pieces. Ported from his tests where
    // they fit, adapted where our tree differs (the Refresh row, the Mini Remote keeping its tabs), and
    // added for what the merge must keep (favourites, click-to-print, the Markers tab's content).
    internal static partial class Program
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static uint _seq = 5000;

        private static string Body(UiWindow w) => Encoding.UTF8.GetString(w.Body);

        /// <summary>One draw of the main window, parsed by the engine's own parser (ops under the limit), body returned.</summary>
        private static string Frame(UiHost host, UiWindow main, OracleView view, string what, out int ops)
        {
            main.Begin(); view.Draw(main); main.End();
            ParseAll(host, what, out ops);
            string body = Body(main);
            main.MarkSubmitted(true);
            return body;
        }

        private static string Frame(UiHost host, UiWindow main, OracleView view, string what) => Frame(host, main, view, what, out _);

        private static void Click(UiHost host, UiWindow w, string id) => Apply(host, Event(1, ++_seq, w.Hash, w.KeyOf(id), new byte[] { 1, 0, 0 }));

        private static void Tick(UiHost host, UiWindow w, string id, bool on) => Apply(host, Event(2, ++_seq, w.Hash, w.KeyOf(id), new byte[] { (byte)(on ? 1 : 0) }));

        private static void Type(UiHost host, UiWindow w, string id, string text)
        {
            byte[] t = Encoding.UTF8.GetBytes(text);
            var payload = new byte[3 + t.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), (ushort)t.Length);
            t.CopyTo(payload, 3);
            Apply(host, Event(5, ++_seq, w.Hash, w.KeyOf(id), payload));
        }

        /// <summary>A context on a given server (a tag, or "" for retail) with the retail catalog loaded.</summary>
        private static OracleContext On(string tag)
        {
            OracleContext c = Context();
            c.Server.Force(tag, tag.Length > 0 ? tag : "Coldeve");
            c.Catalog.Load(tag, null);
            c.Catalog.SyncDiscoveries(c.Flags);
            return c;
        }

        // ── The CSV lists ────────────────────────────────────────────────────

        /// <summary>The Legendary list (legendary.csv) and its have / missing / timer rules.</summary>
        private static void Legendary()
        {
            Check(LegendaryList.All.Count == 40, $"legendary quests: {LegendaryList.All.Count} (40 expected)");
            Check(LegendaryList.Groups.SequenceEqual(new[] { "Graveyard", "Rynthid", "Hoshino", "Frozen Valley", "Neftet", "Random Locations" }),
                  "legendary groups: " + string.Join(",", LegendaryList.Groups));
            Check(LegendaryList.All.All(q => q.Flags.Length > 0 && q.Luminance > 0), "every legendary quest has a flag and luminance");
            var catalog = new QuestCatalog();
            catalog.Load("", _ => { });
            var unknown = LegendaryList.All.SelectMany(q => q.Flags).Where(f => !catalog.TryGet(f, out _)).ToList();
            Check(unknown.Count == 0, "every legendary flag is a known quest flag: " + string.Join(",", unknown));
            LegendaryQuest rtw = LegendaryList.All.First(q => q.CommonName == "RTW");
            Check(rtw.Flags[0] == "rtwcompleted_1013" && rtw.Label == "Defeating the Curator of Torment (RTW)", "RTW row: " + rtw.Label);

            var lf = new QuestFlagStore();
            long t = 900_000;
            long done = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
            long old = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds();
            // RTW done 2 h ago on a 20 h timer; End of Days done 3 days ago (ready); Lugian Assault never.
            lf.OnChatLine($"rtwcompleted_1013 - 4 solves ({done})\"RTW\" -1 72000", t);
            lf.OnChatLine($"eodcompleted_1013 - 1 solves ({old})\"EoD\" -1 72000", t + 1);
            lf.OnChatLine($"legendaryquestcounter_0913 - 12 solves ({old})\"John\" -1 0", t + 2);
            lf.Tick(t + 5_000);
            DateTime now = DateTime.UtcNow;
            TimeSpan? rtwLeft = rtw.TimeLeft(lf, now);
            Check(rtw.Have(lf) && rtwLeft is { } l && l.TotalHours > 17.9 && l.TotalHours < 18.1, $"RTW: done, ~18 h left ({rtwLeft})");
            LegendaryQuest eod = LegendaryList.All.First(q => q.CommonName == "EoD");
            Check(eod.Have(lf) && eod.Status(lf, now) == "ready", "EoD: done, timer up -> ready: " + eod.Status(lf, now));
            LegendaryQuest la = LegendaryList.All.First(q => q.CommonName == "LA");
            Check(!la.Have(lf) && la.TimeLeft(lf, now) == null && la.Status(lf, now) == "missing", "Lugian Assault: missing");
            Check(LegendaryList.JohnCount(lf) == 12, "John's counter read: " + LegendaryList.JohnCount(lf));
            Check(LegendaryList.JohnCount(new QuestFlagStore()) == null, "no counter on a fresh character");

            // A quest with a second timer flag: the longest wait of the two counts.
            LegendaryQuest erq = LegendaryList.All.First(q => q.Name == "Empyrean Rescue Quest");
            var ef = new QuestFlagStore();
            ef.OnChatLine($"empyreanrescuecompleted_1010 - 1 solves ({old})\"ERQ\" -1 0", t);
            ef.OnChatLine($"erqlegkeypickuptimer - 1 solves ({done})\"key\" -1 36000", t + 1);
            ef.Tick(t + 5_000);
            TimeSpan? erqLeft = erq.TimeLeft(ef, now);
            Check(erqLeft is { } e && e.TotalHours > 7.9 && e.TotalHours < 8.1, $"ERQ: key pickup timer decides (~8 h): {erqLeft}");
        }

        /// <summary>The Dynamic Stamps list (dynstamps.csv), its have / missing rules, RynthNav targets and the Go to parser.</summary>
        private static void DynamicStamps()
        {
            var all = DynamicStampList.All;
            Check(all.Count == 243, $"dynamic stamps: {all.Count} (243 expected from the ExtraDyn sheet)");
            Check(DynamicStampList.Areas.Count == 17 && DynamicStampList.Areas[0] == "Facility Hub" && DynamicStampList.Areas[^1] == "Candeth Keep",
                  "dynamic areas: " + string.Join(",", DynamicStampList.Areas));
            Check(all.All(s => s.Stamp.StartsWith("dynamic_") && s.Stamp == s.Stamp.ToLowerInvariant()), "every stamp is a lower-case dynamic_ flag");
            Check(all.Select(s => s.Stamp).Distinct().Count() == all.Count, "no stamp listed twice");
            Check(all.All(s => s.Label.Length > 0), "every row names an NPC or a place");

            DynamicStamp felscuda = all.First(s => s.Npc == "Felscuda");
            Check(felscuda.Stamp == "dynamic_1_&9jiytb!" && felscuda.Place == "Folthid Cellar" && felscuda.Reward == "Healing Rabbit Pie"
                  && felscuda.Note == "8.8S, 53.1E", $"Felscuda row: {felscuda.Place} / {felscuda.Reward} / {felscuda.Note}");

            // /myquests keys flags as \S+ lower case, so the stamps' ! & # ^ $ survive.
            var f = new QuestFlagStore();
            long t = 900_000, at = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
            f.OnChatLine($"Dynamic_1_&9jiyTB! - 1 solves ({at})\"Dynamic\" 1 0", t);
            f.OnChatLine($"Dynamic_1_ANND! - 1 solves ({at})\"Dynamic\" 1 0", t + 1);        // the sheet lost the closing !
            f.OnChatLine($"Dynamic_1_ZZZnewNPC! - 1 solves ({at})\"Dynamic\" 1 0", t + 2);   // not on the sheet
            f.OnChatLine($"rtwcompleted_1013 - 1 solves ({at})\"RTW\" -1 72000", t + 3);
            f.Tick(t + 5_000);
            Check(felscuda.Have(f), "Felscuda's stamp read from /myquests");
            DynamicStamp annd = all.First(s => s.Stamp == "dynamic_1_annd");
            Check(annd.Have(f) && annd.MatchedFlag(f) == "dynamic_1_annd!", "a stamp the sheet lost its ! on still matches");
            Check(!all.First(s => s.Npc == "Raxanza Folthid").Have(f), "an NPC not done is missing");
            List<string> extra = DynamicStampList.Unlisted(f);
            Check(extra.Count == 1 && extra[0] == "dynamic_1_zzznewnpc!", "unlisted: only the stamp the sheet doesn't have: " + string.Join(",", extra));
            DynamicStamp shortStamp = all.First(s => s.Stamp == "dynamic_1_s");
            var sf = new QuestFlagStore();
            sf.OnChatLine($"Dynamic_1_SomethingElse! - 1 solves ({at})\"Dynamic\" 1 0", t);
            sf.Tick(t + 5_000);
            Check(!shortStamp.Have(sf), "a cut-short stem under 14 characters isn't matched by prefix");

            // RynthNav targets, best first (RynthNav takes the first on its map).
            Check(string.Join(" | ", felscuda.NavCandidates()) == "8.8S, 53.1E | Folthid Cellar | Felscuda | Facility Hub",
                  "Felscuda nav: " + string.Join(" | ", felscuda.NavCandidates()));
            var gerent = new DynamicStamp { Area = "Graveyard", Place = "Gerent's Compound at 87.2S, 17.5W", Npc = "Gerent" };
            Check(string.Join(" | ", gerent.NavCandidates()) == "87.2S, 17.5W | Gerent's Compound | Gerent | Graveyard",
                  "coordinates inside the place, 'at' dropped: " + string.Join(" | ", gerent.NavCandidates()));
            var lost = new DynamicStamp { Area = "Odd Stamps", Place = "Lost Wish Cottages (Arwic) 42.6n 62.4e", Npc = "Bob" };
            Check(string.Join(" | ", lost.NavCandidates()) == "42.6N, 62.4E | Lost Wish Cottages (Arwic) | Lost Wish Cottages | Arwic",
                  "lower-case n/e, a bracketed town, no grouping area, a 3-letter NPC left out: " + string.Join(" | ", lost.NavCandidates()));
            var mhoire = new DynamicStamp { Area = "Looking Around", Place = "Mhoire Castle - Great Hall" };
            Check(string.Join(" | ", mhoire.NavCandidates()) == "Mhoire Castle - Great Hall | Mhoire Castle | Great Hall",
                  "a place's parts: " + string.Join(" | ", mhoire.NavCandidates()));
            Check(all.Count(s => s.NavCandidates().Count > 0) >= 200, "most stamps have somewhere to go: " + all.Count(s => s.NavCandidates().Count > 0));
        }

        /// <summary>Coordinates in a quest's notes become RynthNav targets (Go to).</summary>
        private static void GoToParsing()
        {
            var grael = NavTargets.Stops("Give Progenitor's Ichor to Hoshar. - Speak with Hoshar ibn Jalaq 59.6S 88.0W - Kill Mukkir Progenitor 86.5N 57.3E"
                                         + " - Speak with Hoshar ibn Jalaq - Abyssal Totem Gateway 86.5N 57.3E - Abyssal Totem Temple 86.5N 57.3E");
            Check(string.Join(" | ", grael.ConvertAll(s => s.Display)) == "Speak with Hoshar ibn Jalaq - 59.6S, 88.0W | Kill Mukkir Progenitor - 86.5N, 57.3E",
                  "Splitting Grael stops: " + string.Join(" | ", grael.ConvertAll(s => s.Display)));
            Check(grael[0].Target == "59.6S, 88.0W Speak with Hoshar ibn Jalaq", "a stop's RynthNav target is the coordinates, then its label");
            var shadow = NavTargets.Stops("Xarabydun 41.8S 15.8E - Speak with Ciandra Arcanum Alchemist - Empyrean Workshop 66.9N 15.9E - Cragstone 26.0N 47.0E Speak with Bretself the Translator - Xarabydun - Shade of Farelaith 74.3N 58.6E");
            Check(string.Join(" | ", shadow.ConvertAll(s => s.Label)) == "Xarabydun | Empyrean Workshop | Cragstone | Shade of Farelaith",
                  "Shadowfire Stone labels: " + string.Join(" | ", shadow.ConvertAll(s => s.Label)));
            Check(NavTargets.Stops("Kill 20 drudges near Holtburg.").Count == 0, "no coordinates: no stops");
            Check(NavTargets.Stops(null).Count == 0 && NavTargets.Stops("").Count == 0, "no text: no stops");
            Check(NavTargets.Stops("Meet at 42.6n 62.4e").Count == 1 && NavTargets.Stops("Meet at 42.6n 62.4e")[0].Display == "Meet - 42.6N, 62.4E",
                  "lower-case letters; 'at' dropped from the label");
            Check(NavTargets.FirstCoords("go to 8.8S, 53.1E now") == "8.8S, 53.1E" && NavTargets.FirstCoords("nowhere") == null, "the first coordinates in a text");
            Check(NavTargets.WithoutCoords("Hall 8.8S, 53.1E door").Contains("Hall") && !NavTargets.WithoutCoords("Hall 8.8S, 53.1E door").Contains("53.1"),
                  "the text without its coordinates");
            Check(NavTargets.Place("  Holtburg ").Target == "Holtburg", "a named place is its own target");
        }

        /// <summary>Both CSV loaders: counts from the shipped files, and a malformed file doesn't cost the good rows.</summary>
        private static void Loaders()
        {
            // The shipped files: counts, and they are embedded resources (the way the other lists are).
            Check(LegendaryList.All.Count == 40 && DynamicStampList.All.Count == 243, "the shipped lists load: 40 Legendary quests, 243 stamps");
            Check(MarkerList.All.Count == 100, "the shipped marker list still has its 100 markers");

            var rows = LegendaryList.Parse(new StringReader(
                "Group,Quest,CommonName,Luminance,Flags,Url,Note,Video\n"
                + "\"G\",\"Good\",\"GD\",5000,\"a_flag;B_Flag\",\"https://x\",\"a note, with a comma\",\"\"\n"
                + "\"G\",\"\",\"\",1,\"x\",\"\",\"\",\"\"\n"                     // no quest name: skipped
                + "\"G\",\"ShortRow\",\"\",abc\n"                              // short row, luminance isn't a number
                + "\n"
                + "just one field\n"
                + "\"G\",\"After\",\"\",7,\"c_flag\"\n"));
            Check(rows.Count == 3 && rows[0].Name == "Good" && rows[1].Name == "ShortRow" && rows[2].Name == "After",
                  "legendary: bad rows skipped, the rest kept: " + string.Join(",", rows.Select(r => r.Name)));
            Check(rows[0].Flags.SequenceEqual(new[] { "a_flag", "b_flag" }) && rows[0].Luminance == 5000 && rows[0].Note == "a note, with a comma"
                  && rows[0].Label == "Good (GD)", "legendary: flags lower-cased and split, quoted commas kept, the short name in the label");
            Check(rows[1].Luminance == 0 && rows[1].Flags.Length == 0 && rows[1].Url == "", "legendary: a short row reads as empty, luminance 0");
            Check(LegendaryList.Parse(new StringReader("")).Count == 0 && LegendaryList.Parse(new StringReader("Group,Quest\n")).Count == 0,
                  "legendary: an empty file is an empty list");
            Check(LegendaryList.Parse(new StringReader("Quest\r\nCRLF Quest\r\n")).Single().Name == "CRLF Quest", "legendary: CRLF lines");

            var stamps = DynamicStampList.Parse(new StringReader(
                "Area,Place,Npc,Reward,Stamp,Note\n"
                + "\"A\",\"P\",\"Npc1\",\"R\",\"Dynamic_1_Abc!\",\"8.8S, 53.1E\"\n"
                + "\"A\",\"P\",\"Dup\",\"R\",\"dynamic_1_abc!\",\"\"\n"           // the same stamp again: skipped
                + "\"A\",\"P\",\"NotDyn\",\"R\",\"something_else\",\"\"\n"       // not a dynamic_ flag: skipped
                + "\"A\",\"P\",\"Short\"\n"                                       // no stamp column: skipped
                + "\n"
                + "\"B\",\"\",\"\",\"\",\"Dynamic_2_x\",\"\"\n"));
            Check(stamps.Count == 2 && stamps[0].Stamp == "dynamic_1_abc!" && stamps[1].Stamp == "dynamic_2_x",
                  "dynamic: bad and repeated rows skipped, the rest kept: " + string.Join(",", stamps.Select(s => s.Stamp)));
            Check(stamps[0].Npc == "Npc1" && stamps[0].Note == "8.8S, 53.1E" && stamps[1].Label == "", "dynamic: fields read, a missing NPC and place leave an empty label");
            Check(DynamicStampList.Parse(new StringReader("")).Count == 0, "dynamic: an empty file is an empty list");

            // The markers file is the existing loader; a marker row still parses to its flag bit.
            Marker m = MarkerList.All[0];
            Check(m.Number == 1 && m.Flag.StartsWith("explorationmarkersfoundingroup") && m.BitMask == 1, "marker 1 keeps its quest flag bit");
        }

        // ── RynthILT's profile, charms and pet breeding ──────────────────────

        private const string QbProfileJson = "{\"ilt\":true,\"world\":\"InfiniteLeaftide\",\"fellowship\":{\"maxMembers\":29},"
            + "\"questBonus\":{\"disabled\":false,\"generation\":3,\"count\":41,\"status\":\"2 completed, 1 incomplete\","
            + "\"completed\":[\"qbflagone\",\"qbflagtwo\"],\"incomplete\":[\"qbflagthree\"]}}";

        /// <summary>RynthILT's "RynthILT.Server" profile JSON as the Quests, About and Pet Breeding pages read it.</summary>
        private static void IltProfile()
        {
            const string json = "{\"ilt\":true,\"world\":\"InfiniteLeaftide\",\"forced\":false,\"source\":\"probe\",\"checked\":\"2026-10-13T21:25:37Z\","
                + "\"features\":[{\"key\":\"bank\",\"label\":\"Banking\",\"group\":\"Character\",\"state\":1,\"where\":\"Mini Remote > Banking tab\"},"
                + "{\"key\":\"zone_share\",\"label\":\"Zone Share\",\"group\":\"Fellowship\",\"state\":1,\"where\":\"server\"},"
                + "{\"key\":\"fellowship_blackjack\",\"label\":\"Fellowship Blackjack\",\"group\":\"Fellowship\",\"state\":0,\"where\":\"/fblackjack\"}],"
                + "\"fellowship\":{\"maxMembers\":29,\"zoneShare\":1,\"blackjack\":0,\"luminanceShare\":-1},\"later\":{\"x\":1}}";
            IltServerProfile p = IltServerProfile.Parse(json);
            Check(p.Available && p.Ilt && p.World == "InfiniteLeaftide" && p.Source == "probe", "ILT profile header");
            Check(p.FellowshipMax == 29 && p.ZoneShare == 1 && p.Blackjack == 0 && p.LuminanceShare == -1, "ILT fellowship rules");
            Check(p.Features.Count == 3 && p.InGroup("Fellowship").Count() == 2, $"ILT features: {p.Features.Count}");
            Check(p.CheckedUtc == new DateTime(2026, 10, 13, 21, 25, 37, DateTimeKind.Utc), "checked time is UTC: " + p.CheckedUtc.ToString("o"));

            IltServerProfile none = IltServerProfile.Parse("");
            Check(!none.Available && none.FellowshipMax == 9, "no RynthILT: a retail fellowship");
            Check(!IltServerProfile.Parse("not json").Available, "garbage is ignored");
            Check(IltServerProfile.Parse("{\"ilt\":false}").FellowshipMax == 9, "an older profile without fellowship rules: retail size");
            Check(IltServerProfile.Parse("{\"fellowship\":{\"maxMembers\":5000}}").FellowshipMax == 9, "an absurd size is not believed");
            Check(IltServerProfile.StateText(1) == "on" && IltServerProfile.StateText(0) == "off" && IltServerProfile.StateText(-1) == "checking", "state words");

            Check(p.QuestBonus == null, "no questBonus object: no /qb list");
            IltServerProfile qb = IltServerProfile.Parse(QbProfileJson);
            Check(qb.QuestBonus is { Disabled: false, Generation: 3, Count: 41 } b && b.Completed.Count == 2 && b.Incomplete.Count == 1
                  && b.Incomplete[0] == "qbflagthree" && b.Status == "2 completed, 1 incomplete", "quest bonus list parsed");

            // The identity: a Leaftide world name, RynthILT's own verdict, and RynthCore's "ilt" server all give the ILT tag.
            Check(ServerIdentity.TagForWorld("InfiniteLeaftide") == ServerTags.InfiniteLeaftide, "InfiniteLeaftide");
            Check(ServerIdentity.TagForWorld("Infinite Leaftide Staging") == ServerTags.InfiniteLeaftide, "a Leaftide staging world is ILT");
            Check(ServerIdentity.TagForEngineId("ilt") == ServerTags.InfiniteLeaftide && ServerIdentity.EngineIdForTag(ServerTags.InfiniteLeaftide) == "ilt",
                  "RynthCore's ilt server <-> the ILT tag");
        }

        /// <summary>The About page's "Server features" list comes from RynthILT's profile.</summary>
        private static void ServerFeatures()
        {
            OracleContext c = On(ServerTags.InfiniteLeaftide);
            c.Settings.TabName = OracleView.TabAbout;
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            string body = Frame(host, main, view, "About, no RynthILT");
            Check(body.Contains("Load RynthILT to list the Infinite Leaftide features here."), "ILT without RynthILT: says to load it");
            c.Ilt.SetJson("{\"ilt\":true,\"world\":\"InfiniteLeaftide\",\"checked\":\"2026-10-13T21:25:37Z\","
                + "\"features\":[{\"key\":\"bank\",\"label\":\"Banking\",\"group\":\"Character\",\"state\":1,\"where\":\"Mini Remote > Banking tab\"},"
                + "{\"key\":\"pet_breeding\",\"label\":\"Pet breeding\",\"group\":\"Pets\",\"state\":0,\"where\":\"Pets\"}]}");
            body = Frame(host, main, view, "About, with RynthILT");
            Check(body.Contains("Server features") && body.Contains("1 of 2 on.") && body.Contains("Character") && body.Contains("###o.feat.h.Pets"),
                  "the features RynthILT lists, grouped, with a count on");
            Check(body.Contains("o.alert") && body.Contains("o.alertall") && body.Contains("o.readlogin"), "the About settings (quest alerts too) are all still there");
            Check(body.Contains("/ro trackers") && body.Contains("/ro legendary") && body.Contains("/ro markers") && body.Contains("/ro guardians") && body.Contains("/ro breeding"),
                  "the About page lists the new commands on ILT");
        }

        private static string BreedingJson(int essences)
        {
            var sb = new StringBuilder("{\"available\":true,\"world\":\"InfiniteLeaftide\",\"enabled\":\"on\",\"maturity\":\"on\",\"bond\":\"on\","
                + "\"room\":\"landblock 0x0106 variant 2\",\"freeSlots\":12,\"message\":\"\",\"essences\":[");
            for (int i = 0; i < essences; i++)
            {
                if (i > 0) sb.Append(',');
                bool male = i % 2 == 0, ready = i < 2;
                sb.Append("{\"id\":").Append(1000 + i).Append(",\"name\":\"Essence ").Append(i).Append("\",\"sex\":\"").Append(male ? 'M' : 'F')
                  .Append("\",\"tier\":125,\"bond\":100,\"ready\":").Append(ready ? "true" : "false")
                  .Append(",\"status\":\"").Append(ready ? "Ready" : "Rest 1h 05m").Append("\",\"readyIn\":").Append(ready ? 0 : 3900)
                  .Append(",\"charges\":7,\"chargesMax\":10,\"juvenile\":false,\"stage\":\"\",\"kills\":0,\"killsRequired\":300,")
                  .Append("\"mutations\":1,\"potencyMutations\":0,\"problems\":").Append(ready ? "[]" : "[\"resting after a breed\"]")
                  .Append(",\"boosters\":").Append(i == 0 ? "[\"Incense +10%\"]" : "[]").Append('}');
            }
            sb.Append("],\"pairs\":[{\"stud\":\"Essence 0\",\"dam\":\"Essence 1\",\"chanceBest\":0.13636,\"chanceWorst\":0.05,\"inheritMin\":1,\"inheritMax\":2}],")
              .Append("\"supplies\":[{\"key\":\"incense1\",\"name\":\"Breeding Incense\",\"effect\":\"+10% mutation chance\",\"count\":2,\"price\":250000,\"apply\":true,\"targets\":[1000,1001]},")
              .Append("{\"key\":\"neuter\",\"name\":\"Neutering Kit\",\"effect\":\"Permanent\",\"count\":1,\"price\":0,\"apply\":false,\"targets\":[]}],")
              .Append("\"rules\":[{\"label\":\"Stud charges\",\"value\":\"10/day\",\"source\":\"seen on server\",\"help\":\"Breeds per refill.\"},")
              .Append("{\"label\":\"Mutation decay\",\"value\":\"0.1\",\"source\":\"ILT profile\",\"help\":\"\"}],")
              .Append("\"events\":[{\"when\":\"10-15 12:00\",\"kind\":\"birth\",\"text\":\"Congratulations! A baby pet has been born: Pup\"}],")
              .Append("\"debug\":{\"when\":\"10-15 12:01\",\"enabled\":\"on\",\"location\":\"0x0106 variant 2\",\"match\":\"valid\",\"pet\":\"Essence 0\",\"sex\":\"M\",\"partners\":1}}");
            return sb.ToString();
        }

        /// <summary>
        /// The Pet Breeding tab: Infinite Leaftide only and only where the server has pet breeding; RynthILT's
        /// breeding JSON drawn within the op budget; Dance and supply uses asked first, then sent as RynthILT's verbs.
        /// </summary>
        private static void Breeding()
        {
            OracleContext c = Context();
            var view = new OracleView(c);
            Check(!view.VisibleTabs().Contains(OracleView.TabBreeding), "Aelrynth: no Pet Breeding tab");
            c.Server.Force("", "Coldeve");
            Check(!view.VisibleTabs().Contains(OracleView.TabBreeding), "other servers: no Pet Breeding tab");
            c.Server.Force(ServerTags.InfiniteLeaftide, "InfiniteLeaftide");
            Check(view.VisibleTabs().Contains(OracleView.TabBreeding), "Infinite Leaftide: the Pet Breeding tab");
            c.Ilt.SetJson("{\"ilt\":true,\"features\":[{\"key\":\"pet_breeding\",\"label\":\"Pet breeding\",\"group\":\"Pets\",\"state\":0,\"where\":\"\"}]}");
            Check(!view.VisibleTabs().Contains(OracleView.TabBreeding), "ILT with pet breeding off on the server: no Pet Breeding tab");
            c.Ilt.SetJson("{\"ilt\":true,\"features\":[{\"key\":\"pet_breeding\",\"label\":\"Pet breeding\",\"group\":\"Pets\",\"state\":1,\"where\":\"\"}]}");
            Check(view.VisibleTabs().Contains(OracleView.TabBreeding), "ILT with pet breeding on: the tab");
            c.Ilt.SetJson("{\"ilt\":true,\"features\":[{\"key\":\"pet_breeding\",\"label\":\"Pet breeding\",\"group\":\"Pets\",\"state\":-1,\"where\":\"\"}]}");
            Check(view.VisibleTabs().Contains(OracleView.TabBreeding), "ILT with pet breeding not known yet: the tab (it says what it is waiting for)");
            c.Ilt.SetJson("");

            IltBreedingView v = IltBreedingView.Parse(BreedingJson(2));
            Check(v.Loaded && v.Available && v.Enabled == "on" && v.Room == "landblock 0x0106 variant 2" && v.FreeSlots == 12, "breeding JSON header");
            Check(v.Essences.Count == 2 && v.Essences[0].Male && !v.Essences[1].Male && v.Essences[0].Ready && v.Essences[0].Boosters.Count == 1,
                  "breeding essences with sex, readiness and boosters");
            Check(v.Pairs.Count == 1 && Math.Abs(v.Pairs[0].ChanceBest - 0.13636) < 1e-9 && v.Pairs[0].InheritMax == 2, "breeding pair odds");
            Check(v.Supplies.Count == 2 && v.Supplies[0].Targets.Count == 2 && !v.Supplies[1].Apply, "breeding supplies and their targets");
            Check(v.Debug is { Match: "valid", Partners: 1 }, "breeding diagnostics");
            Check(!IltBreedingView.Parse("not json").Loaded && !IltBreedingView.Parse("").Loaded && !IltBreedingView.Parse("[1]").Loaded,
                  "bad breeding JSON is not loaded");
            Check(IltBreedingView.ApplyVerb(" incense1 ", 1000) == "apply incense1 1000" && IltBreedingView.DanceVerb() == "dance", "breeding verbs");

            // Nothing from RynthILT yet: the tab says to load it.
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabBreeding;
            Check(Frame(host, main, view, "Pet Breeding empty").Contains("Load RynthILT (0.3.1 or later)"), "without RynthILT: says to load it");

            // 50 essences with every section open: rows cut at 40, the page still fits.
            var sent = new List<string>();
            string ran = "";
            c.BreedingCommand = verb => { sent.Add(verb); return true; };
            c.RunCommand = cmd => { ran = cmd; return true; };
            c.Breeding = IltBreedingView.Parse(BreedingJson(50));
            Frame(host, main, view, "Pet Breeding opening");
            foreach (string header in new[] { "br.sup", "br.rules", "br.events" })
                Apply(host, Event(6, ++_seq, main.Hash, main.KeyOf(header), new byte[] { 1 }));
            string body = Frame(host, main, view, "Pet Breeding with 50 essences", out int ops);
            Check(ops > 5 && ops < 1400, $"Pet Breeding with 50 essences: {ops} ops");
            Console.WriteLine($"  InfiniteLeaftide/Pet Breeding (50 essences, all open): {ops} ops, {body.Length} bytes");
            Check(body.Contains("Essences (2 ready of 50)") && body.Contains("... and 10 more"), "essence rows cut at 40 with a count");
            Check(body.Contains("Essence 0 x Essence 1") && body.Contains("5%-13.6%"), "the ready pair with its odds");
            Check(body.Contains("Supplies (3 carried)") && body.Contains("250,000 pyreals"), "supplies with Ivo's price");
            Check(body.Contains("Stud charges") && body.Contains("seen on server") && body.Contains("In the breeding room"), "rules and diagnostics");
            Check(body.Contains("A baby pet has been born"), "recent events");
            Check(body.Contains("###refresh.Pet Breeding"), "the page has the Refresh row every page has");

            // Dance asks first; No drops it, Yes sends it.
            Click(host, main, "br.dance");
            Check(sent.Count == 0, "Dance asks before sending");
            body = Frame(host, main, view, "Pet Breeding asking");
            Check(body.Contains("Dance now?") && body.Contains("###br.yes") && body.Contains("###br.no"), "the question with Yes / No");
            Click(host, main, "br.no");
            body = Frame(host, main, view, "Pet Breeding no");
            Check(sent.Count == 0 && !body.Contains("###br.yes"), "No drops the question");
            Click(host, main, "br.dance");
            Frame(host, main, view, "Pet Breeding asking again");
            Click(host, main, "br.yes");
            Check(sent.Count == 1 && sent[0] == "dance", "Yes sends the dance verb");

            // A supply use on the selected essence (the first) asks first as well.
            body = Frame(host, main, view, "Pet Breeding supplies");
            Check(body.Contains("###br.use.0") && !body.Contains("###br.use.1"), "only a usable, carried supply has Use");
            Click(host, main, "br.use.0");
            Check(sent.Count == 1, "a supply use asks before sending");
            Frame(host, main, view, "Pet Breeding supply question");
            Click(host, main, "br.yes");
            Check(sent.Count == 2 && sent[1] == "apply incense1 1000", "Use then Yes sends apply with the essence id: " + string.Join("|", sent));

            Click(host, main, "br.debug");
            Check(sent.Count == 3 && sent[2] == "debug", "Diagnostics is sent straight away (it only reads)");
            Click(host, main, "br.remote");
            Check(ran == "/ra hub open breeding", "Mini Remote opens Pets > Breeding: " + ran);

            // The page's Refresh row asks RynthILT to appraise again.
            Click(host, main, "refresh.Pet Breeding");
            Check(sent.Count == 4 && sent[3] == "refresh", "Refresh sends refresh: " + string.Join("|", sent));

            // Breeding switched off on the server: said in red, page still drawn.
            c.Breeding = IltBreedingView.Parse("{\"available\":true,\"enabled\":\"off\",\"essences\":[]}");
            Check(Frame(host, main, view, "Pet Breeding off").Contains("Pet breeding is off on this server."), "breeding off is shown");
        }

        /// <summary>The Character tab's Charms section (RynthILT's list, Toggle routed back to it).</summary>
        private static void Charms()
        {
            const string charmsJson = "{\"available\":true,\"world\":\"InfiniteLeaftide\",\"acquiredCount\":2,\"activeCount\":1,"
                + "\"charms\":[{\"name\":\"Mana Barrier\",\"effect\":\"Absorbs damage\",\"acquired\":\"carried\",\"count\":1,\"tier\":3,\"maxTier\":5,"
                + "\"active\":\"on\",\"server\":\"on\",\"toggleId\":5001},"
                + "{\"name\":\"Swift Feet\",\"acquired\":\"seen\",\"lastSeen\":\"2026-10-12\",\"active\":\"unknown\",\"server\":\"off\"}],"
                + "\"hands\":[{\"id\":7001,\"name\":\"Guardian Hand\",\"active\":\"off\",\"server\":\"on\"}]}";

            OracleContext c = Context();
            c.Server.Force(ServerTags.InfiniteLeaftide, "InfiniteLeaftide");
            c.Settings.TabName = OracleView.TabCharacter;
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });

            string empty = Frame(host, main, view, "Character, no RynthILT");
            Check(empty.Contains("Load RynthILT to list your Infinite Leaftide charms"), "ILT without RynthILT: says to load it");

            uint toggled = 0;
            c.ToggleCharm = id => { toggled = id; return true; };
            c.Charms = RynthCore.Plugin.Shared.IltCharmsView.Parse(charmsJson);
            string body = Frame(host, main, view, "Character with charms", out int ops);
            Check(ops > 5 && ops < 1400, $"Character with charms: {ops} ops");
            Check(body.Contains("Charms (2)") && body.Contains("Mana Barrier") && body.Contains("T3/5") && body.Contains("seen 2026-10-12"),
                  "the charms RynthILT listed, with tier and acquired state");
            Check(body.Contains("server off"), "a charm the server switched off is marked");
            Check(body.Contains("Guardian Hands (1)"), "the Guardian Hands are listed");
            Check(body.Contains("###c.charm.0") && !body.Contains("###c.charm.1"), "an uncarried charm has no Toggle");
            Click(host, main, "c.charm.0");
            Check(toggled == 5001, "the carried charm's Toggle goes to RynthILT with its id");
            Click(host, main, "c.hand.0");
            Check(toggled == 7001, "the Guardian Hand's Toggle goes to RynthILT with its id");

            // Another world: RynthILT answers "not available", so no charms section at all.
            c.Server.Force("", "");
            c.Charms = RynthCore.Plugin.Shared.IltCharmsView.Parse("{\"available\":false,\"charms\":[]}");
            Check(!Frame(host, main, view, "Character off ILT").Contains("Charms ("), "off ILT: no charms section");
        }

        // ── The merged Quests list ───────────────────────────────────────────

        private const string GuardianJson = "{\"available\":true,\"world\":\"InfiniteLeaftide\",\"chat\":true,\"auto\":false,\"buy\":true,"
            + "\"cooldownHours\":20,\"answer\":\"Pyreal\",\"npc\":\"Guardian of Attribute Enlightenment\",\"answerAgeSec\":30,"
            + "\"heard\":\"What is the coin of the realm?\",\"running\":false,\"status\":\"idle\",\"synced\":\"2026-10-18T19:05:00Z\","
            + "\"rows\":[{\"attribute\":\"Strength\",\"npc\":\"Fiun Strongarm\",\"count\":3,\"status\":\"Ready\",\"eta\":\"\",\"ready\":true,\"exact\":true},"
            + "{\"attribute\":\"Focus\",\"npc\":\"Fiun Thinker\",\"count\":1,\"status\":\"Cooldown\",\"eta\":\"in 5h 2m\",\"ready\":false,\"exact\":false}]}";

        /// <summary>The "N shown" count in a page body.</summary>
        private static int Shown(string body)
        {
            int at = body.IndexOf(" shown", StringComparison.Ordinal);
            if (at < 0) return -1;
            int s = at;
            while (s > 0 && char.IsDigit(body[s - 1])) s--;
            return int.TryParse(body.AsSpan(s, at - s), out int n) ? n : -1;
        }

        /// <summary>Each source appears in the one list, per server, with its filters; Trackers only and Show narrow it.</summary>
        private static void MergedList()
        {
            // Retail: quests, Legendary quests and markers; no stamps or guardians.
            OracleContext c = On("");
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;
            int quests = c.Catalog.Quests.Count;
            int leg = LegendaryList.All.Count, mrk = MarkerList.All.Count, dyn = DynamicStampList.All.Count;

            Check(view.AvailableKinds().SequenceEqual(new[] { QuestKind.Quest, QuestKind.Legendary, QuestKind.Marker }), "retail kinds: quests, Legendary, markers");
            string body = Frame(host, main, view, "Quests, retail, everything");
            Check(Shown(body) == quests + leg + mrk, $"retail: quests + Legendary + markers in one list ({Shown(body)} shown, {quests + leg + mrk} expected)");
            Check(body.Contains("###q.trackers") && body.Contains("###q.kind") && body.Contains("###q.sort") && body.Contains("###q.timers"),
                  "Trackers only, Show, Sort and the Quest Timers button are on the page");
            Check(body.Contains("###refresh.Quests"), "the Refresh row every page has is still on the Quests page");

            // Trackers only: the quests drop out, the trackers' status shows above.
            Tick(host, main, "q.trackers", true);
            body = Frame(host, main, view, "Quests, trackers only");
            Check(Shown(body) == leg + mrk, $"Trackers only: Legendary + markers ({Shown(body)})");
            Check(body.Contains("toward John") && body.Contains("markers found") && body.Contains("LEG") && !body.Contains("###q.r.30minattributes"),
                  "Trackers only: John's counter and the markers' progress above, no quests");
            Check(!body.Contains("###dy.area") && !body.Contains("Dynamic stamps"), "retail: no stamp status or area picker");
            Tick(host, main, "q.trackers", false);

            // Show: one kind per source.
            foreach ((QuestKind kind, int want) in new[] { (QuestKind.Quest, quests), (QuestKind.Legendary, leg), (QuestKind.Marker, mrk) })
            {
                view.ShowTrackers(kind);
                body = Frame(host, main, view, "Quests, " + kind);
                Check(Shown(body) == want, $"Show {kind}: {Shown(body)} shown ({want} expected)");
            }

            // Done / Not done on markers: the Markers tab's "only the ones still to find".
            long at = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
            c.Flags.OnChatLine($"explorationmarkersfoundingroupa - 3 solves ({at})\"Markers\" 1023 0", c.NowMs);
            c.Flags.Tick(c.NowMs + 5_000);
            view.ShowTrackers(QuestKind.Marker);
            body = Frame(host, main, view, "Markers, none filtered");
            Check(Shown(body) == 100 && body.Contains("2 / 100 markers found"), "markers: bits 1 and 2 of group A found: " + (body.Contains("markers found") ? "status shown" : "no status"));
            Tick(host, main, "q.notdone", true);
            body = Frame(host, main, view, "Markers, not done");
            Check(Shown(body) == 98, $"markers, Not done: the ones still to find ({Shown(body)})");
            Tick(host, main, "q.notdone", false);
            Tick(host, main, "q.done", true);
            body = Frame(host, main, view, "Markers, done");
            Check(Shown(body) == 2, $"markers, Done: the two found ({Shown(body)})");
            Tick(host, main, "q.done", false);

            // Repeatable / One-time: Legendary rows repeat, markers don't.
            view.ShowTrackers(null);
            Tick(host, main, "q.rep", true);
            body = Frame(host, main, view, "Trackers, repeatable");
            Check(Shown(body) == leg, $"Repeatable: the Legendary quests ({Shown(body)})");
            Tick(host, main, "q.rep", false);
            Tick(host, main, "q.one", true);
            body = Frame(host, main, view, "Trackers, one-time");
            Check(Shown(body) == mrk, $"One-time: the markers ({Shown(body)})");
            Tick(host, main, "q.one", false);

            // The quest-only filters leave the trackers out.
            Tick(host, main, "q.new", true);
            body = Frame(host, main, view, "Trackers, Not in list");
            Check(Shown(body) == 0, "Not in list is a quests-only filter: no tracker rows");
            Tick(host, main, "q.new", false);

            // Search reaches the trackers' text (the notes, places, flags).
            Type(host, main, "q.search", "mhoire");
            body = Frame(host, main, view, "Trackers, search");
            int wantSearch = LegendaryList.All.Count(l => $"{l.Label} {l.Group} {l.Note} {string.Join(" ", l.Flags)}".Contains("mhoire", StringComparison.OrdinalIgnoreCase))
                             + MarkerList.All.Count(m => $"#{m.Number} {m.Name} {m.Location} {m.Hint}".Contains("mhoire", StringComparison.OrdinalIgnoreCase));
            Check(Shown(body) == wantSearch && wantSearch > 0, $"Search \"mhoire\" over the trackers: {Shown(body)} shown ({wantSearch} expected)");
            Type(host, main, "q.search", "");

            // Favourites: tracking a quest (the * list) also marks the Legendary row of the same flag.
            LegendaryQuest rtw = LegendaryList.All.First(q => q.CommonName == "RTW");
            c.Book.SetTracked(rtw.Flags[0], true);
            Tick(host, main, "q.tracked", true);
            body = Frame(host, main, view, "Trackers, tracked");
            Check(Shown(body) == 1 && body.Contains("###q.r.lg:" + rtw.Name), $"Tracked: the tracked quest's Legendary row ({Shown(body)})");
            view.ShowTrackers(QuestKind.Quest);
            body = Frame(host, main, view, "Quests, tracked");
            Check(Shown(body) == 3 && body.Contains("###q.t.30minattributes"), $"Tracked quests are still the favourites (the * list): {Shown(body)} shown");
            Tick(host, main, "q.tracked", false);
            c.Book.SetTracked(rtw.Flags[0], false);

            // Sorts do not throw and keep every row.
            view.ShowTrackers(null);
            foreach (int sort in new[] { 1, 2, 3, 4, 5, 0 })
            {
                Apply(host, Event(3, ++_seq, main.Hash, main.KeyOf("q.sort"), BitConverter.GetBytes(sort)));
                body = Frame(host, main, view, "Trackers, sort " + sort);
                Check(Shown(body) == leg + mrk, $"sort {sort} keeps every row ({Shown(body)})");
            }

            // Infinite Leaftide: stamps and guardians join; the area picker narrows the stamps.
            OracleContext ic = On(ServerTags.InfiniteLeaftide);
            var iv = new OracleView(ic);
            var ihost = new UiHost();
            UiWindow imain = ihost.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            ic.Settings.TabName = OracleView.TabQuests;
            ic.Guardian = IltGuardianView.Parse(GuardianJson);
            ic.GuardianRevision++;
            int iq = ic.Catalog.Quests.Count;
            body = Frame(ihost, imain, iv, "Quests, ILT, everything");
            Check(Shown(body) == iq + leg + mrk + dyn + 2, $"ILT: quests + Legendary + markers + {dyn} stamps + 2 guardians ({Shown(body)}, {iq + leg + mrk + dyn + 2} expected)");
            foreach ((QuestKind kind, int want, string tag) in new[] { (QuestKind.Dynamic, dyn, "DYN"), (QuestKind.Guardian, 2, "GRD") })
            {
                iv.ShowTrackers(kind);
                body = Frame(ihost, imain, iv, "Quests, ILT, " + kind);
                Check(Shown(body) == want && body.Contains(tag), $"ILT Show {kind}: {Shown(body)} shown ({want} expected)");
            }
            iv.ShowTrackers(QuestKind.Dynamic);
            Frame(ihost, imain, iv, "Quests, ILT, stamps");
            Apply(ihost, Event(3, ++_seq, imain.Hash, imain.KeyOf("dy.area"), BitConverter.GetBytes(1)));
            body = Frame(ihost, imain, iv, "Quests, ILT, one area");
            int areaCount = DynamicStampList.All.Count(s => s.Area == DynamicStampList.Areas[0]);
            Check(Shown(body) == areaCount, $"the area picker narrows the stamps to {DynamicStampList.Areas[0]}: {Shown(body)} ({areaCount} expected)");

            // A saved tab name of an old tab opens Quests; the Markers / Legendary / Dynamic Stamps tabs are gone.
            ic.Settings.TabName = "Markers";
            Check(iv.Tab == OracleView.TabQuests, "a saved \"Markers\" tab opens Quests");
            ic.Settings.TabName = "Dynamic Stamps";
            Check(iv.Tab == OracleView.TabQuests, "a saved \"Dynamic Stamps\" tab opens Quests");
            string[] tabs = iv.VisibleTabs();
            Check(!tabs.Contains("Markers") && !tabs.Contains("Legendary") && !tabs.Contains("Dynamic Stamps"), "no separate tracker tabs: " + string.Join(",", tabs));
        }

        /// <summary>Everything the old Markers tab showed is reachable from the Quests list.</summary>
        private static void MarkersReachable()
        {
            OracleContext c = On("");
            var printed = new List<string>();
            c.PrintSink = printed.Add;
            var navs = new List<string>();
            c.SendNav = line => { navs.Add(line); return true; };
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;
            long at = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
            c.Flags.OnChatLine($"explorationmarkersfoundingroupa - 1 solves ({at})\"Markers\" 1023 0", c.NowMs);
            c.Flags.Tick(c.NowMs + 5_000);

            view.ShowTrackers(QuestKind.Marker);            // /ro markers
            string body = Frame(host, main, view, "Markers");
            Check(body.Contains("1 / 100 markers found"), "the progress bar: markers found of 100");
            Marker next = MarkerList.All[1];
            Check(body.Contains("Next marker:") && body.Contains($"#{next.Number} {next.Name} ({next.Location})"), "the next marker to run to");
            Check(body.Contains("###q.r.mk:1") && body.Contains("###q.r.mk:2"), "a row per marker");

            // "Next marker" opens that marker, and a click prints the directions in chat (the old tab did).
            Click(host, main, "m.next");
            Check(printed.Count >= 2 && printed[0].StartsWith($"Marker {next.Number}: {next.Name}") && printed.Contains(next.Hint), "a click prints the marker and its directions: " + string.Join(" | ", printed.Take(3)));
            body = Frame(host, main, view, "Marker opened");
            Check(body.Contains("Marker 2") && body.Contains("not found yet") && body.Contains("###q.nav.go") && body.Contains("###m.sel.print") && body.Contains("###m.sel.close"),
                  "the marker opens below the list: status, Go to, Print, Close");
            Click(host, main, "q.nav.go");
            Check(navs.Count == 1 && navs[0].StartsWith("go "), "Go to sends RynthNav a go: " + string.Join("|", navs));
            Click(host, main, "q.nav.route");
            Click(host, main, "q.nav.arrow");
            Check(navs.Count == 3 && navs[1].StartsWith("route ") && navs[2].StartsWith("arrow "), "Route and Arrow likewise");

            // Print in chat from the panel; a second click on the row closes it (and prints again).
            printed.Clear();
            Click(host, main, "m.sel.print");
            Check(printed.Count >= 2, "Print in chat from the opened marker");
            printed.Clear();
            Click(host, main, "q.r.mk:2");
            body = Frame(host, main, view, "Marker closed");
            Check(printed.Count >= 2 && !body.Contains("###m.sel.close"), "a second click on the row prints again and closes it");
        }

        /// <summary>Opening a Legendary quest, a marker or a quest with coordinates in its notes offers RynthNav's Go to.</summary>
        private static void GoToButtons()
        {
            OracleContext c = On("");
            var navs = new List<string>();
            c.SendNav = line => { navs.Add(line); return true; };
            var printed = new List<string>();
            c.PrintSink = printed.Add;
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;

            // The shipped Legendary notes carry no coordinates, so a row with them is added for the test
            // (first in the list, taken out again): several places give a picker first, picking plans the route.
            var fake = new LegendaryQuest
            {
                Group = "Testing", Name = "Test Route Quest", Luminance = 1, Flags = new[] { "testroutequest" },
                Note = "Speak with Hoshar 59.6S 88.0W - Kill Mukkir 86.5N 57.3E",
            };
            var one = new LegendaryQuest
            {
                Group = "Testing", Name = "Test One Place", Luminance = 1, Flags = new[] { "testoneplace" },
                Note = "Speak with Hoshar 59.6S 88.0W",
            };
            LegendaryList.All.Insert(0, fake);
            try
            {
                view.ShowTrackers(QuestKind.Legendary);
                Frame(host, main, view, "Legendary");
                Click(host, main, "q.r.lg:" + fake.Name);
                string body = Frame(host, main, view, "Legendary opened");
                Check(body.Contains("Go to (2 places in these notes)") && body.Contains("###q.nav.pick") && body.Contains("###q.nav.go"),
                      "a Legendary quest with 2 places in its notes: a picker and Go");
                Check(body.Contains("###lg.sel.print") && body.Contains("###lg.sel.close") && body.Contains("Testing"), "its area, Print and Close buttons");
                Apply(host, Event(3, ++_seq, main.Hash, main.KeyOf("q.nav.pick"), BitConverter.GetBytes(1)));
                Check(navs.Count == 1 && navs[0] == "route 86.5N, 57.3E Kill Mukkir", "picking the second place plans its route: " + string.Join("|", navs));
                Frame(host, main, view, "Legendary, second place picked");
                Click(host, main, "q.nav.go");
                Check(navs.Count == 2 && navs[1] == "go 86.5N, 57.3E Kill Mukkir", "Go walks the picked place: " + string.Join("|", navs));
                Click(host, main, "lg.sel.print");
                Check(printed.Count >= 1 && printed[0].Contains("Test Route Quest") && printed.Any(p => p.Contains("Speak with Hoshar")),
                      "Print in chat prints the quest and its notes: " + string.Join(" | ", printed.Take(3)));

                // One place: no picker.
                Click(host, main, "q.r.lg:" + fake.Name);   // close
                LegendaryList.All.Insert(0, one);
                Type(host, main, "q.search", "Test One");   // the row list is cached per filter: a new search rebuilds it
                Frame(host, main, view, "Legendary, with the one-place row");
                Click(host, main, "q.r.lg:" + one.Name);
                body = Frame(host, main, view, "Legendary, one place");
                Check(body.Contains("Go to") && !body.Contains("###q.nav.pick") && body.Contains("###q.nav.go"), "one place: Go / Route / Arrow, no picker");

                // A real Legendary quest (no coordinates in its notes): opens, no Go to.
                LegendaryQuest real = LegendaryList.All.First(q => q != fake && q != one);
                Click(host, main, "q.r.lg:" + one.Name);   // close
                Type(host, main, "q.search", "");
                Frame(host, main, view, "Legendary, all rows again");
                Click(host, main, "q.r.lg:" + real.Name);
                body = Frame(host, main, view, "Legendary, no place");
                Check(!body.Contains("###q.nav.go") && body.Contains("###lg.sel.close") && body.Contains("Area"), "no coordinates in the notes: no Go to");
            }
            finally
            {
                LegendaryList.All.Remove(fake);
                LegendaryList.All.Remove(one);
            }

            // A plain quest whose notes have coordinates: Go to under its texts, and the click still prints.
            Quest? withCoords = c.Catalog.Quests.FirstOrDefault(q => NavTargets.Stops(q.Info + " - " + q.Hint).Count > 0);
            Check(withCoords != null, "some quest in the list has coordinates in its notes");
            if (withCoords != null)
            {
                view.ShowTrackers(QuestKind.Quest);
                Type(host, main, "q.search", withCoords.Flag);
                Frame(host, main, view, "Quests, searched");
                printed.Clear();
                Click(host, main, "q.r." + withCoords.Flag);
                string body = Frame(host, main, view, "Quest with coordinates");
                Check(printed.Count >= 4, "a click on a quest still prints its texts in chat");
                Check(body.Contains("###q.nav.go") && body.Contains("Save rewards") && body.Contains("###q.sel.track"),
                      "the quest opens with Go to, Track and the rewards editor");
                Type(host, main, "q.search", "");
            }
        }

        /// <summary>The guardians as rows, their controls sending RynthILT's verbs, and the stamps' opened box.</summary>
        private static void Trackers()
        {
            var g = IltGuardianView.Parse(GuardianJson);
            Check(g.Loaded && g.Chat && !g.Auto && g.Buy && g.Answer == "Pyreal" && g.AnswerAgeSec == 30 && g.SyncedUtc.Year == 2026, "guardian JSON header");
            Check(g.Rows.Count == 2 && g.Rows[0].Ready && g.Rows[0].Exact && g.Rows[1].Eta == "in 5h 2m" && !g.Rows[1].Exact, "guardian rows");
            Check(!IltGuardianView.Parse("nope").Loaded && !IltGuardianView.Parse("").Loaded, "bad guardian JSON is not loaded");
            Check(IltGuardianView.SwitchVerb(" Auto ", true) == "auto on" && IltGuardianView.SwitchVerb("buy", false) == "buy off"
                  && IltGuardianView.HandInVerb() == "handin", "guardian verbs");

            OracleContext c = On(ServerTags.InfiniteLeaftide);
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;

            view.ShowTrackers(QuestKind.Guardian);
            string body = Frame(host, main, view, "Guardians without RynthILT");
            Check(body.Contains("RynthILT isn't loaded"), "guardians without RynthILT: says to load it");

            var sent = new List<string>();
            c.GuardianCommand = verb => { sent.Add(verb); return true; };
            c.Guardian = g;
            c.GuardianRevision++;
            body = Frame(host, main, view, "Guardians");
            Check(body.Contains("Temple guardians") && body.Contains("1 of 2 attribute turn-ins ready") && body.Contains("Pyreal")
                  && body.Contains("###gd.handin") && body.Contains("GRD") && body.Contains("Strength (Fiun Strongarm)") && body.Contains("in 5h 2m"),
                  "guardian controls, answer and rows");
            Click(host, main, "gd.handin");
            Tick(host, main, "gd.auto", true);
            Tick(host, main, "gd.buy", false);
            Click(host, main, "gd.refresh");
            Check(string.Join("|", sent) == "handin|auto on|buy off|refresh", "guardian controls send their verbs: " + string.Join("|", sent));

            // Open a guardian row, then every kind at once under the engine's ops limit.
            Click(host, main, "q.r.gd:Strength");
            view.ShowTrackers(null);
            body = Frame(host, main, view, "ILT trackers, a guardian opened", out int ops);
            int rows = LegendaryList.All.Count + MarkerList.All.Count + DynamicStampList.All.Count + 2;
            Check(Shown(body) == rows && body.Contains("Temple guardian: Strength") && body.Contains("###gd.sel.close"), $"every ILT tracker listed ({Shown(body)}), a guardian opened below");
            Check(ops > 40 && ops < 1400, $"ILT Quests with every tracker and a guardian opened: {ops} ops");
            Console.WriteLine($"  InfiniteLeaftide/Quests, trackers only ({rows} rows, guardian opened): {ops} ops, {body.Length} bytes");

            // A stamp: the nav box with Go / Arrow / Chat, RynthNav asked for the route at once.
            var navs = new List<string>();
            c.SendNav = line => { navs.Add(line); return true; };
            var printed = new List<string>();
            c.PrintSink = printed.Add;
            view.ShowTrackers(QuestKind.Dynamic);
            DynamicStamp felscuda = DynamicStampList.All.First(s => s.Npc == "Felscuda");
            Frame(host, main, view, "Stamps");
            Click(host, main, "q.r.dy:" + felscuda.Stamp);
            Check(navs.Count == 1 && navs[0] == "route 8.8S, 53.1E || Folthid Cellar || Felscuda || Facility Hub", "clicking a stamp asks RynthNav for its route: " + string.Join("|", navs));
            body = Frame(host, main, view, "Stamp opened");
            Check(body.Contains("Navigate to Felscuda") && body.Contains("###dy.nav.go") && body.Contains("###dy.nav.arrow") && body.Contains("###dy.nav.chat"), "the stamp's box");
            Click(host, main, "dy.nav.go");
            Check(navs.Count == 2 && navs[1].StartsWith("go 8.8S, 53.1E || "), "Go sends a go with every candidate");
            Click(host, main, "dy.nav.chat");
            Check(printed.Count >= 1 && printed[0].Contains("Felscuda") && printed[0].Contains("missing"), "Chat prints the stamp: " + string.Join(" | ", printed.Take(2)));
        }

        /// <summary>Give Parchment sends /ub give, and is disabled with a tooltip when RynthAi can't take it.</summary>
        private static void GiveParchment()
        {
            OracleContext c = On(ServerTags.InfiniteLeaftide);
            c.ParchmentFinder = () => "Parchment of Learning";
            var ran = new List<string>();
            c.RunCommand = cmd => { ran.Add(cmd); return true; };
            c.SendNav = _ => true;
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;
            DynamicStamp felscuda = DynamicStampList.All.First(s => s.Npc == "Felscuda");
            view.ShowTrackers(QuestKind.Dynamic);
            Frame(host, main, view, "Stamps");
            Click(host, main, "q.r.dy:" + felscuda.Stamp);

            // RynthAi isn't there: no button, a greyed line and the reason.
            c.UbGiveAvailable = () => false;
            string body = Frame(host, main, view, "Give, no RynthAi");
            Check(!body.Contains("###dy.nav.give") && body.Contains("[Give Parchment of Learning]") && body.Contains("Needs RynthAi"),
                  "no RynthAi: Give Parchment is disabled with a tooltip");
            Check(ran.Count == 0, "nothing was sent");

            // RynthAi there: the button sends /ub give 1.
            c.UbGiveAvailable = () => true;
            body = Frame(host, main, view, "Give, RynthAi");
            Check(body.Contains("###dy.nav.give") && body.Contains("Hands one Parchment of Learning to Felscuda"), "RynthAi loaded: the Give button and its tooltip");
            c.NowMs = 50_000;
            Click(host, main, "dy.nav.give");
            Check(ran.Count == 0, "one click doesn't give anything away");
            body = Frame(host, main, view, "Give, asking");
            Check(body.Contains("Give one Parchment of Learning to Felscuda?") && body.Contains("###dy.nav.give.yes") && body.Contains("###dy.nav.give.no"),
                  "it asks, naming the item and the NPC");
            Click(host, main, "dy.nav.give.no");
            body = Frame(host, main, view, "Give, cancelled");
            Check(ran.Count == 0 && body.Contains("###dy.nav.give") && !body.Contains("###dy.nav.give.yes"), "Cancel drops the question, nothing sent");
            Click(host, main, "dy.nav.give");
            Frame(host, main, view, "Give, asking again");
            Click(host, main, "dy.nav.give.yes");
            Check(ran.Count == 1 && ran[0] == "/ub give 1 Parchment of Learning to Felscuda", "Yes sends /ub give 1: " + string.Join("|", ran));
            Check(c.UbGiveSentMs == 50_000, "the send is stamped, so a rejection can be tied to it");

            // The loaded RynthAi answers "Unrecognized /ub command": the button turns itself off.
            Check(!c.NoteChatLineForGive("[RynthAi] some other line", 52_000) && !c.UbGiveMissing, "an unrelated line changes nothing");
            Check(!c.NoteChatLineForGive("[RynthAi] Unrecognized /ub command.", 60_000) && !c.UbGiveMissing, "a rejection long after the send is not tied to it");
            Check(c.NoteChatLineForGive("[RynthAi] Unrecognized /ub command.", 52_000) && c.UbGiveMissing, "a rejection right after the send disables Give");
            body = Frame(host, main, view, "Give, rejected");
            Check(!body.Contains("###dy.nav.give") && body.Contains("[Give Parchment of Learning]") && body.Contains("Unrecognized /ub command"),
                  "rejected: disabled, the tooltip says why");
            Click(host, main, "q.r.dy:" + felscuda.Stamp);   // close
            Click(host, main, "q.r.dy:" + felscuda.Stamp);   // open again
            ran.Clear();
            Frame(host, main, view, "Give, rejected again");
            Check(ran.Count == 0, "no further /ub give goes out");

            // No Parchment carried: said, no button either way.
            c.UbGiveMissing = false;
            c.ParchmentFinder = () => null;
            body = Frame(host, main, view, "Give, none carried");
            Check(body.Contains("No Parchment in your packs") && !body.Contains("###dy.nav.give"), "no Parchment: no button, says so");

            // A name with a control character can't smuggle a second command.
            c.ParchmentFinder = () => "Parchment\nof /quit";
            c.UbGiveAvailable = () => true;
            Frame(host, main, view, "Give, odd name");
            ran.Clear();
            Click(host, main, "dy.nav.give");
            Frame(host, main, view, "Give, odd name asking");
            Click(host, main, "dy.nav.give.yes");
            Check(ran.Count == 1 && !ran[0].Contains('\n') && ran[0].StartsWith("/ub give 1 "), "control characters are stripped from the command: " + (ran.Count > 0 ? ran[0].Replace("\n", "\\n") : ""));
        }

        /// <summary>The throttle line, and the Refresh bar on the Quests page.</summary>
        private static void Throttle()
        {
            OracleContext c = On("");
            var view = new OracleView(c);
            var host = new UiHost();
            UiWindow main = host.Add(new UiWindow("RynthOracle/Oracle", "Oracle") { Visible = true });
            c.Settings.TabName = OracleView.TabQuests;
            c.Flags.Request(_ => true, c.NowMs);
            c.Flags.OnChatLine("[MyQuests] This command may only be run once every 60 seconds.", c.NowMs + 100);
            string body = Frame(host, main, view, "Quests, throttled");
            Check(body.Contains("The server answers one /myquests a minute: Refresh works again in 6"), "the throttle line says when Refresh works again");
            Click(host, main, "refresh.Quests");
            Check(Frame(host, main, view, "Quests, Refresh while throttled").Contains("try again in 6"), "Refresh while the server's minute runs says to wait, and sends nothing");
            c.NowMs += 61_000;
            Check(!Frame(host, main, view, "Quests, throttle over").Contains("Refresh works again in"), "the countdown line goes once the minute is up");
        }
    }
}
