using System.Text.RegularExpressions;
using IceCrow.Recording;

namespace IceCrow.FixtureTool;

public sealed partial class RecordingAnonymizer
{
    private readonly Dictionary<string, string> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    public RecordedMatch Anonymize(RecordedMatch source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RecordingSerializer.Validate(source);
        _accounts.Clear();
        _names.Clear();

        var events = source.Events.Select(AnonymizeEvent).ToArray();
        var checkpoints = source.Checkpoints
            .Select((checkpoint, index) => checkpoint with
            {
                Name = $"Checkpoint_{index + 1:D4}",
            })
            .ToArray();
        var result = new RecordedMatch(
            source.FormatVersion,
            source.StartedAt,
            events,
            checkpoints);
        RecordingSerializer.Validate(result);
        return result;
    }

    private RecordedEvent AnonymizeEvent(RecordedEvent source)
    {
        var value = source.Value;
        if (source.Tag is "NAME" or "PLAYER_NAME")
        {
            value = MapName(value);
        }
        else if (source.Tag is "GAME_ACCOUNT_ID" or "ACCOUNT_ID")
        {
            value = MapAccount(value);
        }
        else
        {
            value = SanitizeTagValue(value);
        }

        return source with
        {
            EntityName = MapName(source.EntityName),
            GameAccountId = MapAccount(source.GameAccountId),
            Value = value,
            Content = source.Type == RecordedEventType.UnknownPower
                ? "SANITIZED_UNKNOWN_POWER"
                : SanitizeFreeText(source.Content),
            Block = source.Block is null
                ? null
                : source.Block with
                {
                    EntityName = MapName(source.Block.EntityName),
                    Target = SanitizeBlockTarget(source.Block.Target),
                    TriggerKeyword = SanitizeProtocolToken(
                        source.Block.TriggerKeyword,
                        "REDACTED_TRIGGER"),
                },
        };
    }

    private string? MapName(string? value) => Map(value, _names, "Player");

    private string? MapAccount(string? value) => Map(value, _accounts, "Account");

    private static string? SanitizeTagValue(string? value)
    {
        if (value is null || IsSafeProtocolToken(value))
        {
            return value;
        }

        return "REDACTED_VALUE";
    }

    private static string SanitizeBlockTarget(string value)
    {
        if (IsSafeProtocolToken(value))
        {
            return value;
        }

        var entityId = EntityIdInText().Match(value);
        return entityId.Success
            ? $"Entity_{entityId.Groups["id"].Value}"
            : "REDACTED_TARGET";
    }

    private static string? SanitizeProtocolToken(string? value, string replacement) =>
        value is null || IsSafeProtocolToken(value) ? value : replacement;

    private static bool IsSafeProtocolToken(string value) =>
        SafeProtocolToken().IsMatch(value);

    private static string? Map(
        string? value,
        Dictionary<string, string> values,
        string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!values.TryGetValue(value, out var replacement))
        {
            replacement = $"{prefix}_{values.Count + 1}";
            values.Add(value, replacement);
        }

