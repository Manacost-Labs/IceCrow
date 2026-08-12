using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Protocol;

public enum PowerParseStatus
{
    Parsed,
    Ignored,
    Malformed,
    Unknown,
}

public sealed record PowerParseResult(
    PowerParseStatus Status,
    GameEvent? Event,
    string? Diagnostic)
{
    public bool HasEvent => Event is not null;

    internal static PowerParseResult Parsed(GameEvent gameEvent) =>
        new(PowerParseStatus.Parsed, gameEvent, null);

    internal static PowerParseResult Ignored() =>
        new(PowerParseStatus.Ignored, null, null);

    internal static PowerParseResult Malformed(string? diagnostic) =>
        new(PowerParseStatus.Malformed, null, diagnostic);

    internal static PowerParseResult Unknown(GameEvent gameEvent) =>
        new(PowerParseStatus.Unknown, gameEvent, null);
}
