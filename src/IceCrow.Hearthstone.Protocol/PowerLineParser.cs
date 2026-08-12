using System.Globalization;
using System.Text.RegularExpressions;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Hearthstone.Protocol;

public sealed class PowerLineParser
{
    public const int MaximumInputCharacters = 256 * 1024;

    private const string PowerTaskListPrefix = "PowerTaskList.DebugPrintPower() -";
    private const string GameStatePrefix = "GameState.DebugPrintPower() -";
    private const string EndCurrentTaskListPrefix = "PowerProcessor.EndCurrentTaskList";

    private readonly PowerParserContext _context;

    public PowerLineParser(PowerParserContext? context = null)
    {
        _context = context ?? new PowerParserContext();
    }

    public PowerParserContext Context => _context;

    public PowerParseResult Parse(string? content, DateTimeOffset timestamp = default)
    {
        lock (_context)
        {
            if (content is null)
            {
                _context.ResetEntityContext();
                return _context.RecordMalformed("input.null", "Power line content is null.");
            }

            if (content.Length > MaximumInputCharacters)
            {
                _context.ResetEntityContext();
                return _context.RecordMalformed(
                    "input.too_long",
                    $"Power line exceeds the {MaximumInputCharacters} character limit.");
            }

            var payload = StripKnownPrefix(content).Trim();
            if (payload.Length == 0 || payload.StartsWith(EndCurrentTaskListPrefix, StringComparison.Ordinal))
            {
                ClearCreationContext();
                return _context.RecordIgnored();
            }

            if (_context.IsRecoveringBlock &&
                !payload.StartsWith("BLOCK_START", StringComparison.Ordinal) &&
                !payload.StartsWith("BLOCK_END", StringComparison.Ordinal))
            {
                _context.ResetEntityContext();
                return _context.RecordIgnored();
            }

            if (payload.StartsWith("GameEntity", StringComparison.Ordinal))
            {
                return ParseGameEntity(payload, timestamp);
            }

            if (payload.StartsWith("Player", StringComparison.Ordinal))
            {
                return ParsePlayerEntity(payload, timestamp);
            }

            if (payload.StartsWith("TAG_CHANGE", StringComparison.Ordinal))
            {
                return ParseTagChange(payload, timestamp);
            }

            if (payload.StartsWith("FULL_ENTITY", StringComparison.Ordinal))
            {
                return ParseFullEntity(payload, timestamp);
            }

            if (payload.StartsWith("SHOW_ENTITY", StringComparison.Ordinal))
            {
                return ParseUpdatedEntity(payload, timestamp, isReveal: true);
            }

            if (payload.StartsWith("CHANGE_ENTITY", StringComparison.Ordinal))
            {
                return ParseUpdatedEntity(payload, timestamp, isReveal: false);
            }

            if (payload.StartsWith("BLOCK_START", StringComparison.Ordinal))
            {
                return ParseBlockStart(payload, timestamp);
            }

            if (payload.StartsWith("BLOCK_END", StringComparison.Ordinal))
            {
                return ParseBlockEnd(payload, timestamp);
            }

            if (payload.StartsWith("tag=", StringComparison.Ordinal))
            {
                return ParseCurrentEntityTag(payload, timestamp);
            }

            ClearCreationContext();
            return _context.RecordUnknown(
                new UnknownPowerEvent(timestamp, _context.CurrentBlockId, payload));
        }
    }

    private PowerParseResult ParseGameEntity(string payload, DateTimeOffset timestamp)
    {
        var match = PowerLinePatterns.GameEntity().Match(payload);
        if (!TryReadInt32(match, "id", out var entityId))
        {
            return Malformed("GameEntity", "GameEntity is missing a valid EntityID.");
        }

        _context.CurrentEntityId = entityId;
        ClearCreationContext();
        return _context.RecordParsed(
            new GameEntityDeclared(timestamp, _context.CurrentBlockId, entityId));
    }

    private PowerParseResult ParsePlayerEntity(string payload, DateTimeOffset timestamp)
    {
        var match = PowerLinePatterns.PlayerEntity().Match(payload);
        if (!TryReadInt32(match, "id", out var entityId) ||
            !TryReadInt32(match, "playerId", out var playerId))
        {
            return Malformed("PlayerEntity", "Player entity declaration is malformed.");
        }

        var accountId = match.Groups["account"].Value;
        _context.CurrentEntityId = entityId;
        ClearCreationContext();
        return _context.RecordParsed(
            new PlayerEntityDeclared(
                timestamp,
                _context.CurrentBlockId,
                entityId,
                playerId,
                accountId));
    }

