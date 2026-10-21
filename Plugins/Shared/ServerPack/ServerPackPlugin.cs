// ServerPackPlugin.cs - The shared body of every server plugin (RynthColdeve, RynthAelrynth, ...),
// built like RynthILT: one plugin per game server, so each one ships and updates on its own.
//
// What it does for its server:
//   * declares the server to RynthCore (RynthPluginGetSectionsJson "servers": its hosts and world
//     names), so RynthCore detects it and puts this plugin's views in the right Mini Remote spots
//     (contributions with "server": <id> show only while that server is active);
//   * shows the server card (ServerPackUi): RynthCore's verdict, this character's override, what is
//     known about the server, and how to set an account up for it in the launcher;
//   * stays quiet anywhere else. RynthCore usually doesn't even load it there: the embedded manifest
//     names the server's hosts, and the engine loads only the plugin for the host the client
//     connects to (PluginManifestGate.SelectServerPlugins).
//
// Server-specific features: add files to the server's project implementing the partial hooks below
// (ServerLoginComplete, ServerTick, ServerChatCommand, ...). Unimplemented hooks compile away.
//
// What the shared code gives a server's own features (added 2026-10-20, when RynthOracle's and
// RynthInventory's Conquest and Aelrynth features moved into RynthConquest and RynthAelrynth, so a
// shared plugin carries nothing for one server):
//   ServerPages(list)          pages of its own (ServerPackPages.cs): sub-tabs of the Mini Remote's
//                              Server section, only while its server is active, and tabs of the
//                              /rserver window. Draw runs on the render thread.
//   ServerChatText(...)        every incoming chat line (pump thread), to read the server's replies
//                              and hide the ones answering the plugin's own requests (eat).
//   ServerChatBar(text)        every line sent through the chat bar (AC's thread: note it only),
//                              to tell the player's typed command from the plugin's own.
//   Settings                  the plugin's own settings file (ServerPackSettings.cs), loaded before
//                              ServerInitialize; MigrateOnce imports a setting from its old home.
//   SetProfileValue(key, n)    a live number in its "RynthServer.Profile" ("values"), for other
//                              plugins that apply a server rule without knowing the server.
//   ServerCommandVerbs(list)   its own "/rserver <verb>" words, handled in ServerChatCommand.
//   RunOnTick(action)          work for the next tick (a page's button, on the render thread).
//   Send(command)              a chat command as if typed (pump thread, logged in only);
//   LogTagged(text)            the engine log, "[RynthConquest] ..."; ChatLine(text) the chat window.
// The tick, chat and command hooks only run while a character is logged in; each
// should check Active itself, since the plugin can be loaded for a world that isn't its server.
//
// Threads: OnTick, login/logout, chat lines and the engine's export calls run on the plugin pump.
// OnChatBarEnter runs on AC's main thread, so it only queues. The card and the pages draw on the
// render thread (OnRender, RynthPluginRenderEmbed) and queue their commands for the tick.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using RynthCore.PluginCore;

namespace RynthCore.Plugin.ServerPack;

public sealed partial class ServerPackPlugin : RynthPluginBase
{
    internal static readonly IntPtr NamePointer = Marshal.StringToHGlobalAnsi(RynthPluginInfo.Name);
    internal static readonly IntPtr VersionPointer = Marshal.StringToHGlobalAnsi(
        RynthPluginInfo.Version.Length > 0 ? RynthPluginInfo.Version : "0.1.0");

    /// <summary>The chat command every server plugin answers ("/rserver [show|hide|toggle|info]").</summary>
    internal const string ChatCommand = "/rserver";

    /// <summary>The card's surface in RynthCore's Mini Remote sections ("section:server-coldeve").</summary>
    internal static readonly string SettingsSurface = ServerPackModel.SurfaceFor(RynthPluginInfo.ServerId);

    private readonly ServerPackModel _model;
    private readonly ServerPackUi _ui;
    private readonly string _sectionsJson;
    // The server's own pages (ServerPages), validated; what was left out is logged at Initialize.
    private readonly List<ServerPage> _pages;
    private readonly List<string> _pageNotes = new();
    private readonly ServerPackSettings _settings = new(ServerPackSettings.DefaultPath(RynthPluginInfo.Name));
    private readonly ServerProfileValues _profileValues = new();
    // RynthCore commands from the card (render thread), run on the next tick.
    private readonly ConcurrentQueue<string> _engineCommands = new();
    // "/rserver ..." lines from OnChatBarEnter, run on the next tick.
    private readonly ConcurrentQueue<string[]> _commands = new();
    // The server code's work for the tick (RunOnTick: its pages' buttons).
    private readonly ConcurrentQueue<Action> _tickActions = new();
    // The server's own "/rserver" verbs (ServerCommandVerbs).
    private readonly HashSet<string> _serverVerbs = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _loginComplete;
    private volatile string? _world;
    private bool _wasActive;

