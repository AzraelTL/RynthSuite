using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RynthCore.Loot.Editing;
using RynthCore.Plugin.RynthAi.CreatureData;

namespace RynthCore.Plugin.RynthAi.ItemInfo;

/// <summary>One item's appraisal values, as <see cref="ItemInfoText"/> reads them.</summary>
internal interface IItemInfoSource
{
    /// <summary>The name the client shows ("" when unknown).</summary>
    string Name { get; }
    bool TryInt(uint key, out int value);
    bool TryFloat(uint key, out double value);
    /// <summary>The item's spells (empty when none or not appraised).</summary>
    IReadOnlyList<int> SpellIds { get; }
}

/// <summary>
/// /ra iteminfo's text: one item on one chat line, in the spirit of Mag-Tools' item info
/// (name, type, set, armor, ratings, damage and mods, imbues, slayer, tinks, spells by name,
/// wield and activation requirements, craft, material, value and burden). A field the
/// appraisal doesn't carry is left out. Pure: no host calls, so the host tests feed it a
/// fake source.
/// </summary>
internal static class ItemInfoText
{
    // STypeInt (AC / ACE PropertyInt numbering; the names in Meta/PropertyNames.cs).
    internal const uint IntItemType = 1, IntEncumbrance = 5, IntStackSize = 12, IntRareId = 17, IntValue = 19,
        IntArmorLevel = 28, IntBonded = 33, IntDamage = 44, IntDamageType = 45, IntWeaponSkill = 48,
        IntWeaponTime = 49, IntWorkmanship = 105, IntSpellcraft = 106, IntCurMana = 107, IntMaxMana = 108,
        IntItemDifficulty = 109, IntAllegianceRankLimit = 110, IntAttuned = 114, IntSkillLevelLimit = 115,
        IntMaterial = 131, IntWieldReq = 158, IntWieldSkill = 159, IntWieldDiff = 160, IntSlayerType = 166,
        IntTinks = 171, IntImbued = 179, IntElementalBonus = 204, IntSetId = 265, IntWieldReq2 = 270,
        IntWieldSkill2 = 271, IntWieldDiff2 = 272, IntItemMaxLevel = 319;

    // STypeFloat (ACE PropertyFloat numbering).
    internal const uint FloatDamageVariance = 22, FloatMeleeDefense = 29, FloatWeaponOffense = 62, FloatDamageMod = 63,
        FloatManaConversion = 144, FloatMissileDefense = 149, FloatMagicDefense = 150, FloatElementalDamage = 152;

    /// <summary>Gear ratings (STypeInt 370-389) and their short tags, in print order.</summary>
    internal static readonly (uint Key, string Tag)[] Ratings =
    {
        (370, "D"), (371, "DR"), (372, "C"), (373, "CR"), (374, "CD"), (375, "CDR"),
        (376, "HB"), (379, "Vit"), (377, "NR"), (378, "LR"), (388, "OP"), (389, "OPR"),
        (383, "PvP D"), (384, "PvP DR"),
    };

    // ItemType flags (STypeInt 1), most specific first.
    private static readonly (uint Flag, string Name)[] Types =
    {
        (0x00040000, "Promissory Note"), (0x00080000, "Mana Stone"), (0x00001000, "Spell Component"),
        (0x00000800, "Gem"), (0x00004000, "Key"), (0x40000000, "Salvage"), (0x20000000, "Tinkering Tool"),
        (0x00000200, "Container"), (0x00008000, "Caster"), (0x00000100, "Missile Weapon"),
        (0x00000001, "Melee Weapon"), (0x00000002, "Armor"), (0x00000004, "Clothing"), (0x00000008, "Jewelry"),
        (0x00000020, "Food"), (0x00000040, "Money"), (0x00002000, "Writable"), (0x00000080, "Misc"),
    };

    // ImbuedEffect flags (STypeInt 179).
    private static readonly (uint Flag, string Name)[] Imbues =
    {
        (0x0001, "Critical Strike"), (0x0002, "Crippling Blow"), (0x0004, "Armor Rending"),
        (0x0008, "Slash Rending"), (0x0010, "Pierce Rending"), (0x0020, "Bludgeon Rending"),
        (0x0040, "Acid Rending"), (0x0080, "Cold Rending"), (0x0100, "Lightning Rending"),
        (0x0200, "Fire Rending"), (0x0400, "Melee Defense"), (0x0800, "Missile Defense"),
        (0x1000, "Magic Defense"), (0x4000, "Nether Rending"),
    };

