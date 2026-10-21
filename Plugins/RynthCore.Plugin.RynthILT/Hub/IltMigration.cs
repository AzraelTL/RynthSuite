// IltMigration.cs - Carries the ILT Hub's saved data over from RynthAi (2026-10-13).
//
// While the ILT Hub lived in RynthAi it saved per character in RynthAi's settings folder
// (<RynthAi>\SettingsProfiles\<world or ACEmulator>\<character>\: ilt-hub.json,
// ilt-hub-transactions.json, ilt-hub-profiles\) and for every character in the RynthAi folder
// (IltHubProfiles\, ilt-aug-costs.json); the server list and the "Force enable" override were
// fields of each RynthAi profile (FeatureServerNames, ForceServerFeatures).
//
// RynthILT keeps its own files (IltHubStore, IltSettingsStore). On a character's first login
// with RynthILT (no hub.json yet) the old files are COPIED, never moved or deleted: RynthAi no
// longer reads them, and a rollback to an older RynthAi still finds them. Existing RynthILT files
// are never overwritten. The character's active RynthAi profile adds its world names to
// RynthILT's list and, when it had "Force enable" ticked, ticks this character's "Treat this world
// as ILT". The shared files are copied once (IltSettings.SharedFilesMigrated). Pump thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RynthCore.Install;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthILT.Hub;

/// <summary>
/// The fields of a RynthAi profile RynthILT reads (the rest of the file is ignored): the two Server
/// Features fields, and the settings that moved to RynthILT on 2026-10-20 (null when the profile
/// predates them: the default applies).
/// </summary>
public sealed class IltLegacyProfileFlags
{
    public List<string>? FeatureServerNames;
    public bool ForceServerFeatures;
    /// <summary>RynthAi's Empyrean Handler ring evade (on by default there too).</summary>
    public bool? HandlerRingEvade;
    /// <summary>RynthAi's Vaulted Dungeons master switch (off by default).</summary>
    public bool? EnableVaultedDungeons;
    /// <summary>RynthAi's T11 item-aug override (-1 = automatic).</summary>
    public int? T11ItemAugsOverride;
}

/// <summary>The Remote tab switches of RynthAi's huds.json that moved to RynthILT (2026-10-20).</summary>
public sealed class IltLegacyHuds
{
    public bool? MiniShowBank;
    public bool? MiniShowGuardian;
}

/// <summary>The Pets tab fields of RynthAi's miniremote.json that moved to RynthILT (2026-10-20).</summary>
public sealed class IltLegacyMiniRemote
{
    public IltLegacyPetTab? Pet;
}

public sealed class IltLegacyPetTab
{
    public bool? UsePyrealRefillPath;
    public bool? AutoCaptureEnabled;
    public int? CaptureLensIndex;
}

internal static class IltMigration
{
    /// <summary>RynthAi's per-server profile folders (overridable for the tests).</summary>
    public static string LegacyProfilesBase = Path.Combine(RynthInstallPaths.RynthAiDir, "SettingsProfiles");

    /// <summary>RynthAi's folder (shared profiles, aug costs; overridable for the tests).</summary>
    public static string LegacySharedRoot = RynthInstallPaths.RynthAiDir;

    /// <summary>RynthAi's folder for profiles saved before they went per server.</summary>
    private const string LegacyServerFolder = "ACEmulator";

