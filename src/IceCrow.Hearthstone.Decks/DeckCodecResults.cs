namespace IceCrow.Hearthstone.Decks;

public sealed record DeckValidationError(string Code, string Path, string Message);

public sealed record DeckValidationResult(bool IsValid, IReadOnlyList<DeckValidationError> Errors);

public sealed record DeckDecodeResult(
    bool Success,
    DeckDefinition? Deck,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record DeckExportParseResult(
    bool Success,
    DeckExport? Export,
    string? ErrorCode,
    string? ErrorMessage);
