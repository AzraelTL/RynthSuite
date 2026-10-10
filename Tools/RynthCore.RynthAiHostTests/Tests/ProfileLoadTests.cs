using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using RynthCore.Plugin.RynthAi;
using RynthCore.Plugin.RynthAi.LegacyUi;
using RynthCore.RynthAiTests.Harness;

namespace RynthCore.RynthAiHostTests.Tests;

// Loading a profile (login, profile switch) runs LegacyDashboardRenderer.CopySettings, which
// copies the loaded file into the live settings one field at a time. A field with no copy line
// keeps its in-memory default, and the next autosave writes that default over the profile.
// PetAutoRefill, MonsterNameBlacklist and the radar placement were dropped that way.
//  - every serialized LegacyUiSettings member set to a non-default value comes through
//    CopySettings (catches any field added later without a copy line);
//  - a profile written to disk and loaded back serializes unchanged.
internal static class ProfileLoadTests
{
    private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Register(Runner r)
    {
        r.Add("profile: every saved setting survives loading (CopySettings lists each serialized field)", EverySavedFieldIsCopied);
        r.Add("profile: settings survive a save and a reload through the profile file", SettingsSurviveReload);
    }

    /// <summary>LegacyDashboardRenderer.CopySettings(src, dst): what LoadSettings / SwitchProfile run.</summary>
    private static void CopySettings(LegacyUiSettings src, LegacyUiSettings dst) =>
        typeof(LegacyDashboardRenderer).GetMethod("CopySettings", Any)!.Invoke(null, new object[] { src, dst });

    /// <summary>A different value of the member's type that CopySettings' normalizing keeps distinct from the default.</summary>
    private static bool TryMutate(string name, Type type, object? value, out object? mutated)
    {
        mutated = null;
        if (name == nameof(LegacyUiSettings.OffhandDefault)) { mutated = "Shield"; return true; }   // must parse as an OffhandMode
        if (name == nameof(LegacyUiSettings.NavLabelMode))   { mutated = 1; return true; }          // NavLabelModes.Clamp: 0..2
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(bool))   { mutated = !((bool?)value ?? false); return true; }
        if (t == typeof(int))    { mutated = ((int?)value ?? 0) + 3; return true; }   // +3: MovementMode 1/2 map back to 0
        if (t == typeof(long))   { mutated = ((long?)value ?? 0) + 3; return true; }
        if (t == typeof(uint))   { mutated = ((uint?)value ?? 0) + 3; return true; }
        if (t == typeof(float))  { mutated = ((float?)value ?? 0f) + 3f; return true; }
        if (t == typeof(double)) { mutated = ((double?)value ?? 0.0) + 3.0; return true; }
        if (t == typeof(string)) { mutated = (string?)value + "x"; return true; }
        if (t.IsEnum)
        {
            foreach (object v in Enum.GetValues(t))
                if (!v.Equals(value)) { mutated = v; return true; }
        }
        return false;
    }

    private static void EverySavedFieldIsCopied()
    {
        var info = RynthAiJsonContext.Default.LegacyUiSettings;
        var defaults = new LegacyUiSettings();
        var src = new LegacyUiSettings();
        var dst = new LegacyUiSettings();

        // Reference members (lists, option objects): dst must end up with a new instance.
        var dstOriginal = new Dictionary<string, object?>();
        var skipped = new List<string>();
        foreach (var p in info.Properties)
        {
            if (p.Get == null || p.Set == null) continue;
            Type t = p.PropertyType;
            if (t.IsValueType || t == typeof(string))
            {
                if (TryMutate(p.Name, t, p.Get(src), out object? m)) p.Set(src, m);
                else skipped.Add(p.Name);
            }
            else
            {
                dstOriginal[p.Name] = p.Get(dst);
            }
        }

        CopySettings(src, dst);

        int members = 0;
        var missing = new List<string>();
        foreach (var p in info.Properties)
        {
            if (p.Get == null || p.Set == null) continue;
            members++;
            if (dstOriginal.TryGetValue(p.Name, out object? before))
            {
                if (before != null && ReferenceEquals(p.Get(dst), before)) missing.Add(p.Name);
            }
            else if (!skipped.Contains(p.Name)
                     && Equals(p.Get(dst), p.Get(defaults)) && !Equals(p.Get(src), p.Get(defaults)))
            {
                missing.Add(p.Name);
            }
        }

        Check.True(members > 100, $"the JSON metadata lists the settings ({members} members)");
        Check.Eq(skipped.Count, 0, "every value member has a test value (no test value for: " + string.Join(", ", skipped) + ")");
        Check.Eq(missing.Count, 0, "members CopySettings leaves at their default: " + string.Join(", ", missing));
    }

    private static void SettingsSurviveReload()
    {
        var saved = new LegacyUiSettings();
        saved.ItemInfoOnSelect = !saved.ItemInfoOnSelect;
        saved.PetAutoRefill = !saved.PetAutoRefill;
        saved.MonsterNameBlacklist.Add("Drudge Skulker");
        saved.RadarPosX = 120f;
        saved.RadarSizeY = 300f;

        string path = Path.Combine(Program.TempRoot, "profile-load", "Default.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(saved, RynthAiJsonContext.Default.LegacyUiSettings));

        // LoadSettings: read the file, copy it into the live settings object (a fresh session).
        var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), RynthAiJsonContext.Default.LegacyUiSettings)!;
        var live = new LegacyUiSettings();
        CopySettings(loaded, live);

        Check.Eq(live.ItemInfoOnSelect, saved.ItemInfoOnSelect, "describe items when selected");
        Check.Eq(live.PetAutoRefill, saved.PetAutoRefill, "pet auto refill");
        Check.True(live.MonsterNameBlacklist.Contains("Drudge Skulker"), "monster blacklist");
        Check.Eq(live.RadarPosX, 120f, "radar position");
        Check.Eq(live.RadarSizeY, 300f, "radar size");

        // Unchanged JSON means the autosave right after login has nothing to write over the file.
        Check.Eq(JsonSerializer.Serialize(live, RynthAiJsonContext.Default.LegacyUiSettings),
                 JsonSerializer.Serialize(saved, RynthAiJsonContext.Default.LegacyUiSettings),
                 "the reloaded profile saves back unchanged (autosave writes nothing different)");
    }
}
