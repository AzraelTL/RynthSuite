// ============================================================================
//  RynthSuite - Plugins/Shared/RynthServerProfileApi.cs
//  The "RynthServer.Profile" interface: what a server plugin (RynthColdeve,
//  RynthConquest, ... - Plugins/Shared/ServerPack) knows about its game server,
//  for any plugin that wants the server's settings without drawing the card.
//  RynthCore says which server is active and which plugin owns it (plugin API
//  v86, GetServerStateJson: "active", "activePlugin"); the reader then asks that
//  plugin for this interface (host GetPluginInterface). RynthILT answers it too,
//  for "ilt" (RynthILT/ProfileInterface.cs).
//  Compiled into the server plugins, RynthILT and their readers (linked source),
//  so the layout can't drift.
//
//  Rules (as the other shared interfaces):
//  - Append-only: new functions go at the end, and Version goes up.
//  - Strings are UTF-8, NUL-terminated. The JSON itself is plain ASCII.
//  - Every function returns safely (no exception crosses the boundary).
//  - The table is allocated once per loaded copy of the plugin and never freed;
//    shutdown sets Version to 0, and a reader re-queries when it sees that.
//
//  GetProfileJson payload (schema 1; read leniently, fields may be added):
//  {
//    "schema": 1, "id": "coldeve", "name": "Coldeve", "plugin": "RynthColdeve",
//    "version": "0.1.3",
//    "active": true,                    // RynthCore's verdict (or the world name before one)
//    "world": "Coldeve",                // the world name the client reports ("" before login)
//    "hosts": [ "play.coldeve.ac" ], "worldNames": [ "Coldeve" ],
//    "port": 9000, "emulator": "ACE", "ruleset": "PvE", "status": "Stable",
//    "botting": "unknown|allowed|restricted|forbidden", "bottingNote": "",
//    "accountLimit": "", "summary": "",
//    "facts": [ "..." ], "commands": [ { "command": "/x", "what": "..." } ],
//    "salvage": [ {                     // the server's own tinkering (the Mini Remote's Crafting tab)
//      "bench": "melee",                // armor, melee, missile, caster, jewelry, items, trinkets
//      "material": 52, "kind": "tink|imbue|once", "effect": "...",
//      "slot": true,                    // counts toward the item's 10 tinks
//      "nameMatch": "",                 // the bag's name must contain this
//      // optional (absent = off / the default):
//      "applied": 370,                  // item int that reads 2 once the row is on it
//      "prepared": true,                // the bag is first prepared on the tier's Ammunition
//      "order": 10,                     // apply order, lower first (else imbue, tink, once)
//      "afterCap": true,                // last, only once the item's tink slots are full
//      "stackGroup": "x", "stackCap": 10  // rows of one group share this cap per item
//    } ],
//    "discord": "", "website": "", "wiki": "", "source": ""
//  }
// ============================================================================

using System.Runtime.InteropServices;

namespace RynthCore.Plugin.Shared;

/// <summary>Constants of the "RynthServer.Profile" interface.</summary>
public static class RynthServerProfile
{
    /// <summary>The profile JSON's schema.</summary>
    public const int Schema = 1;

    /// <summary>"botting" values in the profile JSON.</summary>
    public const string BottingUnknown = "unknown";
    public const string BottingAllowed = "allowed";
    public const string BottingRestricted = "restricted";
    public const string BottingForbidden = "forbidden";
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct RynthServerProfileApiV1
{
    public const string InterfaceName = "RynthServer.Profile";
    public const uint InterfaceVersion = 1;

    public uint Version;      // InterfaceVersion of the table the plugin filled; 0 once it has shut down
    public uint StructSize;   // sizeof(RynthServerProfileApiV1) as the plugin built it

    /// <summary>
    /// GetProfileJson(buffer, capacity): writes the profile JSON (UTF-8, NUL-terminated) into
    /// buffer when it fits, and returns its length in bytes without the NUL. A return value of
    /// capacity or more means nothing was written: call again with a bigger buffer. 0 = nothing
    /// to report (the plugin is shutting down). Any thread.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, int, int> GetProfileJson;
}
