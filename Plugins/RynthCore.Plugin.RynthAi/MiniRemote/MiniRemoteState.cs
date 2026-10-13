// MiniRemoteState.cs — What the Mini Remote's universal tabs remember per character: the Pets
// tab (roster view, healing / cosmetic pets, the refill path) and the Quests tab (filter,
// favorites, the favorites HUD), plus the VTank .usd importer's last file.
//
// Lives in <charFolder>\miniremote.json. These settings were part of the ILT Hub's
// ilt-hub.json (SilentKelpie's ILT Hub, which RynthAi hosted until 2026-10-13). The ILT Hub went
// to the RynthILT plugin; the universal tabs stayed. On the first load without miniremote.json
// the Pets / Quests / import parts of ilt-hub.json are read (field names unchanged, so the old
// file deserializes straight into these classes); ilt-hub.json itself is never written again (it
// is RynthILT's to copy, IltMigration). Serialized with the source-generated RynthAiJsonContext
// (IncludeFields = true), so plain public fields are used throughout.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RynthCore.Plugin.RynthAi.MiniRemote;

public sealed class MiniRemoteState
{
    public int SchemaVersion = 1;
    public PetTabState Pet = new();
    public QuestsTabState Quests = new();
    /// <summary>Last VTank .usd file the importer read (the ILT Hub's Gear.LastUsdPath before).</summary>
    public string LastUsdPath = string.Empty;
}

/// <summary>The Pets tab's settings (the ILT Hub's IltPetState, without the ILT /pets and /shinies logs).</summary>
public sealed class PetTabState
{
    // Healing-pet automation (UB "HealingBuddy" subset).
    public bool HealingEnabled;
    /// <summary>Exact name of the heal-pet essence to use; empty = auto-detect (Healing Buddy / Dule box).</summary>
    public string HealPetName = string.Empty;
    public int HealthThresholdPercent = 80;
    /// <summary>Do not summon when current HP is below this (summoning mid-death wastes the use). 0 = off.</summary>
    public int MinHealthPoints = 100;
    public bool AutoDespawnAfterHeal = true;
    public int RespawnDelaySeconds = 2;
    public int PetTimeoutSeconds = 30;

    /// <summary>
    /// Allow a combat summon from an EMPTY essence when the Summon Essence Refill Charm is
    /// active (ACECustom: the server refills from banked pyreals). Still requires the server
    /// flag to be on (RynthILT reports it).
    /// </summary>
    public bool UsePyrealRefillPath;

    /// <summary>Roster type shown in the Pet tab (PetKind: 0 Combat, 1 Healing, 2 Cosmetic).</summary>
    public int RosterKind;
    /// <summary>Roster sort (PetSort: 0 Priority, 1 Bond, 2 Potency, 3 Breed ready, 4 Level, 5 Uses, 6 Name).</summary>
    public int RosterSort;
    /// <summary>User type overrides by essence name; essences not listed are auto-classified.</summary>
    public List<PetAssignment> Assignments = new();

    /// <summary>Essence name of the chosen cosmetic (display) pet; empty = none.</summary>
    public string CosmeticPetName = string.Empty;
    /// <summary>Keep the cosmetic pet summoned while in peace mode; it is dismissed when a combat pet is needed.</summary>
    public bool KeepCosmeticOut;
    /// <summary>Seconds to wait before re-summoning the cosmetic pet after it despawns.</summary>
    public int CosmeticRespawnSeconds = 15;
}

/// <summary>A user's choice of summon type for one essence (matched by name, case-insensitive).</summary>
public sealed class PetAssignment
{
    public string Name = string.Empty;
    /// <summary>PetKind as int (0 Combat, 1 Healing, 2 Cosmetic).</summary>
    public int Kind;
}

/// <summary>The Quests tab's settings (the quest fields of the ILT Hub's IltCharacterState).</summary>
public sealed class QuestsTabState
{
    /// <summary>Quest Tracker search box contents.</summary>
    public string QuestFilter = string.Empty;
    /// <summary>Starred quest flag keys (lowercase), shown on the floating favorites HUD.</summary>
    public List<string> QuestFavorites = new();
    /// <summary>Quest tracker lists starred quests only.</summary>
    public bool QuestFavoritesOnly;
    /// <summary>Floating quest favorites HUD is shown.</summary>
    public bool ShowQuestFavoritesHud;
    public bool QuestFavoritesHudLocked;
}