        return replacement;
    }

    public static string? SanitizeFreeText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var sanitized = WindowsUserPath().Replace(value, "<LOCAL_PATH>");
        sanitized = UnixUserPath().Replace(sanitized, "<LOCAL_PATH>");
        sanitized = GameAccount().Replace(sanitized, "<ACCOUNT_ID>");
        sanitized = BattleTag().Replace(sanitized, "<BATTLETAG>");
        return SecretAssignment().Replace(sanitized, "<REDACTED_SECRET>");
    }

    public static void ValidateSanitized(RecordedMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        for (var index = 0; index < match.Checkpoints.Count; index++)
        {
            var expectedName = $"Checkpoint_{index + 1:D4}";
            if (!string.Equals(match.Checkpoints[index].Name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Anonymized recording checkpoint names must be deterministic placeholders.");
            }
        }

        foreach (var recordedEvent in match.Events)
        {
            ValidateText(recordedEvent.EntityName, nameof(recordedEvent.EntityName));
            ValidateText(recordedEvent.GameAccountId, nameof(recordedEvent.GameAccountId));
            ValidateText(recordedEvent.Value, nameof(recordedEvent.Value));
            ValidateText(recordedEvent.Content, nameof(recordedEvent.Content));
            ValidateText(recordedEvent.CardId, nameof(recordedEvent.CardId));
            ValidateCanonicalIdentity(recordedEvent.EntityName, "Player_", nameof(recordedEvent.EntityName));
            ValidateCanonicalIdentity(recordedEvent.GameAccountId, "Account_", nameof(recordedEvent.GameAccountId));
            if (recordedEvent.Type == RecordedEventType.UnknownPower &&
                !string.Equals(recordedEvent.Content, "SANITIZED_UNKNOWN_POWER", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unknown Power content was not fully redacted.");
            }

            if (recordedEvent.Tag is "NAME" or "PLAYER_NAME")
            {
                ValidateCanonicalIdentity(recordedEvent.Value, "Player_", nameof(recordedEvent.Value));
            }
            else if (recordedEvent.Tag is "GAME_ACCOUNT_ID" or "ACCOUNT_ID")
            {
                ValidateCanonicalIdentity(recordedEvent.Value, "Account_", nameof(recordedEvent.Value));
            }
            else if (recordedEvent.Value is not null &&
                     !IsSafeProtocolToken(recordedEvent.Value) &&
                     !string.Equals(recordedEvent.Value, "REDACTED_VALUE", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Anonymized tag value is not a safe protocol token.");
            }

            if (recordedEvent.Block is not null)
            {
                ValidateText(recordedEvent.Block.EntityName, nameof(recordedEvent.Block.EntityName));
                ValidateText(recordedEvent.Block.Target, nameof(recordedEvent.Block.Target));
                ValidateText(recordedEvent.Block.EffectCardId, nameof(recordedEvent.Block.EffectCardId));
                ValidateCanonicalIdentity(
                    recordedEvent.Block.EntityName,
                    "Player_",
                    nameof(recordedEvent.Block.EntityName));
                if (!IsSafeProtocolToken(recordedEvent.Block.Target) &&
                    !EntityPlaceholder().IsMatch(recordedEvent.Block.Target) &&
                    !string.Equals(recordedEvent.Block.Target, "REDACTED_TARGET", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Anonymized block target is not canonical.");
                }
            }
        }
    }

    private static void ValidateCanonicalIdentity(
        string? value,
        string prefix,
        string fieldName)
    {
        if (value is not null &&
            (!value.StartsWith(prefix, StringComparison.Ordinal) ||
             !int.TryParse(value.AsSpan(prefix.Length), out var index) ||
             index <= 0))
        {
            throw new InvalidDataException(
                $"Anonymized identity field '{fieldName}' is not canonical.");
        }
    }

    private static void ValidateText(string? value, string fieldName)
    {
        if (!string.Equals(value, SanitizeFreeText(value), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Anonymized recording still contains sensitive-looking data in '{fieldName}'.");
        }
    }

    [GeneratedRegex(
        @"(?i)\b[A-Z]:\\(?:Users|Documents and Settings)\\[^\s\""']+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WindowsUserPath();

    [GeneratedRegex(
        @"(?i)(?:/home/|/Users/)[^\s\""']+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex UnixUserPath();

    [GeneratedRegex(
        @"\[?hi=-?[0-9]+\s+lo=-?[0-9]+\]?",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex GameAccount();

    [GeneratedRegex(
        @"\b[\p{L}\p{N}_-]{2,32}#[0-9]{4,8}\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BattleTag();

    [GeneratedRegex(
        @"(?i)\b(?:api[_-]?key|access[_-]?token|token|secret|password|authorization)\s*[:=]\s*[^\s,;]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(
        @"^(?:-?[0-9]+|[A-Z][A-Z0-9_]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex SafeProtocolToken();

    [GeneratedRegex(
        @"(?:^|[\s\[])id=(?<id>[0-9]+)(?:$|[\s\]])",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EntityIdInText();

    [GeneratedRegex(
        @"^Entity_[0-9]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex EntityPlaceholder();
}