    private PowerParseResult ParseTagChange(string payload, DateTimeOffset timestamp)
    {
        var match = PowerLinePatterns.TagChange().Match(payload);
        if (!match.Success)
        {
            return Malformed("TAG_CHANGE", "TAG_CHANGE is missing an entity, tag, or value.");
        }

        var entity = ParseEntityReference(match.Groups["entity"].Value);
        if (entity.EntityId is null && entity.EntityName is null)
        {
            return Malformed("TAG_CHANGE.entity", "TAG_CHANGE contains an empty entity reference.");
        }

        ClearCreationContext();
        return _context.RecordParsed(
            new RawTagChanged(
                timestamp,
                _context.CurrentBlockId,
                entity.EntityId,
                entity.EntityName,
                match.Groups["tag"].Value,
                match.Groups["value"].Value,
                IsCreationTag: false));
    }

    private PowerParseResult ParseFullEntity(string payload, DateTimeOffset timestamp)
    {
        var creating = PowerLinePatterns.FullEntityCreating().Match(payload);
        int entityId;
        string cardId;

        if (TryReadInt32(creating, "id", out entityId))
        {
            cardId = creating.Groups["cardId"].Value;
        }
        else
        {
            var updating = PowerLinePatterns.FullEntityUpdating().Match(payload);
            if (!updating.Success)
            {
                return Malformed("FULL_ENTITY", "FULL_ENTITY is missing a valid entity or CardID field.");
            }

            var entity = ParseEntityReference(updating.Groups["entity"].Value);
            if (entity.EntityId is not int parsedEntityId)
            {
                return Malformed("FULL_ENTITY.entity", "FULL_ENTITY requires a numeric entity id.");
            }

            entityId = parsedEntityId;
            cardId = updating.Groups["cardId"].Value;
        }

        _context.CurrentEntityId = entityId;
        _context.CurrentCreationEntityId = entityId;
        return _context.RecordParsed(
            new EntityCreated(timestamp, _context.CurrentBlockId, entityId, cardId));
    }

    private PowerParseResult ParseUpdatedEntity(
        string payload,
        DateTimeOffset timestamp,
        bool isReveal)
    {
        var match = (isReveal ? PowerLinePatterns.ShowEntity() : PowerLinePatterns.ChangeEntity()).Match(payload);
        if (!match.Success)
        {
            return Malformed(
                isReveal ? "SHOW_ENTITY" : "CHANGE_ENTITY",
                $"{(isReveal ? "SHOW_ENTITY" : "CHANGE_ENTITY")} is missing an entity or CardID field.");
        }

        var entity = ParseEntityReference(match.Groups["entity"].Value);
        if (entity.EntityId is null && entity.EntityName is null)
        {
            return Malformed(
                isReveal ? "SHOW_ENTITY.entity" : "CHANGE_ENTITY.entity",
                "Updated entity reference is empty.");
        }

        _context.CurrentEntityId = entity.EntityId;
        ClearCreationContext();
        var cardId = match.Groups["cardId"].Value;
        GameEvent gameEvent = isReveal
            ? new EntityRevealed(
                timestamp,
                _context.CurrentBlockId,
                entity.EntityId,
                entity.EntityName,
                cardId)
            : new EntityChanged(
                timestamp,
                _context.CurrentBlockId,
                entity.EntityId,
                entity.EntityName,
                cardId);
        return _context.RecordParsed(gameEvent);
    }

    private PowerParseResult ParseBlockStart(string payload, DateTimeOffset timestamp)
    {
        ClearCreationContext();
        if (_context.IsRecoveringBlock)
        {
            _context.EnterBlockRecovery();
            _context.ResetEntityContext();
            return _context.RecordIgnored();
        }

        var header = PowerLinePatterns.BlockStartHeader().Match(payload);
        if (!header.Success)
        {
            return MalformedBlockStart(
                "BLOCK_START.header",
                "BLOCK_START is missing BlockType or Entity.");
        }

        if (!TryParseBlockDetails(header.Groups["details"].Value, out var details))
        {
            return MalformedBlockStart(
                "BLOCK_START.details",
                "BLOCK_START details are malformed.");
        }

        if (!_context.CanPushBlock)
        {
            return MalformedBlockStart(
                "BLOCK_START.depth",
                $"Power block nesting exceeds the {PowerParserContext.MaximumBlockDepth} level limit.");
        }

        var entity = ParseEntityReference(details.Entity);
        if (entity.EntityId is null && entity.EntityName is null)
        {
            return MalformedBlockStart(
                "BLOCK_START.entity",
                "BLOCK_START contains an empty entity reference.");
        }

        var block = _context.PushBlock(
            header.Groups["type"].Value,
            entity.EntityId,
            entity.EntityName,
            details.EffectCardId,
            details.Target,
            details.SubOption,
            details.TriggerKeyword);
        return _context.RecordParsed(new BlockStarted(timestamp, block));
    }

