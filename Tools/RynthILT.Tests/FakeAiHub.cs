using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.Plugin.Shared;

namespace RynthILT.Tests;

/// <summary>
/// A stand-in for RynthAi's "RynthAi.Hub" table (Plugins/Shared/RynthAiHubApi.cs), so the tests can
/// check what RynthILT asks of RynthAi (RynthAiClient.UseTableForTests takes it). Only the functions
/// the moved features use answer anything; the rest are absent (null), as an old RynthAi's would be.
/// Reset() before each test that uses it.
/// </summary>
internal static unsafe class FakeAiHub
{
    /// <summary>BackAwayFrom calls (object, radius, hold), and what it answers.</summary>
    public static readonly List<(uint Id, float Radius, int HoldMs)> BackAways = new();
    public static int BackAwayResult = RynthAiHub.BackAwayStarted;
    /// <summary>SetPlayerCounter: the last value reported per counter id.</summary>
    public static readonly Dictionary<uint, long> Counters = new();
    /// <summary>SessionStats: the shared session's start (0 = no session: SessionStats answers 0).</summary>
    public static long SessionStartTicks;

    private static RynthAiHubApiV1* _table;

    public static void Reset()
    {
        BackAways.Clear();
        BackAwayResult = RynthAiHub.BackAwayStarted;
        Counters.Clear();
        SessionStartTicks = 0;
    }

    public static IntPtr Table
    {
        get
        {
            if (_table == null)
            {
                var t = (RynthAiHubApiV1*)NativeMemory.AllocZeroed((nuint)sizeof(RynthAiHubApiV1));
                t->StructSize = (uint)sizeof(RynthAiHubApiV1);
                t->BackAwayFrom = &BackAwayFrom;
                t->SetPlayerCounter = &SetPlayerCounter;
                t->SessionStats = &SessionStats;
                t->Version = RynthAiHubApiV1.InterfaceVersion;
                _table = t;
            }
            return (IntPtr)_table;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SessionStats(SessionStatsV1* s)
    {
        if (SessionStartTicks == 0 || s == null) return 0;
        *s = default;
        s->StartUtcTicks = SessionStartTicks;
        s->SessionSeconds = 60;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetPlayerCounter(uint id, long value)
    {
        Counters[id] = value;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int BackAwayFrom(uint id, float radius, int holdMs)
    {
        BackAways.Add((id, radius, holdMs));
        return BackAwayResult;
    }
}