    public ServerPackPlugin()
    {
        ServerPackInfo info = ServerData.Create();
        _model = new ServerPackModel(info);
        var pages = new List<ServerPage>();
        ServerPages(pages);
        _pages = ServerPackPages.Validate(pages, _pageNotes.Add);
        var verbs = new List<string>();
        ServerCommandVerbs(verbs);
        foreach (string v in verbs) if (!string.IsNullOrWhiteSpace(v)) _serverVerbs.Add(v.Trim());
        _ui = new ServerPackUi(_model, RynthPluginInfo.Name + " " + Marshal.PtrToStringAnsi(VersionPointer), () => _world, _pages);
        _sectionsJson = ServerPackModel.BuildSectionsJson(info, RynthPluginInfo.Name, _pages);
    }

    internal ServerPackInfo Info => _model.Info;

    private static ServerPackInfo? _staticInfo;

    /// <summary>The server's facts before the plugin object exists ("RynthServer.Profile" asked early).</summary>
    internal static ServerPackInfo StaticInfo => _staticInfo ??= ServerData.Create();

    /// <summary>RynthCore (or the world name) says this is the plugin's server now.</summary>
    internal bool Active => _loginComplete && _model.IsActive(_world);

    /// <summary>The world name the client reports ("" before login). Any thread.</summary>
    internal string World => _world ?? string.Empty;

    // ── Hooks for a server's own features (implement in the server's project) ──

    partial void ServerInitialize();
    partial void ServerLoginComplete();
    partial void ServerLogout();
    partial void ServerShutdown();
    /// <summary>Every plugin tick while logged in; <paramref name="active"/> = this is the plugin's server.</summary>
    partial void ServerTick(bool active);
    /// <summary>"/rserver &lt;verb&gt; ..." verbs the shared code doesn't know; set handled to stop the help text.</summary>
    partial void ServerChatCommand(string[] parts, ref bool handled);
    /// <summary>
    /// The server's own "/rserver" verbs (lower case), so "/rserver vault" reaches ServerChatCommand
    /// instead of being read as another server's id. Called once, from the constructor.
    /// </summary>
    partial void ServerCommandVerbs(List<string> verbs);
    /// <summary>Extra lines for "/rserver info".</summary>
    partial void ServerInfoLines(Action<string> line);
    /// <summary>The server's own pages (ServerPackPages.cs). Called once, from the constructor (no Host yet).</summary>
    partial void ServerPages(List<ServerPage> pages);
    /// <summary>
    /// An incoming chat line (pump thread, logged in only): set <paramref name="eat"/> to keep it out of
    /// the chat window (it comes in true when an earlier plugin already ate it).
    /// </summary>
    partial void ServerChatText(string text, int chatType, ref bool eat);
    /// <summary>
    /// A line sent through the chat bar, typed or another plugin's command (trimmed), before
    /// "/rserver" is looked at. AC's main thread: only note it (interlocked state or a queue).
    /// </summary>
    partial void ServerChatBar(string text);

    // ── What the server's own code uses ──────────────────────────────────

    /// <summary>The plugin's settings file (loaded before ServerInitialize). Pump thread.</summary>
    internal ServerPackSettings Settings => _settings;

    /// <summary>The live numbers of the profile ("values"). Any thread.</summary>
    internal ServerProfileValues ProfileValues => _profileValues;

    /// <summary>The pages this plugin draws.</summary>
    internal IReadOnlyList<ServerPage> Pages => _pages;

    /// <summary>Publishes a live number in the "RynthServer.Profile" JSON ("values"). Any thread.</summary>
    internal void SetProfileValue(string key, double value) => _profileValues.Set(key, value);

    /// <summary>Runs <paramref name="action"/> on the next tick while logged in (dropped at logout). Any thread.</summary>
    internal void RunOnTick(Action action)
    {
        if (action != null) _tickActions.Enqueue(action);
    }

    /// <summary>Sends a chat command as if typed; false when nothing went out. Pump thread.</summary>
    internal bool Send(string command)
        => _loginComplete && !string.IsNullOrWhiteSpace(command) && Host.HasInvokeChatParser && Host.InvokeChatParser(command);

    /// <summary>The plugin's log, tagged with its name.</summary>
    internal void LogTagged(string message) => Host.Log($"[{RynthPluginInfo.Name}] {message}");