    private PowerParseResult ParseBlockEnd(string payload, DateTimeOffset timestamp)
    {
        ClearCreationContext();
        if (!PowerLinePatterns.BlockEnd().IsMatch(payload))
        {
            return Malformed("BLOCK_END", "BLOCK_END contains unexpected data.");
        }

        if (_context.TryConsumeRejectedBlockEnd())
        {
            _context.ResetEntityContext();
            return _context.RecordIgnored();
        }

        if (!_context.TryPopBlock(out var block))
        {
            return Malformed("BLOCK_END.unbalanced", "BLOCK_END has no matching BLOCK_START.");
        }

        return _context.RecordParsed(new BlockEnded(timestamp, block));
    }

    private PowerParseResult ParseCurrentEntityTag(string payload, DateTimeOffset timestamp)
    {
        var match = PowerLinePatterns.CurrentEntityTag().Match(payload);
        if (!match.Success)
        {
            return Malformed("entity_tag", "Entity tag line is missing a tag or value.");
        }

        if (_context.CurrentEntityId is not int entityId)
        {
            return Malformed("entity_tag.context", "Entity tag line has no current entity context.");
        }

        return _context.RecordParsed(
            new RawTagChanged(
                timestamp,
                _context.CurrentBlockId,
                entityId,
                EntityName: null,
                match.Groups["tag"].Value,
                match.Groups["value"].Value,
                IsCreationTag: _context.CurrentCreationEntityId == entityId));
    }

    private PowerParseResult Malformed(string pattern, string diagnostic)
    {
        _context.ResetEntityContext();
        return _context.RecordMalformed(pattern, diagnostic);
    }

    private PowerParseResult MalformedBlockStart(string pattern, string diagnostic)
    {
        _context.EnterBlockRecovery();
        return Malformed(pattern, diagnostic);
    }

    private void ClearCreationContext()
    {
        _context.CurrentCreationEntityId = null;
    }

    private static string StripKnownPrefix(string content)
    {
        if (content.StartsWith(PowerTaskListPrefix, StringComparison.Ordinal))
        {
            return content[PowerTaskListPrefix.Length..];
        }

        if (content.StartsWith(GameStatePrefix, StringComparison.Ordinal))
        {
            return content[GameStatePrefix.Length..];
        }

        return content;
    }

