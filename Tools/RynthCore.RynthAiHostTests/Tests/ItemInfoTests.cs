using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RynthCore.Plugin.RynthAi;
using RynthCore.Plugin.RynthAi.ItemInfo;
using RynthCore.Plugin.RynthAi.LegacyUi;
using RynthCore.PluginCore;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthCore.RynthAiHostTests.Tests;

// /ra iteminfo (Silentkelpie's rynth PR #1 idea, re-implemented): a Mag-Tools style one-line
// description of the selected or named item, identified first when the client hasn't
// appraised it. The formatter is checked on fake appraisal data; the command end to end on
// FakeHost (selection, ID request, the print when the ID lands, the timeout, name lookup).
internal static class ItemInfoTests
{
    private const uint Player = 0x50000F21;
    private const uint Sword = 0x80300001, Ring = 0x80300002, Scarab = 0x80300003, Scarab2 = 0x80300004;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    public static void Register(Runner r)
    {
        r.Add("iteminfo: a weapon's damage, mods, imbue, slayer, spells and wield line", WeaponLine);
        r.Add("iteminfo: armor's set, AL, ratings, tinks and wield level", ArmorLine);
        r.Add("iteminfo: jewelry with mana, diff and craft; a bare item stays short", JewelryAndBareItem);
        r.Add("iteminfo: a selected item already appraised prints at once, no ID", SelectedAppraised);
        r.Add("iteminfo: an unappraised item is identified, then printed when the ID lands", IdentifiesThenPrints);
        r.Add("iteminfo: no reply prints what the client has after the timeout", TimeoutPrints);
        r.Add("iteminfo: a name picks the inventory item, exact before partial", NameLookup);
        r.Add("iteminfo: nothing selected or no such item says so", NothingSelected);
        r.Add("iteminfo on select: the player's pick prints quietly; the bot's own picks and the setting off do not", OnSelect);
    }

    // ── Fake appraisal data ─────────────────────────────────────────────

    private sealed class Fake : IItemInfoSource
    {
        public string Name { get; init; } = "";
        public Dictionary<uint, int> Ints { get; } = new();
        public Dictionary<uint, double> Floats { get; } = new();
        public List<int> Spells { get; } = new();
        public IReadOnlyList<int> SpellIds => Spells;
        public bool TryInt(uint key, out int value) => Ints.TryGetValue(key, out value);
        public bool TryFloat(uint key, out double value) => Floats.TryGetValue(key, out value);
    }

    private static string Spell(int id) => SpellDatabase.GetSpellName(id);

    /// <summary>Each part appears, in this order.</summary>
    private static void InOrder(string text, string[] parts, string what)
    {
        int at = 0;
        foreach (string p in parts)
        {
            int i = text.IndexOf(p, at, StringComparison.Ordinal);
            Check.True(i >= 0, $"{what}: has \"{p}\" after position {at} in: {text}");
            if (i >= 0) at = i + p.Length;
        }
    }

