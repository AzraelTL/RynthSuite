using System;
using System.Collections.Generic;
using System.Linq;
using RynthCore.Plugin.RynthILT.IltSettingsView;
using RynthCore.Plugin.Shared;
using RynthCore.RynthAiTests.Harness;

namespace RynthILT.Tests;

// RynthILT's copy of the ILT settings (IltSettingsView/IltSettingsModel.cs): RynthCore's server
// list (plugin API v86, GetServerStateJson: verdict, override, reason), the Hub's feature read, the
// engine verdict the Hub follows (IltEngineVerdict), and the RynthCore commands the view sends.
internal static class IltSettingsTests
{
    public static void Register(Runner r)
    {
        r.Add("ilt settings: RynthCore's server list gives the ILT verdict, override and reason", ServerList);
        r.Add("ilt settings: a list without ILT, or no list at all, reads as such", NoIlt);
        r.Add("ilt settings: the Hub's feature read is published whole", FeatureRead);
        r.Add("ilt settings: the Hub follows RynthCore's ILT verdict once one arrives", EngineVerdict);
        r.Add("ilt settings: override and world-name commands are checked before sending", Commands);
        r.Add("ilt settings: logout clears the copy", ClearOnLogout);
    }

    // GetServerStateJson as RynthCore writes it (UI/Sections/ServerStateJson.cs, schema 1).
    private const string State =
        "{\"schema\":1,\"revision\":3,\"host\":\"infiniteleaftide.online\",\"world\":\"InfiniteLeaftide\",\"pick\":\"auto\"," +
        "\"active\":\"ilt\",\"activeName\":\"Infinite Leaftide\",\"activePlugin\":\"RynthILT\",\"servers\":[" +
        "{\"id\":\"ilt\",\"name\":\"Infinite Leaftide\",\"active\":true,\"detected\":false,\"force\":\"on\",\"builtIn\":true," +
        "\"plugin\":\"RynthILT\",\"reason\":\"forced on for this character\"}," +
        "{\"id\":\"aelrynth\",\"name\":\"Aelrynth\",\"active\":false,\"detected\":false,\"force\":\"auto\",\"builtIn\":true," +
        "\"plugin\":\"\",\"reason\":\"not detected\"}]}";

    private const string NoIltState =
        "{\"schema\":1,\"revision\":1,\"active\":\"\",\"servers\":[" +
        "{\"id\":\"aelrynth\",\"name\":\"Aelrynth\",\"active\":true,\"detected\":true,\"force\":\"auto\"}]}";

    private static void ServerList()
    {
        IltEngineView? v = IltSettingsModel.FromServerState(EngineServerState.Parse(State));
        Check.True(v?.Server != null, "the ILT server is there");
        Check.Eq(v!.Server!.Name, "Infinite Leaftide", "name");
        Check.True(v.Server.Active, "active");
        Check.False(v.Server.Detected, "not detected (forced)");
        Check.Eq(v.Server.Force, "on", "override");
        Check.Eq(v.Server.Reason, "forced on for this character", "reason");

        IltEngineView? odd = IltSettingsModel.FromServerState(EngineServerState.Parse(
            "{\"schema\":1,\"servers\":[{\"id\":\"ILT\",\"force\":\"WEIRD\"}]}"));
        Check.True(odd?.Server != null, "the id ignores case");
        Check.Eq(odd!.Server!.Force, "auto", "an unknown override shows as auto");
        Check.Eq(odd.Server.Name, "Infinite Leaftide", "a missing name gets the server's");
        Check.False(odd.Server.Active, "a missing flag is off");
    }

