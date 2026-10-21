// IltSettingsUi.cs - The "ILT settings" view: RynthILT's copy of the Infinite Leaftide settings.
//
// Drawn in its own window ("/ilt settings"). Shows RynthCore's verdict and reason, this character's
// Auto / On / Off override, the ILT world names (edited through RynthCore), this character's ILT
// options (the settings that moved here from RynthAi on 2026-10-20: the Empyrean Handler ring
// evade, ...; saved in the Hub's state), and every ILT feature with the state RynthILT's Hub read
// from the server (IltSettingsModel has where each comes from).
// Edits are RynthCore commands, queued for the plugin tick; the next server-list poll shows the
// result. Render thread only.
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using RynthCore.Plugin.RynthILT.Hub;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthILT.IltSettingsView;

internal sealed class IltSettingsUi
{
    private static readonly Vector4 Teal = new(0.30f, 0.85f, 0.80f, 1f);
    private static readonly Vector4 Mute = new(0.60f, 0.62f, 0.66f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.55f, 0.45f, 1f);

    private const string WindowTitle = "ILT settings (RynthILT)##rynthilt_settings";

    private readonly IltSettingsModel _model;
    private readonly Action<string> _runCommand;
    private readonly Func<bool> _refreshFeatures;
    private readonly Func<IltHubController?> _hub;
    private string _newName = string.Empty;
    private string _status = string.Empty;

    /// <summary>The standalone window is open ("/ilt settings").</summary>
    public bool WindowOpen;

    /// <param name="runCommand">Queues a RynthCore command (run on the plugin tick).</param>
    /// <param name="refreshFeatures">Asks the Hub to read the server's features again; false before it exists.</param>
    /// <param name="hub">This character's Hub (its options live in the Hub's state); null before login.</param>
    public IltSettingsUi(IltSettingsModel model, Action<string> runCommand, Func<bool> refreshFeatures, Func<IltHubController?>? hub = null)
    {
        _model = model;
        _runCommand = runCommand;
        _refreshFeatures = refreshFeatures;
        _hub = hub ?? (() => null);
    }

    /// <summary>The standalone window, while open.</summary>
    public void RenderWindow()
    {
        if (!WindowOpen) return;
        ImGui.SetNextWindowSize(new Vector2(440, 480), ImGuiCond.FirstUseEver);
        bool open = WindowOpen;
        if (ImGui.Begin(WindowTitle, ref open)) RenderBody();
        ImGui.End();
        WindowOpen = open;
    }

    /// <summary>The view's content, into the current window.</summary>
    public void RenderBody()
    {
        RenderVerdict();
        ImGui.Separator();
        RenderCharacterOptions();
        ImGui.Separator();
        RenderFeatures();
        if (_status.Length > 0)
        {
            ImGui.Separator();
            SafeImGui.TextWrapped(_status);
        }
    }