/// <summary>The parts of an old ilt-hub.json the universal tabs take over (first load only).</summary>
public sealed class LegacyIltHubFile
{
    public PetTabState? Pet;
    /// <summary>The ILT Hub's IltCharacterState: its quest fields have QuestsTabState's names.</summary>
    public QuestsTabState? Character;
    public LegacyIltGear? Gear;
}

public sealed class LegacyIltGear
{
    public string? LastUsdPath;
}

/// <summary>Loads and saves <see cref="MiniRemoteState"/> (dirty-checked like the ILT Hub's store was).</summary>
internal sealed class MiniRemoteStore
{
    public const string FileName = "miniremote.json";
    public const string LegacyFileName = "ilt-hub.json";

    private readonly string _charFolder;
    private string _lastJson = string.Empty;

    public MiniRemoteStore(string charFolder) => _charFolder = charFolder ?? string.Empty;

    private string StatePath => Path.Combine(_charFolder, FileName);

    /// <summary>The saved state; on the first load the universal parts of ilt-hub.json; else defaults.</summary>
    public MiniRemoteState Load()
    {
        try
        {
            if (_charFolder.Length > 0 && File.Exists(StatePath))
            {
                string json = File.ReadAllText(StatePath);
                var s = JsonSerializer.Deserialize(json, RynthAiJsonContext.Default.MiniRemoteState);
                if (s != null)
                {
                    Normalize(s);
                    _lastJson = json;
                    return s;
                }
            }
            string legacy = Path.Combine(_charFolder, LegacyFileName);
            if (_charFolder.Length > 0 && File.Exists(legacy))
            {
                var old = JsonSerializer.Deserialize(File.ReadAllText(legacy), RynthAiJsonContext.Default.LegacyIltHubFile);
                if (old != null)
                {
                    var s = FromLegacy(old);
                    RynthLog.Write(LogCat.General, $"[MiniRemote] Pets / Quests tab settings read from {LegacyFileName} (the ILT Hub went to RynthILT; saved in {FileName} from now on).");
                    return s;   // _lastJson stays empty: the first save writes miniremote.json
                }
            }
        }
        catch (Exception ex) { RynthLog.Exception(LogCat.General, ex, $"[MiniRemote] load {StatePath}"); }
        return new MiniRemoteState();
    }

    /// <summary>The universal parts of an old ilt-hub.json as a fresh state.</summary>
    internal static MiniRemoteState FromLegacy(LegacyIltHubFile old)
    {
        var s = new MiniRemoteState
        {
            Pet = old.Pet ?? new PetTabState(),
            Quests = old.Character ?? new QuestsTabState(),
            LastUsdPath = old.Gear?.LastUsdPath ?? string.Empty,
        };
        Normalize(s);
        return s;
    }

    /// <summary>Writes the state only when its JSON differs from the last write.</summary>
    public void SaveIfDirty(MiniRemoteState state)
    {
        if (_charFolder.Length == 0) return;
        try
        {
            string json = JsonSerializer.Serialize(state, RynthAiJsonContext.Default.MiniRemoteState);
            if (json == _lastJson) return;
            Directory.CreateDirectory(_charFolder);
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StatePath, overwrite: true);
            _lastJson = json;
        }
        catch (Exception ex) { RynthLog.Exception(LogCat.General, ex, $"[MiniRemote] save {StatePath}"); }
    }

    /// <summary>Repairs null collections / out-of-range values after deserialization.</summary>
    internal static void Normalize(MiniRemoteState s)
    {
        s.Pet ??= new PetTabState();
        s.Pet.Assignments ??= new();
        s.Pet.Assignments.RemoveAll(a => a == null || string.IsNullOrWhiteSpace(a.Name));
        s.Pet.HealPetName ??= string.Empty;
        s.Pet.CosmeticPetName ??= string.Empty;
        s.Pet.RosterKind = Math.Clamp(s.Pet.RosterKind, 0, 2);
        s.Pet.RosterSort = Math.Clamp(s.Pet.RosterSort, 0, 6);
        s.Pet.CosmeticRespawnSeconds = Math.Clamp(s.Pet.CosmeticRespawnSeconds, 5, 600);
        s.Quests ??= new QuestsTabState();
        s.Quests.QuestFilter ??= string.Empty;
        s.Quests.QuestFavorites ??= new();
        s.Quests.QuestFavorites.RemoveAll(string.IsNullOrWhiteSpace);
        s.LastUsdPath ??= string.Empty;
    }
}
