// ServerChatRequest.cs - "the server plugin just sent this command": a short time window in which the
// command's reply counts as the plugin's own, so the server's code (ServerPackPlugin.ServerChatText)
// can hide replies to its own requests and still show the same command typed by hand. A time window,
// as RynthOracle has always done it (ported from Oracle of Dereth by Advis Eveldan, MIT): these replies
// carry nothing tying them to the request. Each reply line extends it, so a long reply stays ours to its end.
// Pure: RynthServerPack.Tests covers it.
namespace RynthCore.Plugin.ServerPack;

internal sealed class ServerChatRequest
{
    public const long WindowMs = 5000;
    private long _untilMs;

    public void Sent(long nowMs) => _untilMs = nowMs + WindowMs;
    public void Clear() => _untilMs = 0;
    public bool Awaiting(long nowMs) => nowMs < _untilMs;

    /// <summary>A reply line arrived: keep the window open a little longer.</summary>
    public void Extend(long nowMs)
    {
        if (Awaiting(nowMs)) _untilMs = nowMs + 2000 > _untilMs ? nowMs + 2000 : _untilMs;
    }
}