    public override int Initialize()
    {
        Host.Log($"[{RynthPluginInfo.Name}] Plugin initialized for {Info.Name} ({Info.Id}).");
        // The server card draws through the engine's cimgui (and so in its current context, a popped-out
        // Mini Remote's included), never a second copy of ImGui (ImGuiNativeBinding.cs). Found and fixed
        // by SilentKelpie (rynth PR #12); re-implemented here.
        RynthCore.Plugin.Shared.ImGuiNativeBinding.Ensure(msg => Host.Log($"[{RynthPluginInfo.Name}] {msg}"));
        foreach (string note in _pageNotes) LogTagged(note);
        _settings.Load();
        if (_settings.LastError.Length > 0) LogTagged($"Settings ({_settings.FilePath}) not read: {_settings.LastError}; using defaults.");
        ServerInitialize();
        return 0;
    }

    // Also called on a freshly (re)loaded plugin while already in game.
    public override void OnLoginComplete()
    {
        _loginComplete = true;
        _wasActive = false;
        RefreshWorld();
        ServerLoginComplete();
    }

    public override void OnLogout()
    {
        _loginComplete = false;
        _world = null;
        while (_commands.TryDequeue(out _)) { }
        while (_engineCommands.TryDequeue(out _)) { }
        while (_tickActions.TryDequeue(out _)) { }
        _model.Clear();
        _profileValues.Clear();
        ServerLogout();
    }

    public override void Shutdown() => ServerShutdown();