    /// <summary>RynthAi's file-name sanitizer (LegacyDashboardRenderer.SanitizeFileName).</summary>
    internal static string RynthAiSafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '\'' || c == '-' ? c : '_');
        return sb.ToString();
    }

    /// <summary>RynthAi's settings folder for this character: the world's, else the pre-per-server one. Null when neither exists.</summary>
    public static string? FindLegacyCharFolder(string world, string charName)
    {
        if (string.IsNullOrWhiteSpace(charName)) return null;
        string safeChar = RynthAiSafeName(charName.Trim());
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(world))
        {
            string safeWorld = RynthAiSafeName(world.Trim());
            if (safeWorld.Length > 0) candidates.Add(Path.Combine(LegacyProfilesBase, safeWorld, safeChar));
        }
        candidates.Add(Path.Combine(LegacyProfilesBase, LegacyServerFolder, safeChar));
        foreach (string dir in candidates)
            if (Directory.Exists(dir) && (File.Exists(Path.Combine(dir, "ilt-hub.json"))
                                          || File.Exists(Path.Combine(dir, "ilt-hub-transactions.json"))
                                          || Directory.Exists(Path.Combine(dir, "ilt-hub-profiles"))
                                          || File.Exists(Path.Combine(dir, "active_profile.txt"))
                                          || File.Exists(Path.Combine(dir, LegacyMiniRemoteFile))
                                          || File.Exists(Path.Combine(dir, "Default.json"))))
                return dir;
        return null;
    }

    /// <summary>Whether <paramref name="charFolder"/>'s copy from RynthAi failed and hasn't been finished yet.</summary>
    public static bool IsPending(IltSettings settings, string charFolder)
    {
        if (string.IsNullOrEmpty(charFolder)) return false;
        foreach (string f in settings.PendingCharacterMigrations ?? new List<string>())
            if (string.Equals(f, charFolder, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void SetPending(IltSettings settings, string charFolder, bool pending)
    {
        settings.PendingCharacterMigrations ??= new List<string>();
        settings.PendingCharacterMigrations.RemoveAll(f => string.Equals(f, charFolder, StringComparison.OrdinalIgnoreCase));
        if (pending) settings.PendingCharacterMigrations.Add(charFolder);
    }

    /// <summary>
    /// First login of a character with RynthILT: copies its RynthAi ILT Hub files into
    /// <paramref name="newFolder"/> (only when it has no hub.json yet, or an earlier copy failed
    /// part-way) and folds its RynthAi Server Features into <paramref name="settings"/>.
    /// <paramref name="forceIlt"/> says whether the profile had "Force enable" ticked. Returns what
    /// was done (for the log), empty when nothing. When a file can't be copied the folder is marked
    /// pending in <paramref name="settings"/> (save them): the Hub then writes nothing there and the
    /// next call copies what is still missing (never overwriting).
    /// </summary>
    public static List<string> MigrateCharacter(string newFolder, string world, string charName, IltSettings settings, out bool forceIlt)
    {
        forceIlt = false;
        var done = new List<string>();
        if (string.IsNullOrEmpty(newFolder)) return done;
        bool wasPending = IsPending(settings, newFolder);
        if (!wasPending && File.Exists(Path.Combine(newFolder, IltHubStore.StateFile))) return done;
        string? legacy = FindLegacyCharFolder(world, charName);
        if (legacy == null)
        {
            // The RynthAi files are gone: nothing left to wait for.
            if (wasPending) { SetPending(settings, newFolder, false); done.Add("the RynthAi files of an unfinished copy are gone; RynthILT's own files are used from now on"); }
            return done;
        }

        int failed = 0;
        if (CopyIfMissing(Path.Combine(legacy, "ilt-hub.json"), Path.Combine(newFolder, IltHubStore.StateFile), ref failed))
            done.Add("ilt-hub.json");
        if (CopyIfMissing(Path.Combine(legacy, "ilt-hub-transactions.json"), Path.Combine(newFolder, IltHubStore.TransactionsFile), ref failed))
            done.Add("ilt-hub-transactions.json");
        int profiles = CopyFolder(Path.Combine(legacy, "ilt-hub-profiles"), Path.Combine(newFolder, IltHubStore.ProfilesFolder), ref failed);
        if (profiles > 0) done.Add($"{profiles} Hub profile(s)");
        if (failed > 0)
        {
            SetPending(settings, newFolder, true);
            done.Add($"{failed} file(s) NOT copied (see the lines above; the originals in {legacy} are untouched). " +
                     "The Hub saves nothing for this character until they are; tried again every few minutes and at each login");
        }
        else if (wasPending)
        {
            SetPending(settings, newFolder, false);
            done.Add("the earlier unfinished copy is complete");
        }

        if (ReadProfileFlags(legacy, out IltLegacyProfileFlags flags))
        {
            int added = MergeServerNames(settings, flags.FeatureServerNames);
            if (added > 0) done.Add($"{added} server name(s) from the RynthAi profile");
            if (flags.ForceServerFeatures)
            {
                forceIlt = true;
                done.Add("\"Force enable\" -> Treat this world as ILT");
            }
        }
        return done;
    }

    /// <summary>
    /// Once for every character: RynthAi's shared Hub profiles and aug-cost table into
    /// <paramref name="newRoot"/> (never over a file RynthILT already has). Returns what was done.
    /// Marked done only when every copy worked: a failed one is tried again at the next login.
    /// </summary>
    public static List<string> MigrateShared(IltSettings settings, string newRoot)
    {
        var done = new List<string>();
        if (settings.SharedFilesMigrated) return done;
        int failed = 0;
        int profiles = CopyFolder(Path.Combine(LegacySharedRoot, "IltHubProfiles"), Path.Combine(newRoot, "Profiles"), ref failed);
        if (profiles > 0) done.Add($"{profiles} shared Hub profile(s)");
        if (CopyIfMissing(Path.Combine(LegacySharedRoot, "ilt-aug-costs.json"), Path.Combine(newRoot, "ilt-aug-costs.json"), ref failed))
            done.Add("ilt-aug-costs.json");
        if (failed == 0) settings.SharedFilesMigrated = true;
        else done.Add($"{failed} shared file(s) NOT copied; tried again at the next login");
        return done;
    }

    /// <summary>RynthAi's per-character Mini Remote file (the Pets tab's settings).</summary>
    public const string LegacyMiniRemoteFile = "miniremote.json";

    /// <summary>RynthAi's per-character HUD file (the Remote tab's section switches).</summary>
    public const string LegacyHudsFile = "huds.json";

    /// <summary>
    /// Once per character (IltHubState.RynthAiSettingsImported): the RynthAi settings that moved to
    /// RynthILT on 2026-10-20 are copied from the character's RynthAi files into <paramref name="state"/>:
    /// from miniremote.json the Pets tab's ACECustom options (summon from empty with the refill
    /// charm, siphon auto-capture and its lens); from the active profile (as ReadProfileFlags) the
    /// Empyrean Handler ring evade, the Vaulted Dungeons master switch and the T11 item-aug override;
    /// from huds.json the Remote tab's Bank / Guardian switches. The RynthAi files are only read (RynthAi keeps the
    /// old fields readable and no longer uses them). A character with no RynthAi files keeps
    /// RynthILT's defaults. Returns what was copied (for the log); empty when nothing was done.
    /// </summary>
    public static List<string> ImportRynthAiSettings(IltHubState state, string world, string charName)
    {
        var done = new List<string>();
        if (state.RynthAiSettingsImported || string.IsNullOrWhiteSpace(charName)) return done;
        string? legacy = FindLegacyCharFolder(world, charName);
        if (legacy != null)
        {
            if (ReadJson(Path.Combine(legacy, LegacyMiniRemoteFile), IltHubJsonContext.Default.IltLegacyMiniRemote) is { Pet: { } pet })
            {
                if (pet.UsePyrealRefillPath is bool refill) { state.Pet.RefillFromEmpty = refill; done.Add($"summon from empty essences {(refill ? "on" : "off")}"); }
                if (pet.AutoCaptureEnabled is bool capture) { state.Pet.AutoCapture = capture; done.Add($"auto-capture {(capture ? "on" : "off")}"); }
                if (pet.CaptureLensIndex is int lens)
                {
                    state.Pet.CaptureLens = Math.Clamp(lens, 0, PetCaptureRules.Lenses.Length - 1);
                    done.Add("capture lens " + PetCaptureRules.LensAt(state.Pet.CaptureLens).Name);
                }
            }
            if (ReadJson(Path.Combine(legacy, LegacyHudsFile), IltHubJsonContext.Default.IltLegacyHuds) is { } huds)
            {
                if (huds.MiniShowBank is bool bank) { state.Remote.ShowBank = bank; if (!bank) done.Add("the Remote tab's Bank hidden"); }
                if (huds.MiniShowGuardian is bool g) { state.Remote.ShowGuardian = g; if (!g) done.Add("the Remote tab's Guardian line hidden"); }
            }
            if (ReadProfileFlags(legacy, out IltLegacyProfileFlags flags))
            {
                if (flags.HandlerRingEvade is bool ring) { state.Combat.HandlerRingEvade = ring; done.Add($"Empyrean Handler ring evade {(ring ? "on" : "off")}"); }
                if (flags.EnableVaultedDungeons is bool vaulted) { state.VaultedDungeonsEnabled = vaulted; done.Add($"Vaulted Dungeons master switch {(vaulted ? "on" : "off")}"); }
                if (flags.T11ItemAugsOverride is int augs && augs >= 0) { state.T11.ItemAugsOverride = augs; done.Add($"T11 item augmentations fixed at {augs:N0}"); }
            }
        }
        state.RynthAiSettingsImported = true;
        if (done.Count == 0) done.Add(legacy == null ? "no RynthAi settings for this character (defaults kept)" : "nothing set in RynthAi (defaults kept)");
        return done;
    }

    /// <summary>A JSON file read with <paramref name="info"/>, or null (missing or unreadable: logged).</summary>
    private static T? ReadJson<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), info) : null;
        }
        catch (Exception ex)
        {
            RynthLog.Exception(LogCat.IltStore, ex, $"migration: read {path}");
            return null;
        }
    }

    /// <summary>The character's active RynthAi profile's Server Features fields.</summary>
    internal static bool ReadProfileFlags(string legacyCharFolder, out IltLegacyProfileFlags flags)
    {
        flags = new IltLegacyProfileFlags();
        try
        {
            string active = "Default";
            string marker = Path.Combine(legacyCharFolder, "active_profile.txt");
            if (File.Exists(marker))
            {
                string name = File.ReadAllText(marker).Trim();
                if (name.Length > 0) active = name;
            }
            string path = Path.Combine(legacyCharFolder, active + ".json");
            if (!File.Exists(path)) return false;
            var f = JsonSerializer.Deserialize(File.ReadAllText(path), IltHubJsonContext.Default.IltLegacyProfileFlags);
            if (f == null) return false;
            flags = f;
            return true;
        }
        catch (Exception ex)
        {
            RynthLog.Exception(LogCat.IltStore, ex, "migration: RynthAi profile read");
            return false;
        }
    }

    /// <summary>Adds the names the list doesn't have yet (case-insensitive); returns how many.</summary>
    internal static int MergeServerNames(IltSettings settings, IEnumerable<string>? names)
    {
        if (names == null) return 0;
        int added = 0;
        foreach (string n in ServerFeatureGate.ParseList(string.Join(",", names)))
        {
            if (settings.ServerNames.Any(have => have.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
            settings.ServerNames.Add(n);
            added++;
        }
        return added;
    }

    /// <summary>
    /// Copies <paramref name="from"/> to <paramref name="to"/> unless the source is missing or the
    /// target exists. A failed copy is never silent: it is logged and said in chat once per folder
    /// (DataWriteGuard, on ILT), with the reason and the fix and where the original still is, and
    /// counted in <paramref name="failed"/>.
    /// </summary>
    private static bool CopyIfMissing(string from, string to, ref int failed)
    {
        try
        {
            if (!File.Exists(from) || File.Exists(to)) return false;
        }
        catch { return false; }
        string fromDir = Path.GetDirectoryName(from) ?? from;
        bool ok = DataWriteGuard.TrySave($"{Path.GetFileName(from)} carried over from RynthAi (the original is still in {fromDir})", to, () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
        });
        if (!ok) failed++;
        return ok;
    }

    private static int CopyFolder(string from, string to, ref int failed)
    {
        int n = 0;
        string[] files;
        try
        {
            if (!Directory.Exists(from)) return 0;
            files = Directory.GetFiles(from, "*.json");
        }
        catch (Exception ex)
        {
            failed++;
            RynthLog.Exception(LogCat.IltStore, ex, $"migration: list {from}");
            return 0;
        }
        foreach (string f in files)
            if (CopyIfMissing(f, Path.Combine(to, Path.GetFileName(f)), ref failed)) n++;
        return n;
    }
}
