// ServerHubClient.cs - RynthAi's side of a server plugin's hub (the "RynthILT.Hub" table layout,
// Plugins/Shared/RynthIltHubApi.cs; was IltClient until 2026-10-20): the tabs, sub-tabs and sections
// the Mini Remote hosts for the server you're on, its feature bits (the Quests tab's /myquests bit),
// its texts and settings, its "/ra" verbs, and its pet gate. RynthAi knows no server's content: it
// shows what the plugin offers.
//
// The plugin is found by the engine's server list first (the active server's owner plugin, under the
// neutral name "RynthServer.Hub"), then RynthILT under "RynthILT.Hub" (every RynthILT answers that;
// older ones only that). Without one (not installed, too old, not loaded yet: any retail-like world)
// every call answers "nothing": no tab, no section, unknown feature bits, no verbs. The table is
// looked up on the pump thread (Tick) at most every RecheckMs and kept in a volatile field for the
// render thread; the plugin never frees it (Version goes to 0 on its shutdown), so reading a stale
// pointer is safe and a Version of 0 means "look again".
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using RynthCore.Plugin.Shared;
using RynthCore.PluginSdk;

namespace RynthCore.Plugin.RynthAi.MiniRemote;

/// <summary>One tab as the server plugin describes it (a copy).</summary>
internal readonly record struct ServerTabInfo(string Id, string Label, float Width, float Height, bool Shown, bool Available);

internal sealed unsafe class ServerHubClient
{
    private const long RecheckMs = 3000;
    private const int MaxTabs = 16;

    private readonly RynthCoreHost _host;
    private IntPtr _table;          // volatile reads/writes: the render thread uses what the pump found
    private long _checkedAt;
    private bool _checkedOnce;      // false: look now (a sentinel time would overflow "now - _checkedAt")
    private readonly IltTabV1[] _tabBuf = new IltTabV1[MaxTabs];
    private readonly List<ServerTabInfo> _tabs = new();

    private bool _pinned;           // tests: a table handed in, never looked up

    public ServerHubClient(RynthCoreHost host) => _host = host;

    /// <summary>Tests only: use <paramref name="table"/> (a fake server plugin's table) and never look it up.</summary>
    internal void UseTableForTests(IntPtr table)
    {
        _pinned = true;
        _checkedOnce = true;
        System.Threading.Volatile.Write(ref _table, table);
    }

    /// <summary>Pump thread: look the table up again when it's due, missing or shut down.</summary>
    public void Tick()
    {
        if (_pinned) return;
        long now = Environment.TickCount64;
        var t = (RynthIltHubApiV1*)System.Threading.Volatile.Read(ref _table);
        if (!LookupDue(_checkedOnce, _checkedAt, now, haveTable: t != null, tableLive: t != null && t->Version >= 1)) return;
        _checkedOnce = true;
        _checkedAt = now;
        IntPtr found = IntPtr.Zero;
        if (_host.HasGetPluginInterface)
        {
            // The active server's own plugin (the engine's server list), under the neutral name ...
            string owner = ActiveServerPlugin();
            if (owner.Length > 0) found = Look(owner, RynthIltHubApiV1.NeutralInterfaceName);
            // ... else RynthILT under its own name (every RynthILT; it also follows its own world check).
            if (found == IntPtr.Zero) found = Look(RynthIltHub.PluginName, RynthIltHubApiV1.InterfaceName);
        }
        System.Threading.Volatile.Write(ref _table, found);
    }

    /// <summary>A hub table <paramref name="plugin"/> serves as <paramref name="iface"/>, or 0.</summary>
    private IntPtr Look(string plugin, string iface)
    {
        var p = (RynthIltHubApiV1*)_host.GetPluginInterface(plugin, iface, RynthIltHubApiV1.InterfaceVersion);
        // Any table at least as big as the first release's: newer functions are checked with Has.
        return p != null && p->Version >= 1 && p->StructSize >= RynthIltHubApiV1.BaseStructSize ? (IntPtr)p : IntPtr.Zero;
    }

