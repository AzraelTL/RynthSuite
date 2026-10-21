// ServerPackSettings.cs - A server plugin's own settings file, %APPDATA%\RynthCore\<plugin>.json
// (rynthconquest.json, ...), for the server's features (ServerPackPlugin.Settings).
//
// Flat named values (true/false, text), written by hand and read with JsonDocument: plugin types
// never go through System.Text.Json's serializer, which roots them across hot reloads. Values the
// plugin doesn't ask for are kept as they were written, so an older or newer copy of the plugin
// never drops another's. MigrateOnce imports settings that used to live elsewhere (another
// plugin's file) exactly once, and remembers that it did.
// Pure (file I/O only): RynthServerPack.Tests covers it. Pump thread only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RynthCore.Plugin.ServerPack;

internal sealed class ServerPackSettings
{
    // name -> the value's JSON text, in file order.
    private readonly List<KeyValuePair<string, string>> _values = new();

    public ServerPackSettings(string path) => FilePath = path;

    /// <summary>%APPDATA%\RynthCore\&lt;plugin name, lower case&gt;.json.</summary>
    public static string DefaultPath(string pluginName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", pluginName.ToLowerInvariant() + ".json");

    public string FilePath { get; }

    /// <summary>Why the last Load or Save failed ("" when it didn't).</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Reads the file; a missing or broken one means defaults (the next save rewrites it).</summary>
    public void Load()
    {
        _values.Clear();
        LastError = string.Empty;
        try
        {
            if (!File.Exists(FilePath)) return;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { LastError = "not a JSON object"; return; }
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                Put(p.Name, p.Value.GetRawText());
        }
        catch (Exception ex)
        {
            _values.Clear();
            LastError = ex.Message;
        }
    }

    /// <summary>Writes the file (through a temporary file, so a crash never leaves half of one). False on failure.</summary>
    public bool Save()
    {
        try
        {
            var sb = new StringBuilder("{\n");
            for (int i = 0; i < _values.Count; i++)
            {
                sb.Append("  ").Append(Quote(_values[i].Key)).Append(": ").Append(_values[i].Value);
                sb.Append(i + 1 < _values.Count ? ",\n" : "\n");
            }
            sb.Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, FilePath, overwrite: true);
            LastError = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public bool Has(string name) => Find(name) >= 0;

    public bool GetBool(string name, bool fallback)
    {
        int i = Find(name);
        return i < 0 ? fallback : _values[i].Value switch { "true" => true, "false" => false, _ => fallback };
    }

    /// <summary>Sets a value; true when it changed (the caller saves).</summary>
    public bool SetBool(string name, bool value) => Put(name, value ? "true" : "false");

    public string GetString(string name, string fallback)
    {
        int i = Find(name);
        if (i < 0) return fallback;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(_values[i].Value);
            return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() ?? fallback : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    public bool SetString(string name, string value) => Put(name, Quote(value ?? string.Empty));

    /// <summary>
    /// Runs <paramref name="migrate"/> once per settings file: unless <paramref name="marker"/> is
    /// already set, it runs, the marker is set and the file saved. True when it ran now.
    /// </summary>
    public bool MigrateOnce(string marker, Action<ServerPackSettings> migrate)
    {
        if (GetBool(marker, false)) return false;
        migrate(this);
        SetBool(marker, true);
        Save();
        return true;
    }

    /// <summary>
    /// A true/false value from another JSON settings file (an old home of a moved setting), or null
    /// when the file, the value or its type isn't there. Never throws.
    /// </summary>
    public static bool? ReadBoolFrom(string path, string name)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(name, out JsonElement v)) return null;
            return v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        }
        catch
        {
            return null;
        }
    }

    private int Find(string name)
    {
        for (int i = 0; i < _values.Count; i++)
            if (string.Equals(_values[i].Key, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    private bool Put(string name, string json)
    {
        int i = Find(name);
        if (i >= 0)
        {
            if (_values[i].Value == json) return false;
            _values[i] = new KeyValuePair<string, string>(name, json);
            return true;
        }
        _values.Add(new KeyValuePair<string, string>(name, json));
        return true;
    }

    private static string Quote(string s) => "\"" + JsonEncodedText.Encode(s).ToString() + "\"";
}