    public override void OnTick()
    {
        if (!_loginComplete) return;
        if (_world == null) RefreshWorld();
        while (_commands.TryDequeue(out string[]? parts)) RunCommand(parts);
        while (_engineCommands.TryDequeue(out string? command))
        {
            if (!Host.HasInvokeChatParser || !Host.InvokeChatParser(command))
                ChatLine($"[{RynthPluginInfo.Name}] RynthCore didn't take: " + command);
        }
        while (_tickActions.TryDequeue(out Action? action))
        {
            try { action(); }
            catch (Exception ex) { LogTagged($"A queued action failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        bool active = Active;
        if (active && !_wasActive && Info.Botting == BottingPolicy.Forbidden)
            ChatLine($"[{RynthPluginInfo.Name}] {Info.Name} forbids bots: don't run RynthAi's combat, navigation or looting here.");
        _wasActive = active;
        ServerTick(active);
    }

    /// <summary>The world name the client reports (the server's own once logged in).</summary>
    private void RefreshWorld()
    {
        if (Host.HasGetWorldName && Host.TryGetWorldName(out string w) && !string.IsNullOrWhiteSpace(w))
            _world = w.Trim();
    }

    // ── RynthCore's verdict ──────────────────────────────────────────────

    /// <summary>RynthPluginApplyRemoteCommand: "servermenu" from RynthCore. Any thread: parsed and published only.</summary>
    internal void ApplyRemoteCommand(string action, string value)
    {
        if (action.Equals("servermenu", StringComparison.OrdinalIgnoreCase))
            _model.ApplyEngine(value, Environment.TickCount64);
    }

    /// <summary>
    /// RynthPluginGetSectionsJson: the server and the card's Mini Remote spot. Always declared
    /// (the manifest is static): the engine only collects it once in game, and keeping it across
    /// logout and character switches stops the server dropping out of Options > Diagnostics.
    /// </summary>
    internal string? BuildSectionsJson() => _sectionsJson;

    // ── Drawing ──────────────────────────────────────────────────────────

    /// <summary>The standalone card window. Render thread.</summary>
    public override void OnRender()
    {
        if (!_loginComplete || !_ui.WindowOpen || Host.ImGuiContext == IntPtr.Zero) return;
        IntPtr previous = ImGui.GetCurrentContext();
        ImGui.SetCurrentContext(Host.ImGuiContext);
        try { _ui.RenderWindow(); }
        catch (Exception ex) { Host.Log($"[{RynthPluginInfo.Name}] Server window failed: {ex.Message}"); _ui.WindowOpen = false; }
        finally { ImGui.SetCurrentContext(previous); }
    }

    /// <summary>
    /// RynthPluginRenderEmbed "section:server-&lt;id&gt;": the card in RynthCore's Mini Remote, drawn
    /// into the engine's current window and context (popped-out windows have their own).
    /// <paramref name="wantHeight"/> is the height it used. False until login. Render thread.
    /// </summary>
    internal bool RenderEmbeddedCard(out float wantHeight) => RenderEmbedded(_ui.RenderBody, "Server card", out wantHeight);

    /// <summary>
    /// RynthPluginRenderEmbed "section:server-&lt;id&gt;.&lt;page&gt;": one of the server's pages
    /// (ServerPages) in RynthCore's Mini Remote. False for a page this plugin doesn't have, or before login.
    /// </summary>
    internal bool RenderEmbeddedPage(string pageId, out float wantHeight)
    {
        wantHeight = 0;
        ServerPage? page = _pages.Find(p => p.Id == pageId);
        return page != null && RenderEmbedded(page.Draw, "Page " + page.Label, out wantHeight);
    }

    private bool RenderEmbedded(Action draw, string what, out float wantHeight)
    {
        wantHeight = 0;
        if (!_loginComplete) return false;
        IntPtr previous = ImGui.GetCurrentContext();
        IntPtr context = previous != IntPtr.Zero ? previous : Host.ImGuiContext;
        if (context == IntPtr.Zero) return false;
        ImGui.SetCurrentContext(context);
        try
        {
            float startY = ImGui.GetCursorPosY();
            draw();
            wantHeight = MathF.Max(40f, ImGui.GetCursorPosY() - startY + 4f);
            return true;
        }
        catch (Exception ex)
        {
            Host.Log($"[{RynthPluginInfo.Name}] {what} failed: {ex.Message}");
            return false;
        }
        finally { ImGui.SetCurrentContext(previous); }
    }

    // ── Chat ─────────────────────────────────────────────────────────────

    /// <summary>Incoming chat (pump thread): the server's own code reads it (ServerChatText).</summary>
    public override void OnChatWindowText(string? text, int chatType, ref int eat)
    {
        if (!_loginComplete || string.IsNullOrEmpty(text)) return;
        bool eaten = eat != 0;
        ServerChatText(text, chatType, ref eaten);
        if (eaten) eat = 1;
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public override void OnChatBarEnter(string? text, ref int eat)
    {
        if (!_loginComplete || string.IsNullOrEmpty(text)) return;
        string trimmed = text.Trim();
        ServerChatBar(trimmed);
        if (!trimmed.Equals(ChatCommand, StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith(ChatCommand + " ", StringComparison.OrdinalIgnoreCase))
            return;
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "/rserver <id> ...": only that server's plugin answers (several load when the host is unknown).
        if (parts.Length >= 2 && ServerPackModel.IsSafeId(parts[1].ToLowerInvariant()) && !IsVerb(parts[1]))
        {
            if (!parts[1].Equals(Info.Id, StringComparison.OrdinalIgnoreCase)) return;
            parts = Shift(parts);
        }
        eat = 1;
        _commands.Enqueue(parts);
    }

    private bool IsVerb(string word) =>
        word.ToLowerInvariant() is "show" or "hide" or "toggle" or "info" or "help" or "on" or "off" or "auto"
        || _serverVerbs.Contains(word);

    /// <summary>Drops the server id: "/rserver coldeve info" -> "/rserver info".</summary>
    private static string[] Shift(string[] parts)
    {
        var rest = new string[parts.Length - 1];
        rest[0] = parts[0];
        Array.Copy(parts, 2, rest, 1, parts.Length - 2);
        return rest;
    }

    private void RunCommand(string[] parts)
    {
        string verb = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "toggle";
        switch (verb)
        {
            case "show":
                _ui.WindowOpen = true;
                return;
            case "hide":
                _ui.WindowOpen = false;
                return;
            case "toggle":
                _ui.WindowOpen = !_ui.WindowOpen;
                return;
            case "auto":
            case "on":
            case "off":
                _engineCommands.Enqueue(ServerPackModel.ForceCommand(Info.Id, verb));
                return;
            case "info":
                PrintInfo();
                return;
        }

        bool handled = false;
        ServerChatCommand(parts, ref handled);
        if (handled) return;
        ChatLine($"[{RynthPluginInfo.Name}] {ChatCommand} [show|hide|toggle] - the {Info.Name} server card (also in the Mini Remote's Char section).");
        ChatLine($"[{RynthPluginInfo.Name}] {ChatCommand} info - what is known about {Info.Name}; {ChatCommand} auto|on|off - this character's override.");
    }

    private void PrintInfo()
    {
        string tag = $"[{RynthPluginInfo.Name}] ";
        ServerVerdictView? v = _model.Engine;
        string verdict = v != null
            ? (v.Active ? "on for this world" : "not this world") + (v.Reason.Length > 0 ? " (" + v.Reason + ")" : "")
            : (_model.IsActive(_world) ? "this world, by its name" : "not this world, by its name") + " (no RynthCore verdict yet)";
        ChatLine(tag + Info.Name + ": " + verdict);
        if (Info.Address.Length > 0) ChatLine(tag + "Address " + Info.Address + " (" + Info.Emulator + ", " + Info.Ruleset + ")");
        if (Info.AccountLimit.Length > 0) ChatLine(tag + "Account limit: " + Info.AccountLimit);
        ChatLine(tag + "Botting: " + Info.BottingText);
        ServerInfoLines(line => ChatLine(tag + line));
    }

    internal void ChatLine(string text)
    {
        if (Host.HasWriteToChat) Host.WriteToChat(text, 1);
    }
}
