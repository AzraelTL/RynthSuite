using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RynthCore.PluginSdk.Manifest;
using RynthCore.RynthAiTests.Harness;

namespace RynthServerPack.Tests;

// Every server plugin's csproj (Plugins\RynthCore.Plugin.Rynth*\ with RynthPluginServerId), read as
// XML: the detection data RynthCore's loader and sections use must be unique and must not overlap,
// or a client would load (or show) the wrong server's plugin.
internal static class ServerProjectsTests
{
    public static void Register(Runner r)
    {
        r.Add("server projects: every listed server has a plugin", AllServersPresent);
        r.Add("server projects: ids, plugin names and hosts are unique", Unique);
        r.Add("server projects: no server's host is claimed by another server's list", NoHostOverlap);
        r.Add("server projects: each new server plugin has ServerData.cs and imports the shared props", Layout);
        r.Add("server projects: RynthOracle and RynthInventory send no server's own commands (they live in its plugin)", SharedPluginsServerFree);
    }

    /// <summary>
    /// Since 2026-10-20 a server's own features live in its server plugin (RynthConquest, RynthAelrynth):
    /// the shared plugins' sources hold none of those servers' commands or chat markers, and use the
    /// server tags only to pick a quest pack (data, RynthOracle's ServerIdentity.cs).
    /// </summary>
    private static void SharedPluginsServerFree()
    {
        string[] commands = { "\"/bank", "\"/b\"", "\"/augs", "\"/enl augs", "\"/bonus", "\"/fship list", "\"/top", "\"/trial", "\"/mule-data", "\"~ael1" };
        foreach (string plugin in new[] { "RynthCore.Plugin.RynthOracle", "RynthCore.Plugin.RynthInventory" })
        {
            string dir = Path.Combine(PluginsDir(), plugin);
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .ToList();
            Check.True(files.Count > 10, plugin + ": its sources were found (" + files.Count + ")");
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                string name = plugin + "/" + Path.GetRelativePath(dir, file);
                foreach (string c in commands)
                    Check.False(text.Contains(c, StringComparison.Ordinal), $"{name} sends no server command {c.TrimStart('"')}");
                if (!file.EndsWith("ServerIdentity.cs", StringComparison.Ordinal))
                    Check.False(text.Contains("ServerTags.Conquest", StringComparison.Ordinal) || text.Contains("ServerTags.Aelrynth", StringComparison.Ordinal),
                                $"{name} has no Conquest or Aelrynth feature");
            }
        }
    }

    private sealed record Project(string Dir, string PluginName, string Id, string Name, string[] Hosts, string[] Worlds, bool Shared);

    /// <summary>The servers in the request (the server list's busiest, plus Aelrynth) and their plugins.</summary>
    private static readonly string[] Expected =
    {
        "coldeve", "ilt", "frostace", "reefcull", "conquest", "dreamweave", "portalstorm", "sundering",
        "achard", "leafdawn", "derptide", "frostcull", "thistlecrown", "eversong", "leafdawning", "aelrynth",
    };

    private static string PluginsDir()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Plugins", "Shared", "ServerPack")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null) throw new DirectoryNotFoundException("RynthSuite\\Plugins not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir, "Plugins");
    }

    private static List<Project> Load()
    {
        var list = new List<Project>();
        foreach (string dir in Directory.GetDirectories(PluginsDir(), "RynthCore.Plugin.Rynth*"))
        {
            string csproj = Path.Combine(dir, Path.GetFileName(dir) + ".csproj");
            if (!File.Exists(csproj)) continue;
            XDocument doc = XDocument.Load(csproj);
            string Prop(string name) => doc.Descendants(name).FirstOrDefault()?.Value.Trim() ?? "";
            string id = Prop("RynthPluginServerId");
            if (id.Length == 0) continue;
            string[] Split(string v) => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool shared = doc.Descendants("Import").Any(i => (i.Attribute("Project")?.Value ?? "").EndsWith("ServerPlugin.props"));
            string asm = Prop("AssemblyName");
            list.Add(new Project(dir, asm.Replace("RynthCore.Plugin.", ""), id, Prop("RynthPluginServerName"),
                                 Split(Prop("RynthPluginServerHosts")), Split(Prop("RynthPluginServerWorldNames")), shared));
        }
        return list;
    }

    private static void AllServersPresent()
    {
        List<Project> projects = Load();
        foreach (string id in Expected)
            Check.True(projects.Any(p => p.Id == id), "a server plugin for " + id);
        Check.Eq(projects.Count, Expected.Length, "no unexpected server plugins");
    }

    private static void Unique()
    {
        List<Project> projects = Load();
        Check.Eq(projects.Select(p => p.Id).Distinct().Count(), projects.Count, "ids unique");
        Check.Eq(projects.Select(p => p.PluginName.ToLowerInvariant()).Distinct().Count(), projects.Count, "plugin names unique");
        var hosts = projects.SelectMany(p => p.Hosts).Select(h => h.ToLowerInvariant()).ToList();
        Check.Eq(hosts.Distinct().Count(), hosts.Count, "hosts unique");
        foreach (Project p in projects)
        {
            Check.True(p.Hosts.Length > 0, p.PluginName + " lists a host (the loader picks plugins by host)");
            Check.True(p.Worlds.Length > 0, p.PluginName + " lists a world name");
            Check.True(p.Name.Length > 0, p.PluginName + " has a display name");
        }
    }

    private static void NoHostOverlap()
    {
        List<Project> projects = Load();
        foreach (Project owner in projects)
            foreach (string host in owner.Hosts.Where(h => !h.Contains('*')))
                foreach (Project other in projects.Where(o => o != owner))
                {
                    var server = new RynthPluginServer { Id = other.Id, Hosts = other.Hosts.ToList() };
                    Check.False(server.MatchesHost(host, out string hit),
                                $"{owner.PluginName}'s host {host} is not claimed by {other.PluginName} ({hit})");
                }
    }

    private static void Layout()
    {
        foreach (Project p in Load().Where(p => p.Id != "ilt"))
        {
            Check.True(p.Shared, p.PluginName + " imports Shared\\ServerPack\\ServerPlugin.props");
            Check.True(File.Exists(Path.Combine(p.Dir, "ServerData.cs")), p.PluginName + " has ServerData.cs");
        }
    }

}