    // Equipment sets (STypeInt 265) whose ids are certain: the loot armor sets and Aetheria.
    private static readonly Dictionary<int, string> Sets = new()
    {
        [13] = "Soldier's", [14] = "Adept's", [15] = "Archer's", [16] = "Defender's", [17] = "Tinker's",
        [18] = "Crafter's", [19] = "Hearty", [20] = "Dexterous", [21] = "Wise", [22] = "Swift",
        [23] = "Hardened", [24] = "Reinforced", [25] = "Interlocking", [26] = "Flame Proof",
        [27] = "Acid Proof", [28] = "Cold Proof", [29] = "Lightning Proof",
        [35] = "Aetheria: Defense", [36] = "Aetheria: Destruction", [37] = "Aetheria: Fury",
        [38] = "Aetheria: Growth", [39] = "Aetheria: Vigor",
    };

    // Attributes and vitals for attribute/vital wield requirements (ACE PropertyAttribute / PropertyAttribute2nd).
    private static readonly string[] Attributes = { "", "Strength", "Endurance", "Quickness", "Coordination", "Focus", "Self" };
    private static readonly string[] Vitals = { "", "Max Health", "Health", "Max Stamina", "Stamina", "Max Mana", "Mana" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// The item on one line. <paramref name="spellName"/> names a spell id (the plugin's
    /// spell table). <paramref name="identified"/> false adds a note that the server never
    /// answered the ID, so only what the client already knew is shown.
    /// </summary>
    public static string Describe(IItemInfoSource item, Func<int, string> spellName, bool identified = true)
    {
        var parts = new List<string>(24);
        int type = Int(item, IntItemType);

        // Name, with the material in front the way the game names loot ("Gold Ring").
        string name = item.Name.Trim();
        int material = Int(item, IntMaterial);
        string full = WeaponNames.Full(name, material);
        if (full.Length == 0) full = "(unnamed item)";
        string typeName = TypeName(type);
        int stack = Int(item, IntStackSize);
        var head = new StringBuilder(full);
        if (stack > 1) head.Append(" x").Append(stack.ToString("N0", Inv));
        if (typeName.Length > 0) head.Append(" (").Append(typeName).Append(')');
        parts.Add(head.ToString());

        if (Int(item, IntSetId) is int set and > 0)
            parts.Add((Sets.TryGetValue(set, out string? setName) ? setName : "Set " + set) + " Set");

        if (item.TryInt(IntArmorLevel, out int al) && (al > 0 || (type & 0x2) != 0))
            parts.Add("AL " + al.ToString(Inv));

        string ratings = RatingText(item);
        if (ratings.Length > 0) parts.Add(ratings);

        AddWeapon(item, type, parts);

        string imbues = ImbueText(Int(item, IntImbued));
        if (imbues.Length > 0) parts.Add(imbues);

        if (Int(item, IntSlayerType) is int slayer and > 0)
            parts.Add(CreatureTypeNames.Name(slayer) + " Slayer");

        if (item.TryInt(IntTinks, out int tinks) && tinks > 0)
            parts.Add("Tinks " + tinks.ToString(Inv));

        string spells = SpellText(item.SpellIds, spellName);
        if (spells.Length > 0) parts.Add(spells);

        string wield = WieldText(item, IntWieldReq, IntWieldSkill, IntWieldDiff);
        if (wield.Length > 0) parts.Add(wield);
        string wield2 = WieldText(item, IntWieldReq2, IntWieldSkill2, IntWieldDiff2);
        if (wield2.Length > 0) parts.Add(wield2);

        if (Int(item, IntSkillLevelLimit) is int skillLimit and > 0) parts.Add("Skill " + skillLimit.ToString(Inv) + " to activate");
        if (Int(item, IntAllegianceRankLimit) is int rank and > 0) parts.Add("Rank " + rank.ToString(Inv));
        if (Int(item, IntItemDifficulty) is int diff and > 0) parts.Add("Diff " + diff.ToString(Inv));
        if (Int(item, IntSpellcraft) is int spellcraft and > 0) parts.Add("Spellcraft " + spellcraft.ToString(Inv));
        if (item.TryInt(IntMaxMana, out int maxMana) && maxMana > 0)
            parts.Add("Mana " + (item.TryInt(IntCurMana, out int cur) ? cur.ToString(Inv) + "/" : "") + maxMana.ToString(Inv));
        if (Int(item, IntItemMaxLevel) is int maxLevel and > 0) parts.Add("Max Level " + maxLevel.ToString(Inv));

        if (item.TryInt(IntWorkmanship, out int craft) && craft > 0) parts.Add("Craft " + craft.ToString(Inv));
        string matName = WeaponNames.Material(material);
        if (matName.Length > 0) parts.Add("Mat " + matName);

        if (Int(item, IntBonded) == 1) parts.Add("Bonded");
        if (Int(item, IntAttuned) == 1) parts.Add("Attuned");
        if (Int(item, IntRareId) is int rare and > 0) parts.Add("Rare #" + rare.ToString(Inv));

        if (item.TryInt(IntValue, out int value)) parts.Add("Value " + value.ToString("N0", Inv));
        if (item.TryInt(IntEncumbrance, out int burden)) parts.Add("BU " + burden.ToString("N0", Inv));

        if (!identified) parts.Add("(not identified: no reply from the server)");
        return string.Join(", ", parts);
    }

    private static int Int(IItemInfoSource item, uint key) => item.TryInt(key, out int v) ? v : 0;

    /// <summary>The item's kind from its ItemType flags ("" when none is set).</summary>
    internal static string TypeName(int itemType)
    {
        uint t = unchecked((uint)itemType);
        foreach (var (flag, n) in Types)
            if ((t & flag) != 0) return n;
        return "";
    }

    /// <summary>"[D3 CD2 CDR1]" for the gear ratings that are set; "" for none.</summary>
    internal static string RatingText(IItemInfoSource item)
    {
        var sb = new StringBuilder();
        foreach (var (key, tag) in Ratings)
        {
            if (!item.TryInt(key, out int r) || r <= 0) continue;
            sb.Append(sb.Length == 0 ? "[" : " ").Append(tag).Append(r.ToString(Inv));
        }
        return sb.Length == 0 ? "" : sb.Append(']').ToString();
    }

    private static void AddWeapon(IItemInfoSource item, int type, List<string> parts)
    {
        uint t = unchecked((uint)type);
        bool melee = (t & 0x1) != 0, missile = (t & 0x100) != 0, caster = (t & 0x8000) != 0;
        bool weapon = melee || missile || caster;

        if (Int(item, IntWeaponSkill) is int skill and > 0 && weapon)
            parts.Add(SkillName(skill));

        int dmgType = Int(item, IntDamageType);
        if (item.TryInt(IntDamage, out int max) && max > 0)
        {
            string dmg = "Dmg " + max.ToString(Inv);
            if (item.TryFloat(FloatDamageVariance, out double variance) && variance > 0 && variance < 1)
            {
                int min = (int)Math.Round(max * (1.0 - variance), MidpointRounding.AwayFromZero);
                dmg = $"Dmg {min.ToString(Inv)}-{max.ToString(Inv)}";
            }
            string element = DamageTypeName(dmgType);
            if (element.Length > 0) dmg += " " + element;
            parts.Add(dmg);
        }
        else if (weapon && dmgType > 0)
        {
            string element = DamageTypeName(dmgType);
            if (element.Length > 0) parts.Add(element);
        }
        if (Int(item, IntElementalBonus) is int elemBonus and > 0) parts.Add("+" + elemBonus.ToString(Inv) + " elemental dmg");
        if (weapon && item.TryInt(IntWeaponTime, out int speed) && speed > 0) parts.Add("Speed " + speed.ToString(Inv));

        // Modifiers: 1.18 = +18%. Missile launchers' damage mod and casters' elemental mod too.
        var mods = new List<string>(6);
        AddPercent(item, FloatWeaponOffense, "atk", mods, weapon);
        if (missile) AddPercent(item, FloatDamageMod, "dmg", mods, true);
        if (caster) AddPercent(item, FloatElementalDamage, "vs monsters", mods, true);
        AddPercent(item, FloatMeleeDefense, "md", mods, weapon);
        AddPercent(item, FloatMissileDefense, "mid", mods, weapon);
        AddPercent(item, FloatMagicDefense, "mgd", mods, weapon);
        AddPercent(item, FloatManaConversion, "mana c", mods, true);
        if (mods.Count > 0) parts.Add("(" + string.Join(", ", mods) + ")");
    }

    private static void AddPercent(IItemInfoSource item, uint key, string label, List<string> mods, bool allowed)
    {
        if (!allowed || !item.TryFloat(key, out double v) || double.IsNaN(v)) return;
        // Multipliers (attack, defense, damage) are stored as 1.xx; mana conversion as 0.xx.
        double pct = key == FloatManaConversion ? v * 100.0 : (v - 1.0) * 100.0;
        int rounded = (int)Math.Round(pct, MidpointRounding.AwayFromZero);
        if (rounded == 0) return;
        mods.Add((rounded > 0 ? "+" : "") + rounded.ToString(Inv) + "% " + label);
    }

    /// <summary>"Crippling Blow, Fire Rending" for the imbue flags; "" for none.</summary>
    internal static string ImbueText(int flags)
    {
        if (flags == 0) return "";
        uint f = unchecked((uint)flags);
        var names = new List<string>(2);
        foreach (var (flag, n) in Imbues)
            if ((f & flag) != 0) names.Add(n);
        return string.Join(", ", names);
    }

    /// <summary>"Spells: A, B, C" by name, in the item's order, each once; "" for none.</summary>
    internal static string SpellText(IReadOnlyList<int> spellIds, Func<int, string> spellName)
    {
        if (spellIds.Count == 0) return "";
        var seen = new HashSet<int>();
        var names = new List<string>(spellIds.Count);
        foreach (int id in spellIds)
        {
            if (id <= 0 || !seen.Add(id)) continue;
            string n = spellName(id);
            names.Add(string.IsNullOrWhiteSpace(n) ? "spell " + id.ToString(Inv) : n);
        }
        return names.Count == 0 ? "" : "Spells: " + string.Join(", ", names);
    }

    /// <summary>One wield requirement ("Wield Lvl 180", "Heavy Weapons 375 to wield"); "" for none.</summary>
    internal static string WieldText(IItemInfoSource item, uint reqKey, uint skillKey, uint diffKey)
    {
        int req = Int(item, reqKey);
        if (req <= 0) return "";
        int skill = Int(item, skillKey);
        int diff = Int(item, diffKey);
        string d = diff.ToString(Inv);
        return req switch
        {
            1 => $"{SkillName(skill)} {d} to wield",
            2 => $"Base {SkillName(skill)} {d} to wield",
            3 => $"{Pick(Attributes, skill, "Attribute")} {d} to wield",
            4 => $"Base {Pick(Attributes, skill, "Attribute")} {d} to wield",
            5 => $"{Pick(Vitals, skill, "Vital")} {d} to wield",
            6 => $"Base {Pick(Vitals, skill, "Vital")} {d} to wield",
            7 => $"Wield Lvl {d}",
            8 => $"{(diff >= 3 ? "Specialized" : "Trained")} {SkillName(skill)} to wield",
            12 => $"Heritage {d} to wield",
            _ => $"Wield requirement {req.ToString(Inv)} ({skill.ToString(Inv)}, {d})",
        };
    }

    private static string Pick(string[] names, int i, string fallback) =>
        i > 0 && i < names.Length ? names[i] : $"{fallback} {i.ToString(Inv)}";

    /// <summary>AC's skill name for a skill id ("skill N" when unknown).</summary>
    internal static string SkillName(int id)
    {
        foreach (var (sid, n) in LootRuleText.Skills)
            if (sid == id) return n;
        return "skill " + id.ToString(Inv);
    }

    /// <summary>The damage type's name ("Slashing/Piercing" etc.); "" for none.</summary>
    internal static string DamageTypeName(int flags)
    {
        if (flags <= 0) return "";
        foreach (var (id, n) in LootRuleText.DamageTypes)
            if (id == flags) return n;
        // Other combinations: the single flags joined.
        var names = new List<string>(3);
        foreach (var (id, n) in LootRuleText.DamageTypes)
            if (id > 0 && (id & (id - 1)) == 0 && (flags & id) != 0) names.Add(n);
        return string.Join("/", names);
    }
}
