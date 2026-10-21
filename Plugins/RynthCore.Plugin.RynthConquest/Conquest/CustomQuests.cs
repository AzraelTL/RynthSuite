// Ported from Oracle of Dereth by Advis Eveldan (advis61), MIT. See THIRD_PARTY_NOTICES.md.
// Moved here from RynthOracle (Data/CustomQuests.cs) on 2026-10-20, with Resources\customquests.csv.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RynthCore.Plugin.RynthOracle.Data;

namespace RynthCore.Plugin.RynthConquest;

/// <summary>
/// Conquest's custom quest worklist (customquests.csv: Server, Name, QuestFlag, Url, Hint; every row
/// is Conquest's), upstream's Custom tab. The status comes from the character's quest flags
/// (/myquests, read with RynthOracle's parser, linked source). A repeatable counts as done only
/// while on cooldown (upstream's rule).
/// </summary>
internal sealed class CustomQuest
{
    public string Server = "", Name = "", Flag = "", Url = "", Hint = "";

    public bool IsComplete(QuestFlagStore flags, DateTime utcNow)
    {
        if (!flags.TryGet(Flag, out QuestFlag q)) return false;
        if (q.RepeatTime == TimeSpan.Zero) return q.Solves > 0;
        return !q.Ready(utcNow);
    }

    public string Status(QuestFlagStore flags, DateTime utcNow)
    {
        if (!flags.TryGet(Flag, out QuestFlag q)) return "ready";
        if (q.RepeatTime == TimeSpan.Zero) return q.Solves > 0 ? "completed" : "ready";
        return q.NextAvailable(utcNow);
    }
}

internal static class CustomQuestList
{
    /// <summary>The embedded list's logical name (RynthCore.Plugin.RynthConquest.csproj).</summary>
    public const string ResourceName = "RynthConquest.customquests.csv";

    public static List<CustomQuest> All { get; } = Load();

    private static List<CustomQuest> Load()
    {
        var list = new List<CustomQuest>();
        Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return list;
        using var r = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        r.ReadLine(); // Server,Name,QuestFlag,Url,Hint
        while (r.ReadLine() is string line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] f = Csv.ParseLine(line);
            list.Add(new CustomQuest
            {
                Server = Csv.Field(f, 0), Name = Csv.Field(f, 1), Flag = Csv.Field(f, 2).ToLowerInvariant(),
                Url = Csv.Field(f, 3), Hint = Csv.Field(f, 4),
            });
        }
        return list;
    }
}
