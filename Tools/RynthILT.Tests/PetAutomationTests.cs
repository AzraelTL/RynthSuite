using System;
using System.IO;
using System.Linq;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiHostTests.Fakes;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// ACECustom's parts of RynthAi's Pets tab moved to RynthILT on 2026-10-20 (Hub/IltPetAutomation.cs):
// the pet charms, the summon-from-empty refill gate RynthAi's PetManager asks through RynthILT.Hub,
// siphon-lens auto-capture and the Breeding sub-tab offer. Their settings are copied once from the
// character's RynthAi miniremote.json (IltMigration.ImportRynthAiSettings).
internal static class PetAutomationTests
{
    private const uint Player = 0x50000A01;
    private const uint Lens = 0x80000A02;
    private const uint Mob = 0x80000A03;
    private const uint Charm = 0x80000A04;
    private const uint Lb = 0xA9B40000;

    public static void Register(Runner r)
    {
        r.Add("pets (RynthILT): summon from empty needs the opt-in, an active refill charm and the server's word; never off ILT", RefillGate);
        r.Add("pets (RynthILT): auto-capture uses the lens on a weak creature in range only, and only when switched on", Capture);
        r.Add("pets (RynthILT): the Breeding sub-tab is offered on ILT unless the server reports breeding off", BreedingSubTab);
        r.Add("pets (RynthILT): off ILT the Pets sections draw nothing and nothing ticks", DormantOffIlt);
        r.Add("migration: the Pets tab's ACECustom options come over from RynthAi's miniremote.json once", ImportsPetSettings);
    }