    /// <summary>The plugin that owns the engine's active server ("" when none, or the engine is pre-v86). Pump thread.</summary>
    private string ActiveServerPlugin()
    {
        try
        {
            if (!_host.HasGetServerStateJson || !_host.TryGetServerStateJson(out string json)) return string.Empty;
            return EngineServerState.Parse(json)?.ActivePlugin ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>Forget the table (login, logout); the next Tick looks again.</summary>
    public void Reset()
    {
        _pinned = false;
        System.Threading.Volatile.Write(ref _table, IntPtr.Zero);
        _checkedOnce = false;
    }

    /// <summary>
    /// Whether Tick looks the table up again. Pure. Always on the first call (and after Reset), at
    /// once when the table RynthILT gave is shut down (Version 0), else every <see cref="RecheckMs"/>.
    /// </summary>
    internal static bool LookupDue(bool checkedOnce, long checkedAt, long now, bool haveTable, bool tableLive)
    {
        if (!checkedOnce) return true;
        if (haveTable && !tableLive) return true;
        return now - checkedAt >= RecheckMs;
    }

    private RynthIltHubApiV1* T
    {
        get
        {
            var t = (RynthIltHubApiV1*)System.Threading.Volatile.Read(ref _table);
            return t != null && t->Version >= 1 ? t : null;
        }
    }

    /// <summary>A server plugin answers (any thread).</summary>
    public bool Loaded => T != null;

    /// <summary>This world is the plugin's server (or forced there), as it sees it (RynthILT: Infinite Leaftide). Any thread.</summary>
    public bool ServerActive
    {
        get
        {
            var t = T;
            return t != null && t->IsIltWorld != null && t->IsIltWorld() != 0;
        }
    }

    /// <summary>The server's bit for a feature: -1 unknown (and always off its server / without a plugin), 0 off, 1 on. Any thread.</summary>
    public int Feature(string key)
    {
        var t = T;
        if (t == null || t->GetFeature == null) return -1;
        using var k = new Utf8(key);
        return t->GetFeature(k.P);
    }

    /// <summary>The server positively reported the feature off.</summary>
    public bool IsOff(string key) => Feature(key) == 0;

    /// <summary>The server positively reported the feature on.</summary>
    public bool IsOn(string key) => Feature(key) == 1;

    /// <summary>
    /// A short text from the server plugin (RynthIltHub.Text*: the session report's tail, its server id);
    /// "" without one, from an older one, or for nothing. Any thread.
    /// </summary>
    public string Text(string id)
    {
        var t = T;
        if (t == null || !RynthIltHubApiV1.Has(t, RynthIltHubApiV1.GetTextOffset) || t->GetText == null) return string.Empty;
        byte* buf = stackalloc byte[256];
        buf[0] = 0;
        int n;
        using (var k = new Utf8(id)) n = t->GetText(k.P, buf, 256);
        if (n <= 0) return string.Empty;
        buf[255] = 0;
        return Marshal.PtrToStringUTF8((IntPtr)buf) ?? string.Empty;
    }

    /// <summary>
    /// This character forces the plugin's server (RynthILT: "Treat this world as ILT"; set there or in
    /// RynthCore's Options > Diagnostics, never by RynthAi). Any thread.
    /// </summary>
    public bool ServerForced
    {
        get
        {
            var t = T;
            return t != null && t->GetForceIlt != null && t->GetForceIlt() != 0;
        }
    }

    /// <summary>Render thread: the plugin's tabs with their flags (empty without one). The list is reused.</summary>
    public IReadOnlyList<ServerTabInfo> Tabs()
    {
        _tabs.Clear();
        var t = T;
        if (t == null || t->GetTabs == null) return _tabs;
        int n;
        fixed (IltTabV1* buf = _tabBuf) n = t->GetTabs(buf, MaxTabs);
        n = Math.Min(n, MaxTabs);
        for (int i = 0; i < n; i++)
        {
            ref IltTabV1 tab = ref _tabBuf[i];
            string id, label;
            fixed (byte* pid = tab.Id) id = Marshal.PtrToStringUTF8((IntPtr)pid) ?? string.Empty;
            fixed (byte* plabel = tab.Label) label = Marshal.PtrToStringUTF8((IntPtr)plabel) ?? string.Empty;
            if (id.Length == 0) continue;
            _tabs.Add(new ServerTabInfo(id, label.Length > 0 ? label : id, tab.Width, tab.Height,
                (tab.Flags & RynthIltHub.TabShown) != 0, (tab.Flags & RynthIltHub.TabAvailable) != 0));
        }
        return _tabs;
    }

    /// <summary>Render thread: one ILT tab's body into the current child window; false = nothing drawn.</summary>
    public bool RenderTab(string id)
    {
        var t = T;
        if (t == null || t->RenderTab == null) return false;
        using var s = new Utf8(id);
        return t->RenderTab(s.P) != 0;
    }

    /// <summary>Render thread: an ILT section (RynthIltHub.Section*); false = nothing drawn (not ILT, or no RynthILT).</summary>
    public bool RenderSection(string id)
    {
        var t = T;
        if (t == null || t->RenderSection == null) return false;
        using var s = new Utf8(id);
        return t->RenderSection(s.P) != 0;
    }

    /// <summary>An ILT "/ra" verb ("hub bank", "guardian handin", ...); false when RynthILT doesn't take it.</summary>
    public bool Command(string text)
    {
        var t = T;
        if (t == null || t->Command == null) return false;
        using var s = new Utf8(text);
        return t->Command(s.P) != 0;
    }

    /// <summary>
    /// The server plugin's answer to "may an EMPTY pet essence be summoned?" (the server refills it;
    /// RynthILT: ACECustom's Summon Essence Refill charm). False without one, or from an older one
    /// that doesn't answer: an empty essence is then never used. Any thread.
    /// </summary>
    public bool AllowSummonOnEmpty()
    {
        var t = T;
        return t != null && RynthIltHubApiV1.Has(t, RynthIltHubApiV1.AllowSummonOnEmptyOffset)
               && t->AllowSummonOnEmpty != null && t->AllowSummonOnEmpty() != 0;
    }

    /// <summary>One of the server plugin's mirrored settings (RynthIltHub.Setting*): its value, -1 when not offered. Any thread.</summary>
    public int Setting(string key)
    {
        var t = T;
        if (t == null || !RynthIltHubApiV1.Has(t, RynthIltHubApiV1.GetSettingOffset) || t->GetSetting == null) return -1;
        using var k = new Utf8(key);
        return t->GetSetting(k.P);
    }

    /// <summary>Changes one of those settings; false when the plugin doesn't take it. Any thread.</summary>
    public bool SetSetting(string key, int value)
    {
        var t = T;
        if (t == null || !RynthIltHubApiV1.Has(t, RynthIltHubApiV1.SetSettingOffset) || t->SetSetting == null) return false;
        using var k = new Utf8(key);
        return t->SetSetting(k.P, value) != 0;
    }

    private const int MaxSubTabs = 8;

    /// <summary>
    /// The sub-tabs the server plugin adds to one of the Mini Remote's tabs (<paramref name="parent"/>
    /// "pets": RynthILT's Breeding). Each id is a RenderSection id. Empty without one. A new list each
    /// call (the render thread draws them, a command looks one up). Any thread.
    /// </summary>
    public IReadOnlyList<ServerTabInfo> SubTabs(string parent)
    {
        var t = T;
        if (t == null || !RynthIltHubApiV1.Has(t, RynthIltHubApiV1.GetSubTabsOffset) || t->GetSubTabs == null)
            return Array.Empty<ServerTabInfo>();
        IltTabV1* buf = stackalloc IltTabV1[MaxSubTabs];
        int n;
        using (var p = new Utf8(parent)) n = t->GetSubTabs(p.P, buf, MaxSubTabs);
        n = Math.Min(n, MaxSubTabs);
        if (n <= 0) return Array.Empty<ServerTabInfo>();
        var list = new List<ServerTabInfo>(n);
        for (int i = 0; i < n; i++)
        {
            IltTabV1* tab = &buf[i];
            tab->Id[23] = 0;      // never read past a buffer the other side filled to the brim
            tab->Label[31] = 0;
            string id = Marshal.PtrToStringUTF8((IntPtr)tab->Id) ?? string.Empty;
            string label = Marshal.PtrToStringUTF8((IntPtr)tab->Label) ?? string.Empty;
            if (id.Length == 0) continue;
            list.Add(new ServerTabInfo(id, label.Length > 0 ? label : id, tab->Width, tab->Height,
                (tab->Flags & RynthIltHub.TabShown) != 0, (tab->Flags & RynthIltHub.TabAvailable) != 0));
        }
        return list;
    }

    /// <summary>A NUL-terminated UTF-8 copy of a string for one call.</summary>
    private readonly struct Utf8 : IDisposable
    {
        public readonly byte* P;

        public Utf8(string? s)
        {
            s ??= string.Empty;
            int n = Encoding.UTF8.GetByteCount(s);
            P = (byte*)NativeMemory.Alloc((nuint)(n + 1));
            fixed (char* c = s) Encoding.UTF8.GetBytes(c, s.Length, P, n);
            P[n] = 0;
        }

        public void Dispose() => NativeMemory.Free(P);
    }
}
