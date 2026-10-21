// ServerPackProfile.cs - A server plugin's settings as JSON, for other plugins ("RynthServer.Profile",
// Plugins/Shared/RynthServerProfileApi.cs, served by ServerPackExports' RynthPluginQueryInterface).
//
// RynthCore's GetServerStateJson (plugin API v86) names the active server and the plugin that owns
// it; a reader (RynthOracle, RynthAi's "/ra server", ...) asks that plugin for this profile to get
// the server's rules without knowing the server: connection, rule set, botting policy, account
// limits, commands, tinkering salvage and links. Everything ServerPackInfo holds, nothing guessed.
//
// The JSON is plain ASCII (anything else is \u-escaped), so a reader can show it in the AC client.
// Pure (no host calls): RynthServerPack.Tests covers it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RynthCore.Plugin.ServerPack;

/// <summary>
/// Live numbers a server plugin publishes in its profile ("values"), for other plugins that apply a
/// server rule without knowing the server: e.g. RynthConquest's "voidDotDurationScale" (how much
/// longer this character's Void damage-over-time lasts there), which RynthOracle's Void timers use.
/// Set from the server's own code (ServerPackPlugin.SetProfileValue), read by the profile interface
/// on any thread; <see cref="Revision"/> goes up on every change, so the JSON is rebuilt only then.
/// </summary>
internal sealed class ServerProfileValues
{
    private readonly object _gate = new();
    private readonly List<KeyValuePair<string, double>> _values = new();
    private volatile int _revision;

    /// <summary>Goes up whenever a value changes.</summary>
    public int Revision => _revision;

    /// <summary>1-48 of letters, digits, '.', '_' and '-'.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 48) return false;
        foreach (char c in key)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) return false;
        return true;
    }

    /// <summary>Sets (or adds) a value; false for a bad key or a number that isn't finite. Any thread.</summary>
    public bool Set(string key, double value)
    {
        if (!IsValidKey(key) || !double.IsFinite(value)) return false;
        lock (_gate)
        {
            int i = _values.FindIndex(v => v.Key == key);
            if (i >= 0)
            {
                if (_values[i].Value.Equals(value)) return true;
                _values[i] = new KeyValuePair<string, double>(key, value);
            }
            else _values.Add(new KeyValuePair<string, double>(key, value));
            _revision++;
        }
        return true;
    }

    /// <summary>Drops one value (true when it was there). Any thread.</summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            int i = _values.FindIndex(v => v.Key == key);
            if (i < 0) return false;
            _values.RemoveAt(i);
            _revision++;
            return true;
        }
    }

    /// <summary>Drops every value (logout: the next character publishes its own). Any thread.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_values.Count == 0) return;
            _values.Clear();
            _revision++;
        }
    }

    /// <summary>A copy of the values and the revision it belongs to. Any thread.</summary>
    public KeyValuePair<string, double>[] Snapshot(out int revision)
    {
        lock (_gate)
        {
            revision = _revision;
            return _values.ToArray();
        }
    }
}

internal static class ServerPackProfile
{
    /// <summary>The profile JSON's schema (RynthServerProfile.Schema).</summary>
    public const int Schema = 1;

    /// <summary>The "botting" value for a policy.</summary>
    public static string BottingName(BottingPolicy policy) => policy switch
    {
        BottingPolicy.Allowed => "allowed",
        BottingPolicy.Restricted => "restricted",
        BottingPolicy.Forbidden => "forbidden",
        _ => "unknown",
    };

