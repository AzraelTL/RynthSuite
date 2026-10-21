// PetCaptureLines.cs - The chat lines that mean an ACECustom siphon-lens capture worked.
//
// RynthILT's (linked source): its siphon auto-capture (IltPetAutomation, RynthAi's Pets tab's until
// 2026-10-20) reads them to time the next lens (success -> short cooldown), and its Vaulted Dungeons
// keeper counts a capture as a kill for its "Kill Client" timer (a hunter who captures instead of
// killing is not idle). They are the MonsterCapture success replies; nothing new is guessed here.
using System;

namespace RynthCore.Plugin.Shared;

internal static class PetCaptureLines
{
    /// <summary>Substrings of the success replies (matched case-insensitively).</summary>
    private static readonly string[] Success =
    {
        "successfully siphoned the essence",
        "The echo resonates perfectly",
        "The essence is captured",
    };

    /// <summary>True when <paramref name="text"/> is a capture-success reply.</summary>
    public static bool IsSuccess(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string part in Success)
            if (text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