    private static void WeaponLine()
    {
        var w = new Fake { Name = "Ornate Katar" };
        w.Ints[ItemInfoText.IntItemType] = 0x1;
        w.Ints[ItemInfoText.IntMaterial] = 61;            // Iron
        w.Ints[ItemInfoText.IntWeaponSkill] = 44;         // Heavy Weapons
        w.Ints[ItemInfoText.IntDamage] = 77;
        w.Floats[ItemInfoText.FloatDamageVariance] = 0.4;
        w.Ints[ItemInfoText.IntDamageType] = 16;          // Fire
        w.Ints[ItemInfoText.IntWeaponTime] = 30;
        w.Floats[ItemInfoText.FloatWeaponOffense] = 1.18;
        w.Floats[ItemInfoText.FloatMeleeDefense] = 1.20;
        w.Floats[ItemInfoText.FloatMagicDefense] = 1.0;   // +0%: left out
        w.Ints[ItemInfoText.IntImbued] = 0x2 | 0x200;     // Crippling Blow, Fire Rending
        w.Ints[ItemInfoText.IntSlayerType] = 14;          // Undead
        w.Ints[ItemInfoText.IntTinks] = 2;
        w.Spells.AddRange(new[] { 2531, 6107, 2531 });    // a repeat is named once
        w.Ints[ItemInfoText.IntWieldReq] = 1;
        w.Ints[ItemInfoText.IntWieldSkill] = 44;
        w.Ints[ItemInfoText.IntWieldDiff] = 375;
        w.Ints[ItemInfoText.IntWieldReq2] = 7;
        w.Ints[ItemInfoText.IntWieldDiff2] = 150;
        w.Ints[ItemInfoText.IntItemDifficulty] = 300;
        w.Ints[ItemInfoText.IntWorkmanship] = 9;
        w.Ints[ItemInfoText.IntValue] = 12345;
        w.Ints[ItemInfoText.IntEncumbrance] = 350;
        w.Ints[ItemInfoText.Ratings[0].Key] = 2;          // D2

        string s = ItemInfoText.Describe(w, Spell);
        InOrder(s, new[]
        {
            "Iron Ornate Katar (Melee Weapon)", "[D2]", "Heavy Weapons", "Dmg 46-77 Fire", "Speed 30",
            "(+18% atk, +20% md)", "Crippling Blow, Fire Rending", "Undead Slayer", "Tinks 2",
            "Spells: Major Heavy Weapon Aptitude, Legendary Strength", "Heavy Weapons 375 to wield",
            "Wield Lvl 150", "Diff 300", "Craft 9", "Mat Iron", "Value 12,345", "BU 350",
        }, "weapon");
        Check.False(s.Contains("mgd"), "a +0% mod is left out");
        Check.Eq(s.Split("Major Heavy Weapon Aptitude").Length, 2, "each spell named once");
        Check.False(s.Contains("not identified"), "identified: no note");
    }

    private static void ArmorLine()
    {
        var a = new Fake { Name = "Chainmail Tassets" };
        a.Ints[ItemInfoText.IntItemType] = 0x2;
        a.Ints[ItemInfoText.IntSetId] = 16;               // Defender's
        a.Ints[ItemInfoText.IntArmorLevel] = 584;
        a.Ints[371] = 1;                                  // DR1
        a.Ints[375] = 2;                                  // CDR2
        a.Ints[ItemInfoText.IntTinks] = 3;
        a.Ints[ItemInfoText.IntWieldReq] = 7;
        a.Ints[ItemInfoText.IntWieldDiff] = 180;
        a.Ints[ItemInfoText.IntMaterial] = 64;            // Steel
        string s = ItemInfoText.Describe(a, Spell);
        InOrder(s, new[] { "Steel Chainmail Tassets (Armor)", "Defender's Set", "AL 584", "[DR1 CDR2]", "Tinks 3", "Wield Lvl 180", "Mat Steel" }, "armor");
        Check.False(s.Contains("Dmg"), "armor shows no damage");
        Check.False(s.Contains("Speed"), "or speed");
    }

    private static void JewelryAndBareItem()
    {
        var j = new Fake { Name = "Gold Ring" };
        j.Ints[ItemInfoText.IntItemType] = 0x8;
        j.Ints[ItemInfoText.IntMaterial] = 60;            // Gold: already in the name
        j.Ints[ItemInfoText.IntCurMana] = 1200;
        j.Ints[ItemInfoText.IntMaxMana] = 1500;
        j.Ints[ItemInfoText.IntItemDifficulty] = 285;
        j.Ints[ItemInfoText.IntWorkmanship] = 7;
        j.Ints[ItemInfoText.IntAttuned] = 1;
        j.Spells.Add(3964);
        string s = ItemInfoText.Describe(j, Spell);
        InOrder(s, new[] { "Gold Ring (Jewelry)", "Spells: Epic Focus", "Diff 285", "Mana 1200/1500", "Craft 7", "Mat Gold", "Attuned" }, "jewelry");
        Check.False(s.Contains("Gold Gold"), "the material isn't doubled");
        Check.False(s.Contains("AL "), "jewelry with no armor shows no AL");

        var bare = new Fake { Name = "Mana Scarab" };
        bare.Ints[ItemInfoText.IntStackSize] = 25;
        string b = ItemInfoText.Describe(bare, Spell, identified: false);
        Check.True(b.StartsWith("Mana Scarab x25", StringComparison.Ordinal), $"a stack shows its size: {b}");
        Check.True(b.EndsWith("(not identified: no reply from the server)", StringComparison.Ordinal), "unidentified note last");
        Check.Eq(b.Split(", ").Length, 2, "nothing else is made up");
    }

    // ── The command on FakeHost ─────────────────────────────────────────

