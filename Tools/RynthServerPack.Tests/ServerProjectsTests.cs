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