    private static void NoIlt()
    {
        IltEngineView? none = IltSettingsModel.FromServerState(EngineServerState.Parse(NoIltState));
        Check.True(none != null && none.Server == null, "a list without ILT says so (not 'waiting')");
        Check.True(IltSettingsModel.FromServerState(null) == null, "no list: nothing");
        Check.True(EngineServerState.Parse("") == null && EngineServerState.Parse("{\"schema\":2}") == null, "empty or another schema: no list");

        var model = new IltSettingsModel();
        model.ApplyServerState(EngineServerState.Parse(State));
        model.ApplyServerState(null);
        Check.True(model.Engine?.Server?.Active == true, "no answer keeps the last list");
    }

    private static void FeatureRead()
    {
        var model = new IltSettingsModel();
        var read = new IltFeatureView("Infinite Leaftide", "probe", "12:00:00", Checking: true, IltLike: true,
                                      new[] { new KeyValuePair<string, int>("bank", 1) });
        model.PublishFeatures(read);
        Check.True(ReferenceEquals(model.Features, read), "published as is");
        model.PublishFeatures(null);
        Check.True(model.Features == null, "cleared while there is no Hub");
        Check.Eq(IltSettingsModel.StateText(1), "on", "1 is on");
        Check.Eq(IltSettingsModel.StateText(0), "off", "0 is off (not unknown)");
        Check.Eq(IltSettingsModel.StateText(-1), "unknown", "-1 is unknown");
    }

    private static void EngineVerdict()
    {
        var model = new IltSettingsModel();
        try
        {
            IltEngineVerdict.Attach(model);
            Check.False(IltEngineVerdict.Pushed, "nothing sent yet");
            Check.True(IltEngineVerdict.Active == null, "no verdict: RynthILT's own list decides");
            model.ApplyServerState(EngineServerState.Parse(State));
            Check.True(IltEngineVerdict.Pushed, "sent");
            Check.True(IltEngineVerdict.Active == true, "RynthCore says ILT");
            model.ApplyServerState(EngineServerState.Parse(NoIltState));
            Check.True(IltEngineVerdict.Pushed && IltEngineVerdict.Active == null, "a list without ILT gives no ILT verdict");
            model.Clear();
            Check.False(IltEngineVerdict.Pushed, "logout forgets it");
        }
        finally { IltEngineVerdict.Attach(null); }
        Check.True(IltEngineVerdict.Active == null, "detached");
    }

    private static void Commands()
    {
        Check.Eq(IltSettingsModel.ForceCommand("on"), "/rc server ilt on", "on");
        Check.Eq(IltSettingsModel.ForceCommand("auto"), "/rc server ilt auto", "auto");
        Check.Eq(IltSettingsModel.ForceCommand("maybe"), "", "unknown mode sends nothing");
        Check.Eq(IltSettingsModel.NamesListCommand, "/rc server names ilt", "the names are listed by RynthCore");

        Check.Eq(IltSettingsModel.NamesCommand(true, "  *Frost* ", out _), "/rc server names ilt add *Frost*", "add, trimmed");
        Check.Eq(IltSettingsModel.NamesCommand(false, "Infinite Leaftide", out _), "/rc server names ilt remove Infinite Leaftide", "remove");
        Check.Eq(IltSettingsModel.NamesCommand(true, "", out string e1), "", "empty refused");
        Check.True(e1.Length > 0, "with a reason");
        Check.Eq(IltSettingsModel.NamesCommand(true, new string('a', IltSettingsModel.MaxPattern + 1), out _), "", "too long refused");
        Check.Eq(IltSettingsModel.NamesCommand(true, "x\n/rc reload", out _), "", "a line break can't start a second command");
        Check.Eq(IltSettingsModel.NamesCommand(true, "a/b", out _), "", "no '/'");
    }

    private static void ClearOnLogout()
    {
        var model = new IltSettingsModel();
        model.ApplyServerState(EngineServerState.Parse(State));
        model.PublishFeatures(new IltFeatureView("w", "", "", false, true, Array.Empty<KeyValuePair<string, int>>()));
        Check.True(model.Engine != null && model.Features != null, "both copies");
        model.Clear();
        Check.True(model.Engine == null && model.Features == null, "cleared");
    }
}