    private sealed class World
    {
        public readonly RynthAiPlugin P = new();
        public readonly WorldObjectCache Cache;
        public long Now = 1_000_000;

        public World()
        {
            FakeHost.Reset();
            FakeHost.Names[Player] = "Reader";
            FakeHost.PlayerIdValue = Player;
            FakeHost.Positions[Player] = (0x1048002Au, 100f, 100f, 0f);
            FakeHost.PlayerPose = (0x1048002Au, 100f, 100f, 0f);
            Item(Sword, "Ornate Katar", 0x1);
            Item(Ring, "Gold Ring", 0x8);
            Item(Scarab, "Mana Scarab", 0x80);
            Item(Scarab2, "Platinum Scarab", 0x80);
            FakeHost.Ints[(Sword, ItemInfoText.IntDamage)] = 77;
            FakeHost.Ints[(Sword, ItemInfoText.IntWorkmanship)] = 9;
            FakeHost.Ints[(Ring, ItemInfoText.IntWorkmanship)] = 7;
            FakeHost.SpellIds[Ring] = new uint[] { 3964 };
            FakeHost.WeaponCalls = true;   // RequestId + HasAppraisalData
            var host = FakeHost.Create(playerId: Player);
            typeof(RynthPluginBase).GetMethod("Attach", Any)!.Invoke(P, new object[] { FakeHost.LastApi });
            var dash = (LegacyDashboardRenderer)RuntimeHelpers.GetUninitializedObject(typeof(LegacyDashboardRenderer));
            typeof(LegacyDashboardRenderer).GetField("_settings", Any)!.SetValue(dash, new LegacyUiSettings());
            Set("_dashboard", dash);
            Cache = new WorldObjectCache(host);
            Cache.SetPlayerId(Player);
            Set("_objectCache", Cache);
            Set("_playerId", Player);
            Set("_initialized", true);
            Set("_loginComplete", true);
            foreach (uint id in new[] { Sword, Ring, Scarab, Scarab2 }) Cache.OnCreateObject(id);
            Cache.Tick();
            RynthAiPlugin.ItemInfoClock = () => Now;
        }

        private static void Item(uint id, string name, uint type)
        {
            FakeHost.Names[id] = name;
            FakeHost.Containers[id] = Player;
            FakeHost.ItemTypes[id] = type;
            FakeHost.Ints[(id, ItemInfoText.IntItemType)] = (int)type;
        }

        public void Set(string f, object? v) => typeof(RynthAiPlugin).GetField(f, Any)!.SetValue(P, v);

        public void Command(string text)
        {
            string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            typeof(RynthAiPlugin).GetMethod("HandleItemInfoCommand", Any)!.Invoke(P, new object[] { parts });
        }

        public void Tick() => typeof(RynthAiPlugin).GetMethod("TickItemInfo", Any)!.Invoke(P, null);

        public string[] InfoLines => FakeHost.Chat.Where(c => c.StartsWith("[RynthAi] ", StringComparison.Ordinal)).ToArray();
    }

    private static void Done() => RynthAiPlugin.ItemInfoClock = () => Environment.TickCount64;

