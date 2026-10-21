// Ported from Oracle of Dereth by Advis Eveldan (advis61), MIT. See THIRD_PARTY_NOTICES.md.
// RynthOracle's Conquest tab (Views/ConquestTab.cs) and the Conquest half of its Leaderboards tab
// (Views/BoardsTab.cs), moved here on 2026-10-20 and redrawn with ImGui as RynthConquest's pages:
// sub-tabs of the Mini Remote's Server section while on Conquest, and tabs of the /rserver window.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using RynthCore.Plugin.RynthOracle.Data;
using RynthCore.Plugin.ServerPack;
using RynthCore.Plugin.Shared;

namespace RynthCore.Plugin.RynthConquest;

/// <summary>
/// The Conquest page (bank balances and bank commands, advanced and enlightenment augmentations,
/// levels past 275 and the XP bonus breakdown, fellowships looking for members, the custom quest
/// worklist: upstream's Bank, Conquest Augs, Conquest XP, Fship and Custom tabs) and the
/// Leaderboards page (Conquest's /top boards: upstream's Top Players tab).
/// Render thread: each page draws holding the feature's lock, and queues what it sends for the tick.
/// </summary>
internal sealed class ConquestPages
{
    private static readonly Vector4 Gold = new(1.00f, 0.84f, 0.30f, 1f);
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.55f, 0.57f, 0.60f, 1f);
    private static readonly Vector4 Selected = new(0.26f, 0.45f, 0.70f, 1f);

    private static readonly string[] Views = { "Bank", "Augs", "Enlightenment", "Experience", "Fellowships", "Custom quests" };
    private static readonly string[] FshipSorts = { "Name", "Leader", "Most members", "Location" };

    private readonly ConquestFeature _f;
    private readonly Func<bool> _active;
    private readonly Action<Action> _runOnTick;
    private readonly Func<string, bool> _send;
    private readonly Action<string> _print;
    private readonly Action _saveSettings;
    private readonly ServerRefreshRow _refresh = new();

    private int _view;
    private int _withdrawIndex = ConquestData.DefaultWithdraw, _transferIndex = ConquestData.DefaultTransfer;
    private string _withdrawAmount = "", _transferAmount = "", _transferTarget = "";
    private int _fshipSort;
    private int _board;

    /// <param name="active">RynthConquest's server is this world.</param>
    /// <param name="runOnTick">Queues work for the plugin tick (ServerPackPlugin.RunOnTick).</param>
    /// <param name="send">Sends a chat command as if typed (pump thread).</param>
    /// <param name="print">A line in the chat window (pump thread).</param>
    /// <param name="saveSettings">Writes the feature's settings (pump thread).</param>
    public ConquestPages(ConquestFeature feature, Func<bool> active, Action<Action> runOnTick, Func<string, bool> send,
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
        var list = ConquestFeature.PageList;
        return new[]
        {
            new ServerPage(list[0].Id, list[0].Label, list[0].Tooltip, DrawConquest),
            new ServerPage(list[1].Id, list[1].Label, list[1].Tooltip, DrawBoards),
        };
    }

    /// <summary>Logout: the next character's status lines start empty.</summary>
    public void Reset() => _refresh.Reset();

    // ── Conquest page ───────────────────────────────────────────────────

    private void DrawConquest()
    {
        if (!Ready()) return;
        lock (_f.Gate)
        {
            for (int i = 0; i < Views.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                if (Toggle(Views[i] + "##cq_v" + i, i == _view)) _view = i;
            }
            _refresh.Draw("cq" + _view, ConquestHelp(), RefreshConquest);
            ImGui.Separator();
            switch (_view)
            {
                case 0: DrawBank(); break;
                case 1: DrawAugs(); break;
                case 2: DrawEnl(); break;
                case 3: DrawXp(); break;
                case 4: DrawFships(); break;
                default: DrawCustom(); break;
            }
            ImGui.Spacing();
            bool hide = _f.HideOwnReplies;
            if (ImGui.Checkbox("Hide the replies to RynthConquest's own requests##cq_hide", ref hide))
            {
                _f.HideOwnReplies = hide;
                _runOnTick(_saveSettings);
            }
            Tooltip("/b, /augs, /top, /myquests and the like when RynthConquest sends them. A command you type yourself always shows its reply.");
        }
    }

    /// <summary>True when the page has something to show; otherwise says why.</summary>
    private bool Ready()
    {
        if (_active()) return true;
        SafeImGui.TextWrapped("These pages are for Conquest. RynthCore doesn't see this world as Conquest "
                              + "(check RynthCore Options > Diagnostics, or /rserver info).");
        return false;
    }

    private static string ReadAt(DateTime utc) => utc == DateTime.MinValue ? "not read yet" : $"read at {utc.ToLocalTime():HH:mm}";

    private void DrawBank()
    {
        ConquestData cq = _f.Data;
        SafeImGui.TextDisabled(ReadAt(cq.BankUtc));
        foreach (NamedValue b in cq.Bank.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            SafeImGui.TextDisabled(Cut(b.Name, 34));
            ImGui.SameLine(260f);
            SafeImGui.Text(b.Value);
        }
        if (cq.Bank.Count == 0) SafeImGui.TextDisabled("No balances read yet.");

        ImGui.SeparatorText("Deposit");
        if (ImGui.Button("Deposit everything##cq_dep")) Queue("/bank deposit");
        Tooltip("Sends /bank deposit.");
        bool auto = _f.AutoDeposit;
        if (ImGui.Checkbox("Deposit by itself every ten minutes##cq_auto", ref auto))
        {
            _f.AutoDeposit = auto;
            _runOnTick(_saveSettings);
        }
        Tooltip("At :00, :10, :20 ... while you're logged in on Conquest. Its reply is hidden.");

        ImGui.SeparatorText("Withdraw");
        string[] wl = ConquestData.Withdrawable.Select(c => c.Label).ToArray();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.Combo("Currency##cq_wcur", ref _withdrawIndex, wl, wl.Length)) _withdrawIndex = Math.Clamp(_withdrawIndex, 0, wl.Length - 1);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("Amount##cq_wamt", ref _withdrawAmount, 16);
        if (ImGui.Button("Withdraw##cq_withdraw"))
        {
            string? cmd = ConquestData.WithdrawCommand(ConquestData.Withdrawable[_withdrawIndex], _withdrawAmount);
            if (cmd == null) Print("Withdraw: type a whole number above 0.");
            else Queue(cmd);
        }

        ImGui.SeparatorText("Transfer to a character");
        string[] tl = ConquestData.Transferable.Select(c => c.Label).ToArray();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.Combo("Currency##cq_tcur", ref _transferIndex, tl, tl.Length)) _transferIndex = Math.Clamp(_transferIndex, 0, tl.Length - 1);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("Amount##cq_tamt", ref _transferAmount, 16);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText("Character##cq_tgt", ref _transferTarget, 40);
        if (ImGui.Button("Transfer##cq_transfer"))
        {
            string? cmd = ConquestData.TransferCommand(ConquestData.Transferable[_transferIndex], _transferAmount, _transferTarget);
            if (cmd == null) Print("Transfer: type a whole number above 0 (MMD notes: at most 1,000) and a character name.");
            else Queue(cmd);
        }
    }

    private void DrawAugs()
    {
        ConquestData cq = _f.Data;
        SafeImGui.TextDisabled($"{ReadAt(cq.AugsUtc)}, {cq.AugTotal} in total");
        ImGui.TextDisabled("Aug"); ImGui.SameLine(120f); ImGui.TextDisabled("Have"); ImGui.SameLine(170f);
        ImGui.TextDisabled("Next costs"); ImGui.SameLine(330f); ImGui.TextDisabled("Effect now");
        foreach (ConquestAug a in cq.Augs)
        {
            SafeImGui.Text(a.Name);
            ImGui.SameLine(120f);
            SafeImGui.Text(a.Count.ToString());
            ImGui.SameLine(170f);
            SafeImGui.Text(a.NextCostText());
            ImGui.SameLine(330f);
            SafeImGui.TextDisabled(Cut(a.Effect(), 48));
            Tooltip(a.Effect());
        }
    }

    private void DrawEnl()
    {
        ConquestData cq = _f.Data;
        SafeImGui.TextDisabled(ReadAt(cq.EnlUtc));
        foreach (NamedValue a in cq.EnlAugs)
        {
            SafeImGui.Text(Cut(a.Name, 26));
            ImGui.SameLine(200f);
            SafeImGui.Text(a.Value);
            ImGui.SameLine(360f);
            SafeImGui.TextDisabled(ConquestData.EnlCost(a.Name));
        }
        if (cq.EnlAugs.Count == 0) ImGui.TextDisabled("Nothing read yet. Press Refresh.");
    }

    private void DrawXp()
    {
        ConquestData cq = _f.Data;
        int level = _f.Level;
        if (!_f.CharacterKnown) ImGui.TextDisabled("Reading your character...");
        ImGui.SeparatorText($"Level {level}");
        if (level >= ConquestData.MaxLevel) ImGui.TextUnformatted("Maximum level.");
        else if (level < 275)
        {
            // Retail levels: RynthOracle's Character tab has the progress to the next one.
            SafeImGui.TextDisabled($"Conquest's own levels start at 276. {Number(Math.Max(0, ConquestData.XpForLevel(276) - _f.TotalXp))} XP to level 276.");
        }
        else
        {
            long start = level >= 276 ? ConquestData.XpForLevel(level) : 0;
            long next = ConquestData.XpForLevel(level + 1);
            long toNext = Math.Max(0, next - _f.TotalXp);
            long begin = level == 275 ? 191_226_310_247 : start;
            float frac = next > begin ? (float)Math.Clamp((double)(_f.TotalXp - begin) / (next - begin), 0, 1) : 1f;
            ImGui.ProgressBar(frac, new Vector2(-1f, 0f), $"{Number(toNext)} to level {level + 1}");
            long toMax = Math.Max(0, ConquestData.XpForLevel(ConquestData.MaxLevel) - _f.TotalXp);
            SafeImGui.TextDisabled($"{Number(toMax)} to level {ConquestData.MaxLevel}");
        }
        int target = _f.Enlightenment + 1;
        ImGui.SeparatorText($"Enlightenment {_f.Enlightenment}");
        SafeImGui.Text($"Next costs {Number(ConquestData.EnlCoinCost(target))} coins and {(ConquestData.EnlLuminanceCost(target) / 1_000_000.0):0.##}M luminance");

        ImGui.SeparatorText("Experience bonuses");
        SafeImGui.TextDisabled(ReadAt(cq.BonusUtc));
        foreach (NamedValue b in cq.Bonuses)
        {
            SafeImGui.TextDisabled(b.Name);
            ImGui.SameLine(200f);
            SafeImGui.Text(b.Value.Length > 0 ? b.Value : "-");
        }
    }

    private void DrawFships()
    {
        ConquestData cq = _f.Data;
        SafeImGui.TextDisabled($"{ReadAt(cq.FshipUtc)}, {cq.Fellowships.Count} looking for members");
        ImGui.SetNextItemWidth(160f);
        ImGui.Combo("Sort##cq_fsort", ref _fshipSort, FshipSorts, FshipSorts.Length);
        IEnumerable<ConquestFellowship> list = _fshipSort switch
        {
            1 => cq.Fellowships.OrderBy(f => f.Leader, StringComparer.OrdinalIgnoreCase),
            2 => cq.Fellowships.OrderByDescending(f => f.MemberCount),
            3 => cq.Fellowships.OrderBy(f => f.Location, StringComparer.OrdinalIgnoreCase),
            _ => cq.Fellowships.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
        };
        if (ImGui.BeginChild("##cq_fships", new Vector2(0f, 220f), ImGuiChildFlags.Borders))
        {
            foreach (ConquestFellowship f in list)
            {
                SafeImGui.Text(Cut(f.Name, 26));
                ImGui.SameLine(200f);
                SafeImGui.Text(Cut(f.Leader, 20));
                ImGui.SameLine(350f);
                SafeImGui.Text(f.Members);
                if (f.Location.Length > 0) { ImGui.SameLine(410f); SafeImGui.TextDisabled(Cut(f.Location, 26)); }
            }
            if (cq.Fellowships.Count == 0) ImGui.TextDisabled("None read yet. Press Refresh.");
        }
        ImGui.EndChild();
    }

    private void DrawCustom()
    {
        DateTime now = DateTime.UtcNow;
        int i = 0;
        foreach (CustomQuest q in CustomQuestList.All)
        {
            bool done = q.IsComplete(_f.Flags, now);
            SafeImGui.TextColored(done ? Green : Grey, done ? "done" : "--");
            ImGui.SameLine(50f);
            string status = q.Status(_f.Flags, now);
            CustomQuest row = q;
            if (ImGui.Selectable(Cut(q.Name, 34) + "##cq_cust" + i++, false, ImGuiSelectableFlags.None, new Vector2(280f, 0f)))
                PrintEntry(row.Name, row.Flag, status, row.Hint, row.Url);
            Tooltip("Click: the quest's hint in chat.");
            ImGui.SameLine(340f);
            SafeImGui.Text(status);
        }
        if (CustomQuestList.All.Count == 0) ImGui.TextDisabled("No custom quests listed.");
        if (_f.Flags.LastReadUtc == DateTime.MinValue) ImGui.TextDisabled("Press Refresh to read your quest flags (/myquests) for their status.");
    }

    /// <summary>An entry's name, status, hint and link in chat (upstream "thinks" them).</summary>
    private void PrintEntry(string name, string flag, string status, string details, string url)
    {
        Print(flag.Length > 0 ? $"{name} ({flag}){(status.Length > 0 ? " - " + status : "")}" : name);
        if (details.Length > 0) Print(details);
        if (url.Length > 0) Print(url);
    }

    // ── Leaderboards page ───────────────────────────────────────────────

    private void DrawBoards()
    {
        if (!Ready()) return;
        lock (_f.Gate)
        {
            ConquestData cq = _f.Data;
            _board = Math.Clamp(_board, 0, cq.Boards.Count - 1);
            for (int i = 0; i < cq.Boards.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                if (Toggle(cq.Boards[i].Label + "##cb_tab" + i, i == _board)) _board = i;
            }
            ConquestBoard b = cq.Boards[_board];
            _refresh.Draw("board" + b.Key, $"Sends {b.Command} and reads the reply.", () => Ask("board" + b.Key, () => b.UpdatedUtc.Ticks, b.Command,
                now => cq.RequestBoard(b, now)));
            ImGui.Separator();
            SafeImGui.TextDisabled(b.UpdatedUtc == DateTime.MinValue ? "not read yet" : $"read at {b.UpdatedUtc.ToLocalTime():HH:mm}");
            if (b.You != null) SafeImGui.Text($"You: #{b.You.Rank} with {b.You.Value}");
            if (ImGui.BeginChild("##cb_rows", Vector2.Zero, ImGuiChildFlags.Borders))
            {
                ImGui.TextDisabled("Rank"); ImGui.SameLine(60f); ImGui.TextDisabled("Name"); ImGui.SameLine(280f); ImGui.TextDisabled("Value");
                foreach (BoardRow r in b.Rows)
                {
                    SafeImGui.Text(r.Rank.ToString());
                    ImGui.SameLine(60f);
                    if (r.IsYou) SafeImGui.TextColored(Gold, Cut(r.Name, 30));
                    else SafeImGui.Text(Cut(r.Name, 30));
                    ImGui.SameLine(280f);
                    SafeImGui.Text(r.Value);
                    if (r.Extra.Length > 0) { ImGui.SameLine(420f); SafeImGui.TextDisabled(Cut(r.Extra, 40)); }
                }
                if (b.Rows.Count == 0) ImGui.TextDisabled("Nothing read yet. Press Refresh.");
            }
            ImGui.EndChild();
        }
    }

    // ── Refresh (RynthOracle's RefreshBar, Conquest's part) ──────────────

    private ServerRefreshResult RefreshConquest()
    {
        ConquestData cq = _f.Data;
        switch (_view)
        {
            case 0: return Ask("bank", () => cq.BankUtc.Ticks, "/b", now => cq.RequestBank(now));
            case 1: return Ask("augs", () => cq.AugsUtc.Ticks, "/augs", now => cq.RequestAugs(now));
            case 2: return Ask("enl", () => cq.EnlUtc.Ticks, "/enl augs", now => cq.RequestEnl(now));
            case 3: return Ask("bonus", () => cq.BonusUtc.Ticks, "/bonus", now => cq.RequestBonus(now));
            case 4: return Ask("fship", () => cq.FshipUtc.Ticks, "/fship list", now => cq.RequestFships(now));
            default:
                if (_refresh.CoolingDown("quests", Environment.TickCount64)) return new ServerRefreshResult(ServerRefreshRow.TooSoon);
                QuestFlagStore flags = _f.Flags;
                int rev = flags.Revision;
                int wait = flags.ThrottleSecondsLeft(Environment.TickCount64);
                if (wait > 0) return new ServerRefreshResult($"The server answers one /myquests a minute; try again in {wait}s (the flags shown are kept).");
                _runOnTick(() =>
                {
                    lock (_f.Gate) flags.Request(_send, Environment.TickCount64);
                });
                return new ServerRefreshResult("Asked the server for your quest flags (/myquests).", () => flags.Revision != rev);
        }
    }

    private string ConquestHelp() => _view switch
    {
        0 => "Sends /b and reads your balances.",
        1 => "Sends /augs and reads your advanced augmentations.",
        2 => "Sends /enl augs and reads your enlightenment augmentations.",
        3 => "Sends /bonus for the experience bonuses (your level is read from the game).",
        4 => "Sends /fship list and reads the fellowships looking for members.",
        _ => "Sends /myquests: the custom quests' status comes from your quest flags.",
    };

    /// <summary>
    /// Sends one of the page's own commands on the next tick (marking it as RynthConquest's, so its
    /// reply can be hidden) and waits for <paramref name="state"/> to change.
    /// </summary>
    private ServerRefreshResult Ask(string key, Func<long> state, string command, Action<long> markSent)
    {
        if (_refresh.CoolingDown(key, Environment.TickCount64)) return new ServerRefreshResult(ServerRefreshRow.TooSoon);
        long before = state();
        _runOnTick(() =>
        {
            lock (_f.Gate) markSent(Environment.TickCount64);
            if (!_send(command)) _print($"Couldn't send {command} (not in the world).");
        });
        return new ServerRefreshResult($"Sent {command}.", () => state() != before);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>A command from a button, sent on the next tick.</summary>
    private void Queue(string command) => _runOnTick(() => _send(command));

    /// <summary>A line in chat, written on the next tick.</summary>
    private void Print(string text) => _runOnTick(() => _print(text));

    private static bool Toggle(string label, bool selected)
    {
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Selected);
        bool clicked = ImGui.SmallButton(label);
        if (selected) ImGui.PopStyleColor();
        return clicked;
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered()) SafeImGui.SetTooltip(text);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 3)) + "...";

    private static string Number(long n) => TimeText.Number(n);
}