    /// <summary>
    /// The profile for <paramref name="info"/>: <paramref name="active"/> is this plugin's verdict
    /// (RynthCore's when it pushed one), <paramref name="world"/> the world name the client reports,
    /// <paramref name="values"/> the live numbers the server's own code published (ServerProfileValues).
    /// </summary>
    public static string BuildJson(ServerPackInfo info, string pluginName, string pluginVersion, bool active, string? world,
                                   IReadOnlyList<KeyValuePair<string, double>>? values = null)
    {
        var sb = new StringBuilder(1024);
        sb.Append("{\"schema\":").Append(Schema);
        Field(sb, "id", info.Id);
        Field(sb, "name", info.Name);
        Field(sb, "plugin", pluginName);
        Field(sb, "version", pluginVersion);
        sb.Append(",\"active\":").Append(active ? "true" : "false");
        Field(sb, "world", world ?? string.Empty);
        List(sb, "hosts", info.Hosts);
        List(sb, "worldNames", info.WorldNames);
        sb.Append(",\"port\":").Append(info.Port);
        Field(sb, "emulator", info.Emulator);
        Field(sb, "ruleset", info.Ruleset);
        Field(sb, "status", info.Status);
        Field(sb, "botting", BottingName(info.Botting));
        Field(sb, "bottingNote", info.BottingNote);
        Field(sb, "accountLimit", info.AccountLimit);
        Field(sb, "summary", info.Summary);
        List(sb, "facts", info.Facts);
        sb.Append(",\"commands\":[");
        for (int i = 0; i < info.Commands.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"command\":");
            Str(sb, info.Commands[i].Command);
            sb.Append(",\"what\":");
            Str(sb, info.Commands[i].What);
            sb.Append('}');
        }
        sb.Append(']');
        // Added within schema 1: readers that don't know "salvage" skip it. The optional row fields
        // (applied, prepared, order, afterCap, stackGroup + stackCap) are written only when set.
        sb.Append(",\"salvage\":[");
        for (int i = 0; i < info.Salvage.Length; i++)
        {
            ServerSalvage s = info.Salvage[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"bench\":");
            Str(sb, s.Bench);
            sb.Append(",\"material\":").Append(s.Material);
            sb.Append(",\"kind\":");
            Str(sb, s.Kind);
            sb.Append(",\"effect\":");
            Str(sb, s.Effect);
            sb.Append(",\"slot\":").Append(s.UsesTinkSlot ? "true" : "false");
            sb.Append(",\"nameMatch\":");
            Str(sb, s.NameMatch);
            if (s.AppliedInt != 0) sb.Append(",\"applied\":").Append(s.AppliedInt);
            if (s.Prepared) sb.Append(",\"prepared\":true");
            if (s.Order != 0) sb.Append(",\"order\":").Append(s.Order);
            if (s.AfterCap) sb.Append(",\"afterCap\":true");
            if (!string.IsNullOrEmpty(s.StackGroup))
            {
                sb.Append(",\"stackGroup\":");
                Str(sb, s.StackGroup);
                sb.Append(",\"stackCap\":").Append(s.StackCap);
            }
            sb.Append('}');
        }
        sb.Append(']');
        // Added within schema 1 (2026-10-20), only when the server has trinkets.
        if (info.TrinketNames.Length > 0)
        {
            sb.Append(",\"trinkets\":{");
            sb.Append("\"names\":[");
            for (int i = 0; i < info.TrinketNames.Length; i++) { if (i > 0) sb.Append(','); Str(sb, info.TrinketNames[i]); }
            sb.Append("],\"except\":[");
            for (int i = 0; i < info.TrinketExcept.Length; i++) { if (i > 0) sb.Append(','); Str(sb, info.TrinketExcept[i]); }
            sb.Append("]}");
        }
        Field(sb, "discord", info.Discord);
        Field(sb, "website", info.Website);
        Field(sb, "wiki", info.Wiki);
        Field(sb, "source", info.Source);
        // Added within schema 1: live numbers for this character on this server ({} when none).
        sb.Append(",\"values\":{");
        if (values != null)
        {
            bool first = true;
            foreach (KeyValuePair<string, double> v in values)
            {
                if (!ServerProfileValues.IsValidKey(v.Key) || !double.IsFinite(v.Value)) continue;
                if (!first) sb.Append(',');
                first = false;
                Str(sb, v.Key);
                sb.Append(':').Append(v.Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        sb.Append('}');
        sb.Append('}');
        return sb.ToString();
    }

    private static void Field(StringBuilder sb, string name, string? value)
    {
        sb.Append(",\"").Append(name).Append("\":");
        Str(sb, value);
    }

    private static void List(StringBuilder sb, string name, string[]? values)
    {
        sb.Append(",\"").Append(name).Append("\":[");
        if (values != null)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                Str(sb, values[i]);
            }
        }
        sb.Append(']');
    }

    /// <summary>A JSON string literal in plain ASCII (control and non-ASCII characters \u-escaped).</summary>
    private static void Str(StringBuilder sb, string? s)
    {
        sb.Append('"');
        foreach (char c in s ?? string.Empty)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c > 0x7e) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