    private static void OnSelect()
    {
        var w = new World();
        try
        {
            void Select(uint id) => typeof(RynthAiPlugin).GetMethod("QueueItemInfoOnSelect", Any)!
                .Invoke(w.P, new object?[] { id, w.Cache[unchecked((int)id)] });

            FakeHost.Appraised.Add(Ring);
            Select(Ring);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 1, "the player's pick prints once");
            Check.True(w.InfoLines[0].Contains("Gold Ring (Jewelry)"), $"the ring's line: {w.InfoLines.FirstOrDefault()}");

            Select(Sword);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 1, "unappraised: no 'identifying' chatter on select");
            Check.True(FakeHost.IdRequests.Contains(Sword), "but the ID is asked for");
            FakeHost.Appraised.Add(Sword);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "printed when the appraisal lands");

            SelfSelect.Mark(Ring);
            Select(Ring);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "an item RynthAi selected itself (the looter, a meta) prints nothing");

            var dash = (LegacyDashboardRenderer)typeof(RynthAiPlugin).GetField("_dashboard", Any)!.GetValue(w.P)!;
            dash.Settings.ItemInfoOnSelect = false;
            Select(Scarab);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "setting off: nothing");
        }
        finally { Done(); }
    }

    private static void SelectedAppraised()
    {
        var w = new World();
        try
        {
            FakeHost.SelectedItem = Ring;
            FakeHost.Appraised.Add(Ring);
            w.Command("/ra iteminfo");
            Check.Eq(FakeHost.Chat.Count, 0, "the command only queues (the tick prints)");
            w.Tick();
            Check.Eq(FakeHost.IdRequests.Count, 0, "appraised: no ID request");
            Check.Eq(w.InfoLines.Length, 1, "one line");
            Check.True(w.InfoLines[0].Contains("Gold Ring (Jewelry)") && w.InfoLines[0].Contains("Spells: Epic Focus") && w.InfoLines[0].Contains("Craft 7"),
                $"the ring's line: {w.InfoLines.FirstOrDefault()}");
        }
        finally { Done(); }
    }

    private static void IdentifiesThenPrints()
    {
        var w = new World();
        try
        {
            FakeHost.SelectedItem = Sword;
            w.Command("/ra iteminfo");
            w.Tick();
            Check.True(FakeHost.IdRequests.SequenceEqual(new[] { Sword }), "one ID request for the sword");
            Check.Eq(w.InfoLines.Length, 1, "just the 'identifying' line so far");
            Check.True(w.InfoLines[0].Contains("identifying Ornate Katar"), "says it is identifying");
            w.Now += 1000;
            w.Tick();
            Check.Eq(w.InfoLines.Length, 1, "still waiting a second later");
            FakeHost.Appraised.Add(Sword);
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "printed when the appraisal arrives");
            Check.True(w.InfoLines[1].Contains("Ornate Katar (Melee Weapon)") && w.InfoLines[1].Contains("Dmg 77") && !w.InfoLines[1].Contains("not identified"),
                $"the sword's line: {w.InfoLines[1]}");
            w.Now += 10_000;
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "printed once");
            Check.Eq(FakeHost.IdRequests.Count, 1, "never asked twice");
        }
        finally { Done(); }
    }

    private static void TimeoutPrints()
    {
        var w = new World();
        try
        {
            FakeHost.SelectedItem = Sword;
            w.Command("/ra iteminfo");
            w.Tick();
            w.Now += RynthAiPlugin.ItemInfoTimeoutMs - 1;
            w.Tick();
            Check.Eq(w.InfoLines.Length, 1, "not before the timeout");
            w.Now += 1;
            w.Tick();
            Check.Eq(w.InfoLines.Length, 2, "printed at the timeout");
            Check.True(w.InfoLines[1].Contains("Ornate Katar") && w.InfoLines[1].EndsWith("(not identified: no reply from the server)", StringComparison.Ordinal),
                $"with what the client has and the note: {w.InfoLines[1]}");
        }
        finally { Done(); }
    }

    private static void NameLookup()
    {
        var w = new World();
        try
        {
            FakeHost.Appraised.Add(Scarab);
            FakeHost.Appraised.Add(Scarab2);
            FakeHost.Appraised.Add(Ring);
            w.Command("/ra iteminfo mana scarab");
            w.Tick();
            Check.True(w.InfoLines.Last().StartsWith("[RynthAi] Mana Scarab", StringComparison.Ordinal), $"exact name, any case: {w.InfoLines.Last()}");
            w.Command("/ra iteminfo Scarab");
            w.Tick();
            Check.True(w.InfoLines.Last().Contains("Scarab") && !w.InfoLines.Last().Contains("not in your"), "a partial name finds a scarab");
            w.Command("/ra iteminfo ring");
            w.Tick();
            Check.True(w.InfoLines.Last().StartsWith("[RynthAi] Gold Ring", StringComparison.Ordinal), "partial: Gold Ring");
            Check.Eq(FakeHost.SelectedItem, 0u, "a named lookup doesn't need a selection");
        }
        finally { Done(); }
    }

    private static void NothingSelected()
    {
        var w = new World();
        try
        {
            w.Command("/ra iteminfo");
            w.Tick();
            Check.True(w.InfoLines.Length == 1 && w.InfoLines[0].Contains("nothing selected"), "nothing selected says so");
            w.Command("/ra iteminfo Shield of Nowhere");
            w.Tick();
            Check.True(w.InfoLines.Last().Contains("no \"Shield of Nowhere\" in your inventory"), $"unknown name says so: {w.InfoLines.Last()}");
            Check.Eq(FakeHost.IdRequests.Count, 0, "and nothing is identified");
        }
        finally { Done(); }
    }
}
