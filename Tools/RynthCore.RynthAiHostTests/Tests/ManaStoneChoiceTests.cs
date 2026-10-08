using RynthCore.Loot;
using RynthCore.Plugin.RynthAi;
using RynthCore.Plugin.RynthAi.LegacyUi;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthCore.RynthAiHostTests.Tests;

// 2026-10-08: RynthAi used an Aligned Mana Stone on a wand. Only the ordinary tapping stones
// (and mana charges) are stones to RynthAi unless a ManaStone consumable rule names one.
internal static class ManaStoneChoiceTests
{
    public static void Register(Runner r)
    {
        r.Add("mana stones: only ordinary stones and charges are used; special stones only when a rule names them", OrdinaryOnly);
        r.Add("mana stones: with tapping off, charged stones and Mana Charges still refill worn gear", RefillWithoutTapping);
    }

    private static void OrdinaryOnly()
    {
        FakeHost.Reset();
        var host = FakeHost.Create();
        var cache = new WorldObjectCache(host);
        var s = new LegacyUiSettings { EnableManaTapping = true };
        var m = new ManaStoneManager(host, s, cache);
        WorldObject W(string name) => new(0x60001000, name, AcObjectClass.ManaStone);

        foreach (string n in new[] { "Minor Mana Stone", "Lesser Mana Stone", "Moderate Mana Stone", "Mana Stone", "Greater Mana Stone", "Major Mana Stone", "Titan Mana Charge" })
            Check.True(m.IsManaStone(W(n)), $"'{n}' is a stone");
        foreach (string n in new[] { "Aligned Mana Stone", "Aetheria Mana Stone", "Unstable Mana Stone", "Enchanted Mana Stone", "Adjanite Mana Stone" })
            Check.False(m.IsManaStone(W(n)), $"'{n}' is left alone");

        s.ConsumableRules.Add(new ConsumableRule { Name = "Aligned Mana Stone", Type = "ManaStone" });
        Check.True(m.IsManaStone(W("Aligned Mana Stone")), "a rule naming it opts it in");
        Check.False(m.IsManaStone(W("Mana Stone")), "with rules, only the named stones");
    }

    private static void RefillWithoutTapping()
    {
        FakeHost.Reset();
        var host = FakeHost.Create();
        var cache = new WorldObjectCache(host);
        var s = new LegacyUiSettings { EnableManaTapping = false };
        var m = new ManaStoneManager(host, s, cache);
        WorldObject W(string name) => new(0x60001000, name, AcObjectClass.ManaStone);

        Check.False(m.IsManaStone(W("Major Mana Stone")), "tapping off: not a stone to loot or drain into");
        Check.True(m.IsRefillStone(W("Major Mana Stone")), "but a charged one still refills gear");
        Check.True(m.IsRefillStone(W("Titan Mana Charge")), "a bought Mana Charge too");
        Check.False(m.IsRefillStone(W("Aligned Mana Stone")), "never a special stone");

        s.ConsumableRules.Add(new ConsumableRule { Name = "Aligned Mana Stone", Type = "ManaStone" });
        Check.True(m.IsRefillStone(W("Aligned Mana Stone")), "unless a rule names it");
        Check.False(m.IsRefillStone(W("Major Mana Stone")), "with rules, only the named stones");
    }
}