    private static IltHubController NewHub(string world, string folder, Action? setup = null)
    {
        FakeHost.Reset();
        FakeHost.WorldName = world;
        FakeHost.ContentsCalls = true;
        FakeHost.WeaponCalls = true;      // UseObject, RequestId, appraisal state
        FakeHost.LiveObjectsCalls = true;
        FakeHost.Names[Player] = "Petter";
        FakeHost.Positions[Player] = (Lb | 0x1C, 96f, 96f, 0f);
        setup?.Invoke();
        var host = FakeHost.Create(Player);
        string dir = Path.Combine(Program.TempRoot, "pets-" + folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var hub = new IltHubController(host, dir, new IltSettings(), new RynthAiClient(host));
        hub.OnLoginComplete("Petter", forceFromMigration: false);
        return hub;
    }

    private static void Carry(uint id, string name, uint wcid)
    {
        FakeHost.Names[id] = name;
        FakeHost.ItemTypes[id] = 0x80;   // misc
        FakeHost.Containers[id] = Player;
        FakeHost.Wcids[id] = wcid;
    }

    private static void RefillGate()
    {
        var hub = NewHub("InfiniteLeaftide", "refill", () => FakeHost.Bools[(Player, IltInventory.BoolPyrealRefillActive)] = true);
        var pets = hub.PetAutomation;
        Check.False(pets.ComputeAllowSummonOnEmpty(), "not opted in: no summon from empty");
        hub.State.Pet.RefillFromEmpty = true;
        Check.True(pets.ComputeAllowSummonOnEmpty(), "opted in, and the server stamped the refill bool (active and confirmed)");

        var charmOnly = NewHub("InfiniteLeaftide", "refill-charm", () =>
        {
            Carry(Charm, "Summon Essence Refill Charm", IltInventory.WcidSummonRefillCharm);
            FakeHost.Strings[(Charm, IltInventory.StrUse)] = "Toggles the refill. Status: ON";
        });
        charmOnly.State.Pet.RefillFromEmpty = true;
        Check.True(charmOnly.PetAutomation.RefillCharmActive(), "the carried charm reads ON");
        Check.False(charmOnly.PetAutomation.ComputeAllowSummonOnEmpty(), "but the server hasn't confirmed pet_refill: never spend");
        charmOnly.Options.Set(IltFeature.PetRefill, IltTri.On);
        Check.True(charmOnly.PetAutomation.ComputeAllowSummonOnEmpty(), "confirmed: summon from empty");
        charmOnly.Tick();
        Check.True(charmOnly.PetAutomation.AllowSummonOnEmpty, "the tick caches it for RynthAi (RynthILT.Hub AllowSummonOnEmpty)");

        var off = NewHub("Aeshnidae", "refill-off", () => FakeHost.Bools[(Player, IltInventory.BoolPyrealRefillActive)] = true);
        off.State.Pet.RefillFromEmpty = true;
        Check.False(off.PetAutomation.ComputeAllowSummonOnEmpty(), "off ILT: never");
        off.Tick();
        Check.False(off.PetAutomation.AllowSummonOnEmpty, "and nothing cached for RynthAi");
    }

    private static void Capture()
    {
        var hub = NewHub("InfiniteLeaftide", "capture", () =>
        {
            Carry(Lens, "Flawed Siphon Lens", PetCaptureRules.Lenses[0].Wcid);
            FakeHost.Names[Mob] = "Drudge Skulker";
            FakeHost.ItemTypes[Mob] = 0x10;   // creature
            FakeHost.Positions[Mob] = (Lb | 0x1C, 96f, 99f, 0f);   // 3 m
            FakeHost.TargetVitals[Mob] = (100, 100);
        });
        var pets = hub.PetAutomation;
        long now = Environment.TickCount64;   // the chat replies time their back-off on the real clock
        pets.TickCapture(now);
        Check.Eq(FakeHost.Uses.Count, 0, "switched off: no lens used");
        Check.Eq(pets.CaptureStatus, "off", "status off");

        hub.State.Pet.AutoCapture = true;
        pets.TickCapture(now);
        Check.Eq(FakeHost.Uses.Count, 0, "a full-health creature: no lens");
        Check.True(pets.CaptureStatus.Contains("Drudge Skulker") && pets.CaptureStatus.Contains("need"), "it says what it waits for");

        FakeHost.TargetVitals[Mob] = (15, 100);
        pets.TickCapture(now + 1000);
        Check.Eq(FakeHost.Uses.Count, 1, "15%: the lens is used");
        Check.Eq(FakeHost.Uses[0], Lens, "the chosen lens");
        pets.TickCapture(now + 1500);
        Check.Eq(FakeHost.Uses.Count, 1, "the cooldown holds the next use back");
        pets.OnChat("The creature is too strong to be siphoned.");
        Check.True(pets.CaptureStatus.StartsWith("refused"), "a refusal is noted");

        FakeHost.Positions[Mob] = (Lb | 0x1C, 96f, 110f, 0f);   // 14 m
        pets.TickCapture(now + 60_000);
        Check.Eq(FakeHost.Uses.Count, 1, "out of range: no lens");

        hub.Options.Set(RynthIltHub.FeatureMonsterCapture, IltTri.Off);
        FakeHost.Positions[Mob] = (Lb | 0x1C, 96f, 99f, 0f);
        pets.TickCapture(now + 120_000);
        Check.Eq(FakeHost.Uses.Count, 1, "the server reports monster capture off: no lens");
        Check.True(pets.CaptureStatus.Contains("disabled"), "and says so");
    }

    private static void BreedingSubTab()
    {
        var on = NewHub("InfiniteLeaftide", "breed-on");
        var tabs = on.SubTabs(RynthIltHub.TabParentPets);
        Check.Eq(tabs.Length, 1, "one Pets sub-tab on ILT");
        Check.Eq(tabs[0].Id, RynthIltHub.SectionPetBreeding, "Breeding, drawn as the pets-breeding section");
        Check.Eq(on.SubTabs("crafting").Length, 0, "nothing for another tab");
        on.Options.Set(RynthIltHub.FeaturePetBreeding, IltTri.Off);
        Check.Eq(on.SubTabs(RynthIltHub.TabParentPets).Length, 0, "the server reports breeding off: none");

        var off = NewHub("Aeshnidae", "breed-off");
        Check.Eq(off.SubTabs(RynthIltHub.TabParentPets).Length, 0, "off ILT: none");
    }

    private static void DormantOffIlt()
    {
        var off = NewHub("Aeshnidae", "dormant", () =>
        {
            Carry(Lens, "Flawed Siphon Lens", PetCaptureRules.Lenses[0].Wcid);
            FakeHost.Names[Mob] = "Drudge Skulker";
            FakeHost.ItemTypes[Mob] = 0x10;
            FakeHost.Positions[Mob] = (Lb | 0x1C, 96f, 99f, 0f);
            FakeHost.TargetVitals[Mob] = (1, 100);
        });
        off.State.Pet.AutoCapture = true;
        for (int i = 0; i < 5; i++) { off.Tick(); System.Threading.Thread.Sleep(60); }
        Check.Eq(FakeHost.Uses.Count, 0, "off ILT the capture never ticks (no lens used)");
        Check.False(off.RenderSection(RynthIltHub.SectionPetCharms) || off.RenderSection(RynthIltHub.SectionPetCapture),
                    "the Pets sections draw nothing (checked before any ImGui call)");
    }

    private static void ImportsPetSettings()
    {
        string legacy = Path.Combine(IltMigration.LegacyProfilesBase, "InfiniteLeaftide", "Petter");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Default.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, IltMigration.LegacyMiniRemoteFile),
            "{ \"Pet\": { \"HealingEnabled\": true, \"UsePyrealRefillPath\": true, \"AutoCaptureEnabled\": true, \"CaptureLensIndex\": 3 } }");
        var state = new IltHubState();
        Check.False(state.Pet.RefillFromEmpty || state.Pet.AutoCapture, "RynthILT's defaults: both off");
        var lines = IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Petter");
        Check.True(state.RynthAiSettingsImported, "marked done");
        Check.True(state.Pet.RefillFromEmpty, "summon from empty came over");
        Check.True(state.Pet.AutoCapture, "auto-capture came over");
        Check.Eq(state.Pet.CaptureLens, 3, "the lens came over");
        Check.True(lines.Any(l => l.Contains("auto-capture on")), "and the log line says so");

        state.Pet.AutoCapture = false;   // changed in RynthILT afterwards
        Check.Eq(IltMigration.ImportRynthAiSettings(state, "InfiniteLeaftide", "Petter").Count, 0, "the second login does nothing");
        Check.False(state.Pet.AutoCapture, "RynthILT's own change stays");

        var fresh = new IltHubState();
        IltMigration.ImportRynthAiSettings(fresh, "InfiniteLeaftide", "Nobody");
        Check.True(fresh.RynthAiSettingsImported && !fresh.Pet.AutoCapture && !fresh.Pet.RefillFromEmpty,
                   "a character without RynthAi files keeps the defaults (and is marked done)");

        File.WriteAllText(Path.Combine(legacy, IltMigration.LegacyMiniRemoteFile), "{ broken");
        var broken = new IltHubState();
        IltMigration.ImportRynthAiSettings(broken, "InfiniteLeaftide", "Petter");
        Check.True(broken.RynthAiSettingsImported && !broken.Pet.AutoCapture, "an unreadable miniremote.json: defaults, no exception");
    }
}
