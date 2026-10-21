// RynthOracle's Leaderboards tab, Aelrynth's half (Views/BoardsTab.cs), moved here on 2026-10-20 and
// redrawn with ImGui as RynthAelrynth's Leaderboards page: a sub-tab of the Mini Remote's Server
// section while on Aelrynth, and a tab of the /rserver window.
using System;
using System.Numerics;
using ImGuiNET;
using RynthCore.Plugin.ServerPack;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthAelrynth;

/// <summary>
/// Aelrynth's /top boards (every board's leader, one board's top 10, your own standings) and the
/// /trial dungeon records. Render thread: draws holding the feature's lock, and queues what it sends.
/// </summary>
internal sealed class AelrynthPages
{
    private static readonly Vector4 Gold = new(1.00f, 0.84f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 Selected = new(0.26f, 0.45f, 0.70f, 1f);

    private readonly AelrynthFeature _f;
    private readonly Func<bool> _active;
    private readonly Action<Action> _runOnTick;
    private readonly Func<string, bool> _send;
    private readonly Action<string> _print;
    private readonly Action _saveSettings;
    private readonly ServerRefreshRow _refresh = new();

    private int _view;                    // 0 boards, 1 me, 2 trials
    private string _boardArg = "";
    private string _trialArg = "";

    public AelrynthPages(AelrynthFeature feature, Func<bool> active, Action<Action> runOnTick, Func<string, bool> send,
                         Action<string> print, Action saveSettings)
    {
        _f = feature;
        _active = active;
        _runOnTick = runOnTick;
        _send = send;
        _print = print;
        _saveSettings = saveSettings;
    }

    public ServerPage[] Pages()
    {
        var list = AelrynthFeature.PageList;
        return new[] { new ServerPage(list[0].Id, list[0].Label, list[0].Tooltip, DrawBoards) };
    }

    /// <summary>Logout: the next character's status lines start empty.</summary>
    public void Reset() => _refresh.Reset();

    private void DrawBoards()
    {
        if (!_active())
        {
            SafeImGui.TextWrapped("This page is for Aelrynth. RynthCore doesn't see this world as Aelrynth "
                                  + "(check RynthCore Options > Diagnostics, or /rserver info).");
            return;
        }
        lock (_f.Gate)
        {
            AelrynthBoards a = _f.Boards;
            if (Toggle("Boards##ab_v0", _view == 0)) _view = 0;
            ImGui.SameLine();
            if (Toggle("Me##ab_v1", _view == 1)) _view = 1;
            ImGui.SameLine();
            if (Toggle("Trials##ab_v2", _view == 2)) _view = 2;
            _refresh.Draw("ab" + _view, BoardsHelp(), RefreshBoards);
            ImGui.Separator();
            if (a.LastError.Length > 0) SafeImGui.TextColored(Red, a.LastError);

            if (_view == 1) DrawMe(a);
            else if (_view == 2) DrawTrials(a);
            else DrawOverview(a);

            ImGui.Spacing();
            bool hide = _f.HideOwnReplies;
            if (ImGui.Checkbox("Hide the replies to RynthAelrynth's own requests##ab_hide", ref hide))
            {
                _f.HideOwnReplies = hide;
                _runOnTick(_saveSettings);
            }
            if (ImGui.IsItemHovered()) SafeImGui.SetTooltip("/top and /trial when RynthAelrynth sends them. A command you type yourself always shows its reply.");
        }
    }

    private void DrawMe(AelrynthBoards a)
    {
        if (ImGui.BeginChild("##ab_me", new Vector2(0f, 300f), ImGuiChildFlags.Borders))
        {
            foreach (AelrynthStanding s in a.Standings)
            {
                SafeImGui.Text(Cut(s.Title, 34));
                ImGui.SameLine(260f);
                SafeImGui.Text(s.Rank > 0 ? $"#{s.Rank} of {s.Of}" : "-");
                if (s.Value.Length > 0) { ImGui.SameLine(380f); SafeImGui.Text(s.Value); }
            }
            if (a.Standings.Count == 0) ImGui.TextDisabled("Nothing read yet. Press Refresh.");
        }
        ImGui.EndChild();
    }

    private void DrawTrials(AelrynthBoards a)
    {
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Dungeon##ab_trialarg", ref _trialArg, 48);
        ImGui.SameLine();
        if (ImGui.SmallButton("Top 10##ab_trialtop") && _trialArg.Trim().Length > 0) Ask(a, isTrial: true, _trialArg);
        if (ImGui.BeginChild("##ab_trials", new Vector2(0f, 300f), ImGuiChildFlags.Borders))
        {
            int i = 0;
            foreach (TrialDungeon d in a.Trials)
            {
                TrialDungeon row = d;
                if (ImGui.Selectable($"{d.Name} ({d.Landblock})##ab_t{i++}"))
                {
                    _trialArg = row.Name;
                    Ask(a, isTrial: true, row.Name);
                }
                if (ImGui.IsItemHovered()) SafeImGui.SetTooltip("Click for this dungeon's top 10.");
                ImGui.TextDisabled("  record:"); ImGui.SameLine(90f); SafeImGui.TextWrapped(d.Record);
                ImGui.TextDisabled("  you:"); ImGui.SameLine(90f); SafeImGui.Text(d.You);
                ImGui.TextDisabled("  this week:"); ImGui.SameLine(90f); SafeImGui.Text(d.Week);
            }
            foreach (string line in a.TrialLines) if (!line.StartsWith("Trials - ", StringComparison.Ordinal)) SafeImGui.TextWrapped(line);
            if (a.TrialBoardTitle.Length > 0)
            {
                ImGui.SeparatorText(a.TrialBoardTitle);
                foreach (BoardRow r in a.TrialRows)
                {
                    SafeImGui.Text($"{r.Rank,2}. {r.Value}");
                    ImGui.SameLine(90f);
                    SafeImGui.Text(Cut(r.Name, 28));
                    ImGui.SameLine(300f);
                    SafeImGui.TextDisabled(Cut(r.Extra, 40));
                }
                foreach (string n in a.TrialNotes) SafeImGui.TextWrapped(n);
            }
            if (a.Trials.Count == 0 && a.TrialBoardTitle.Length == 0) ImGui.TextDisabled("Nothing read yet. Press Refresh.");
        }
        ImGui.EndChild();
    }

    private void DrawOverview(AelrynthBoards a)
    {
        if (ImGui.SmallButton("All boards##ab_all")) Ask(a, isTrial: false, "");
        if (ImGui.IsItemHovered()) SafeImGui.SetTooltip("Sends /top: every board's leader.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160f);
        ImGui.InputText("Board##ab_arg", ref _boardArg, 32);
        ImGui.SameLine();
        if (ImGui.SmallButton("Top 10##ab_board") && _boardArg.Trim().Length > 0) Ask(a, isTrial: false, _boardArg);
        if (a.OverviewNote.Length > 0) SafeImGui.TextDisabled(a.OverviewNote);

        if (a.BoardTitle.Length > 0)
        {
            ImGui.SeparatorText($"{a.BoardTitle} ({a.BoardNote})");
            if (a.BoardYou.Length > 0) SafeImGui.TextColored(Gold, a.BoardYou);
            if (ImGui.BeginChild("##ab_rows", new Vector2(0f, 300f), ImGuiChildFlags.Borders))
            {
                ImGui.TextDisabled("Rank"); ImGui.SameLine(60f); ImGui.TextDisabled("Name"); ImGui.SameLine(280f); ImGui.TextDisabled("Value");
                foreach (BoardRow r in a.BoardRows)
                {
                    SafeImGui.Text(r.Rank.ToString());
                    ImGui.SameLine(60f);
                    if (r.IsYou) SafeImGui.TextColored(Gold, Cut(r.Name, 30));
                    else SafeImGui.Text(Cut(r.Name, 30));
                    ImGui.SameLine(280f);
                    SafeImGui.Text(r.Value);
                    if (r.Extra.Length > 0) { ImGui.SameLine(420f); SafeImGui.TextDisabled(Cut(r.Extra, 40)); }
                }
                if (a.BoardRows.Count == 0) ImGui.TextDisabled("Nothing read yet. Press Refresh.");
            }
            ImGui.EndChild();
            return;
        }
        if (ImGui.BeginChild("##ab_overview", new Vector2(0f, 300f), ImGuiChildFlags.Borders))
        {
            int i = 0;
            foreach (AelrynthBoardSummary s in a.Overview)
            {
                AelrynthBoardSummary row = s;
                if (ImGui.Selectable(Cut(s.Title, 30) + "##ab_o" + i++, false, ImGuiSelectableFlags.None, new Vector2(230f, 0f)))
                {
                    _boardArg = row.Key;
                    Ask(a, isTrial: false, row.Key);
                }
                if (ImGui.IsItemHovered()) SafeImGui.SetTooltip($"/top {s.Key}");
                ImGui.SameLine(240f);
                SafeImGui.Text(Cut(s.Leader, 24));
                if (s.Value.Length > 0) { ImGui.SameLine(430f); SafeImGui.Text(s.Value); }
            }
            if (a.Overview.Count == 0) ImGui.TextDisabled("Nothing read yet. Press All boards.");
        }
        ImGui.EndChild();
    }

    // ── Refresh (RynthOracle's RefreshBar, Aelrynth's part) ─────────────

    private ServerRefreshResult RefreshBoards()
    {
        AelrynthBoards a = _f.Boards;
        string key = _view switch { 1 => "top me", 2 => "trial", _ => "top " + _boardArg };
        if (_refresh.CoolingDown(key, Environment.TickCount64)) return new ServerRefreshResult(ServerRefreshRow.TooSoon);
        int before = a.Revision;
        string cmd = _view switch
        {
            1 => Ask(a, isTrial: false, "me"),
            2 => Ask(a, isTrial: true, ""),
            _ => Ask(a, isTrial: false, a.BoardTitle.Length > 0 ? _boardArg : ""),
        };
        return new ServerRefreshResult($"Sent {cmd}.", () => a.Revision != before);
    }

    private string BoardsHelp() => _view switch
    {
        1 => "Sends /top me and reads the reply.",
        2 => "Sends /trial and reads the dungeon list.",
        _ => "Sends /top (every board's leader), or asks again for the board that is open.",
    };

    /// <summary>
    /// "/top [arg]" or "/trial [arg]" on the next tick, marked as RynthAelrynth's own (its reply can
    /// be hidden); returns the command.
    /// </summary>
    private string Ask(AelrynthBoards a, bool isTrial, string arg)
    {
        string cmd = string.IsNullOrWhiteSpace(arg) ? (isTrial ? "/trial" : "/top") : (isTrial ? "/trial " : "/top ") + arg.Trim();
        _runOnTick(() =>
        {
            lock (_f.Gate)
            {
                if (isTrial) a.RequestTrial(arg, Environment.TickCount64);
                else a.RequestTop(arg, Environment.TickCount64);
            }
            if (!_send(cmd)) _print($"Couldn't send {cmd} (not in the world).");
        });
        return cmd;
    }

    private static bool Toggle(string label, bool selected)
    {
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Selected);
        bool clicked = ImGui.SmallButton(label);
        if (selected) ImGui.PopStyleColor();
        return clicked;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 3)) + "...";
}