    private void RenderVerdict()
    {
        IltEngineView? engine = _model.Engine;
        if (engine == null)
        {
            ImGui.TextWrapped("Waiting for RynthCore's server list (it needs plugin API v86). Until then RynthILT's own "
                              + "world list and this character's 'Treat this world as ILT' (/ra hub force on|off) decide.");
            return;
        }

        IltServerView? s = engine.Server;
        if (s == null)
        {
            ImGui.TextDisabled("RynthCore doesn't list Infinite Leaftide.");
        }
        else
        {
            SafeImGui.TextColored(s.Active ? Teal : Mute, s.Active ? s.Name + ": on for this world" : s.Name + ": not this world");
            if (s.Reason.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Mute);
                SafeImGui.TextWrapped(s.Reason);
                ImGui.PopStyleColor();
            }

            // Read only: a server is forced in one place, RynthCore Options > Diagnostics (2026-10-13).
            string force = s.Force switch { "on" => "forced on", "off" => "forced off", _ => "auto (detected)" };
            SafeImGui.TextDisabled("This character: " + force + "  (change it in RynthCore Options > Diagnostics)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("RynthCore's server selection (Options > Diagnostics) forces a server for this character,\nor /rc server ilt auto|on|off in chat.");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("ILT world names");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Worlds whose name matches one of these count as Infinite Leaftide ('*' matches any text).\nThe launch host is checked too. Kept by RynthCore for every character.");
        ImGui.SameLine();
        if (ImGui.SmallButton("List in chat##iltnames"))
        {
            _runCommand(IltSettingsModel.NamesListCommand);
            _status = "RynthCore lists the names in chat (" + IltSettingsModel.NamesListCommand + ").";
        }
        ImGui.SetNextItemWidth(220);
        bool enter = ImGui.InputTextWithHint("##iltnewname", "world name, * = any text", ref _newName, (uint)IltSettingsModel.MaxPattern,
                                             ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Add##iltname") || enter)
        {
            string cmd = IltSettingsModel.NamesCommand(true, _newName, out string err);
            if (Send(cmd, err, "Adding '" + _newName.Trim() + "'...")) _newName = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove##iltname"))
        {
            string cmd = IltSettingsModel.NamesCommand(false, _newName, out string err);
            if (Send(cmd, err, "Removing '" + _newName.Trim() + "'...")) _newName = string.Empty;
        }
    }

    /// <summary>
    /// This character's Infinite Leaftide options: the ones that were RynthAi settings until
    /// 2026-10-20 (copied over once). Saved with the Hub's state.
    /// </summary>
    private void RenderCharacterOptions()
    {
        ImGui.TextUnformatted("This character");
        IltHubController? hub = _hub();
        if (hub == null)
        {
            ImGui.TextDisabled("Shown once this character is logged in.");
            return;
        }
        IltHubState st = hub.State;
        bool vaulted = st.VaultedDungeonsEnabled;
        if (ImGui.Checkbox("Vaulted Dungeons (the keeper's master switch)##iltvaulted", ref vaulted)) st.VaultedDungeonsEnabled = vaulted;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Off by default. While off the Vaulted Dungeons keeper does nothing: no recalls, no /rc quit, no Mini\n"
                             + "Remote tab. On: the \"Vaulted Dungeons\" tab shows, where you pick a dungeon and a tied Portal Recall\n"
                             + "Gem and start it (/ilt vaulted on). Also in RynthCore's Settings > Navigation (it was a RynthAi setting).");
        bool ring = st.Combat.HandlerRingEvade;
        if (ImGui.Checkbox("Evade the Empyrean Handler's lethal ring##iltring", ref ring)) st.Combat.HandlerRingEvade = ring;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When an Empyrean Handler channels its lethal ring and you are within 12 yards (or RynthAi's\n"
                             + "Ring Range if larger), RynthAi back-jumps away and re-engages. Only while RynthAi's macro\n"
                             + "is running with combat on. On by default (it was RynthAi's \"Evade Handler Ring\").");
        int augs = st.T11.ItemAugsOverride;
        SafeImGui.TextDisabled(augs >= 0
            ? $"T11 item augmentations: fixed at {augs:N0} for RynthAi's loot rules (/ilt t11 augs auto to read /aug)"
            : "T11 item augmentations: read from /aug (/ilt t11 augs <n> fixes them; /ilt t11 shows the counters)");
    }

    /// <summary>
    /// The server's ILT features, one collapsible section per group (Character, Items, Pets,
    /// Fellowship, Combat, Activities, then the charms and any other bit chat or a probe set), with
    /// how many are on and each feature's state; the tooltip says where the Rynth tools handle it.
    /// </summary>
    private void RenderFeatures()
    {
        IltFeatureView? f = _model.Features;
        ImGui.TextUnformatted("Server features");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh##iltfeatures"))
            _status = _refreshFeatures() ? "Checking the server again..." : "Not ready yet (log in first).";
        if (f == null)
        {
            ImGui.TextWrapped("RynthILT reads which ILT features the server has on once this character is logged in. Not read yet.");
            return;
        }

        string when = f.Checking ? "checking the server..." : f.LastCheck.Length == 0 ? "not checked yet" : "checked " + f.LastCheck;
        SafeImGui.TextDisabled((f.World.Length > 0 ? f.World : "unknown world") + "  -  " + when
                               + (f.Source.Length > 0 ? "  (" + f.Source + ")" : string.Empty));
        if (!f.IltLike) ImGui.TextDisabled("This world isn't treated as Infinite Leaftide, so the server isn't asked.");
        if (f.Features.Length == 0)
        {
            ImGui.TextDisabled("The server hasn't reported any ILT features yet.");
            return;
        }

        var groups = new Dictionary<string, List<(string Label, string Tooltip, int State)>>(StringComparer.Ordinal);
        foreach (var kv in f.Features)
        {
            var (label, group, tooltip) = IltFeatureCatalog.Describe(kv.Key);
            if (!groups.TryGetValue(group, out var rows)) groups[group] = rows = new();
            rows.Add((label, tooltip, kv.Value));
        }
        foreach (string group in FeatureGroups())
        {
            if (!groups.TryGetValue(group, out var rows)) continue;
            int on = 0, off = 0;
            foreach (var r in rows) { if (r.State == 1) on++; else if (r.State == 0) off++; }
            if (!ImGui.CollapsingHeader($"{group}  ({on} of {rows.Count} on)##iltfg_{group}")) continue;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                SafeImGui.TextColored(r.State == 1 ? Teal : r.State == 0 ? Warn : Mute, IltSettingsModel.StateText(r.State));
                ImGui.SameLine(80);
                SafeImGui.Text(r.Label);
                if (ImGui.IsItemHovered()) SafeImGui.SetTooltip(r.Tooltip);
            }
        }
    }

    /// <summary>The feature sections in display order: the catalog's groups, then Charms and Other.</summary>
    private static IEnumerable<string> FeatureGroups()
    {
        foreach (string g in IltFeatureCatalog.Groups) yield return g;
        yield return IltFeatureCatalog.GroupCharms;
        yield return IltFeatureCatalog.GroupOther;
    }

    /// <summary>Queues <paramref name="command"/>, or shows <paramref name="error"/> when it is empty.</summary>
    private bool Send(string command, string error, string pending)
    {
        if (command.Length == 0)
        {
            _status = error;
            return false;
        }
        _runCommand(command);
        _status = pending;
        return true;
    }
}