    private static EntityReference ParseEntityReference(string rawEntity)
    {
        var normalized = rawEntity.Trim();
        const string unknownEntityPrefix = "UNKNOWN ENTITY ";
        if (normalized.StartsWith(unknownEntityPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[unknownEntityPrefix.Length..].Trim();
        }

        if (TryReadInt32(normalized, out var numericEntityId) && numericEntityId >= 0)
        {
            return new EntityReference(numericEntityId, null);
        }

        var entityIdMatch = PowerLinePatterns.EntityId().Match(normalized);
        int? entityId = TryReadInt32(entityIdMatch, "id", out var descriptorEntityId)
            ? descriptorEntityId
            : null;
        var nameMatch = PowerLinePatterns.EntityName().Match(normalized);
        var entityName = nameMatch.Success
            ? NullIfEmpty(nameMatch.Groups["name"].Value.Trim())
            : entityId is null
                ? NullIfEmpty(normalized.Trim('[', ']'))
                : null;
        return new EntityReference(entityId, entityName);
    }

    private static bool TryParseBlockDetails(string value, out BlockDetails details)
    {
        const string entityPrefix = "Entity=";
        const string effectCardIdMarker = " EffectCardId=";
        const string effectIndexMarker = " EffectIndex=";
        const string targetMarker = " Target=";
        const string subOptionMarker = " SubOption=";
        const string triggerKeywordMarker = " TriggerKeyword=";

        details = default;
        if (!value.StartsWith(entityPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var effectIndexStart = value.IndexOf(effectIndexMarker, StringComparison.Ordinal);
        if (effectIndexStart < entityPrefix.Length)
        {
            return false;
        }

        var entityAndEffect = value[entityPrefix.Length..effectIndexStart];
        var effectCardIdStart = entityAndEffect.LastIndexOf(effectCardIdMarker, StringComparison.Ordinal);
        var entity = effectCardIdStart >= 0
            ? entityAndEffect[..effectCardIdStart]
            : entityAndEffect;
        var effectCardId = effectCardIdStart >= 0
            ? entityAndEffect[(effectCardIdStart + effectCardIdMarker.Length)..]
            : string.Empty;

        var afterEffectIndex = effectIndexStart + effectIndexMarker.Length;
        var targetStart = value.IndexOf(targetMarker, afterEffectIndex, StringComparison.Ordinal);
        if (targetStart < 0 ||
            !TryReadInt32(value[afterEffectIndex..targetStart], out _))
        {
            return false;
        }

        var tail = value[(targetStart + targetMarker.Length)..].TrimEnd();
        string? triggerKeyword = null;
        var triggerStart = tail.LastIndexOf(triggerKeywordMarker, StringComparison.Ordinal);
        if (triggerStart >= 0)
        {
            triggerKeyword = NullIfEmpty(tail[(triggerStart + triggerKeywordMarker.Length)..]);
            if (triggerKeyword is null)
            {
                return false;
            }

            tail = tail[..triggerStart];
        }

        int? subOption = null;
        var subOptionStart = tail.LastIndexOf(subOptionMarker, StringComparison.Ordinal);
        if (subOptionStart >= 0)
        {
            if (!TryReadInt32(tail[(subOptionStart + subOptionMarker.Length)..], out var parsedSubOption))
            {
                return false;
            }

            subOption = parsedSubOption;
            tail = tail[..subOptionStart];
        }

        entity = entity.Trim();
        if (entity.Length == 0)
        {
            return false;
        }

        details = new BlockDetails(
            entity,
            effectCardId,
            tail.Trim(),
            subOption,
            triggerKeyword);
        return true;
    }

    private static bool TryReadInt32(Match match, string groupName, out int value)
    {
        value = default;
        return match.Success && TryReadInt32(match.Groups[groupName].Value, out value);
    }

    private static bool TryReadInt32(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private readonly record struct EntityReference(int? EntityId, string? EntityName);

    private readonly record struct BlockDetails(
        string Entity,
        string EffectCardId,
        string Target,
        int? SubOption,
        string? TriggerKeyword);
}

internal static partial class PowerLinePatterns
{
    private const RegexOptions Options =
        RegexOptions.CultureInvariant |
        RegexOptions.ExplicitCapture |
        RegexOptions.NonBacktracking;

    [GeneratedRegex(@"^GameEntity\s+EntityID=(?<id>[0-9]+)\s*$", Options)]
    internal static partial Regex GameEntity();

    [GeneratedRegex(@"^Player\s+EntityID=(?<id>[0-9]+)\s+PlayerID=(?<playerId>[0-9]+)\s+GameAccountId=(?<account>.+?)\s*$", Options)]
    internal static partial Regex PlayerEntity();

    [GeneratedRegex(@"^TAG_CHANGE\s+Entity=(?<entity>.+?)\s+tag=(?<tag>[A-Za-z0-9_]+)\s+value=(?<value>[^\s]+)(?:\s+.*)?$", Options)]
    internal static partial Regex TagChange();

    [GeneratedRegex(@"^FULL_ENTITY\s+-\s+Creating\s+ID=(?<id>[0-9]+)\s+CardID=(?<cardId>[^\s]*)\s*$", Options)]
    internal static partial Regex FullEntityCreating();

    [GeneratedRegex(@"^FULL_ENTITY\s+-\s+Updating(?:\s+Entity=)?(?<entity>.+?)\s+CardID=(?<cardId>[^\s]*)\s*$", Options)]
    internal static partial Regex FullEntityUpdating();

    [GeneratedRegex(@"^SHOW_ENTITY\s+-\s+Updating\s+Entity=(?<entity>.+?)\s+CardID=(?<cardId>[^\s]*)\s*$", Options)]
    internal static partial Regex ShowEntity();

    [GeneratedRegex(@"^CHANGE_ENTITY\s+-\s+Updating\s+Entity=(?<entity>.+?)\s+CardID=(?<cardId>[^\s]*)\s*$", Options)]
    internal static partial Regex ChangeEntity();

    [GeneratedRegex(@"^BLOCK_START\s+BlockType=(?<type>[^\s]+)\s+(?<details>.+)$", Options)]
    internal static partial Regex BlockStartHeader();

    [GeneratedRegex(@"^BLOCK_END\s*$", Options)]
    internal static partial Regex BlockEnd();

    [GeneratedRegex(@"^tag=(?<tag>[A-Za-z0-9_]+)\s+value=(?<value>[^\s]+)(?:\s+.*)?$", Options)]
    internal static partial Regex CurrentEntityTag();

    [GeneratedRegex(@"(?:^|[\s\[])id=(?<id>[0-9]+)(?:$|[\s\]])", Options)]
    internal static partial Regex EntityId();

    [GeneratedRegex(@"(?:^|\[)name=(?<name>.*?)\s+id=[0-9]+(?:\s|\])", Options)]
    internal static partial Regex EntityName();
}
