using System.Text.Json.Serialization;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording;

public enum RecordedEventType
{
    MatchStarted,
    MatchEnded,
    GameEntityDeclared,
    PlayerEntityDeclared,
    EntityCreated,
    EntityRevealed,
    EntityChanged,
    RawTagChanged,
    BlockStarted,
    BlockEnded,
    UnknownPower,
}

public sealed record RecordedEvent
{
    [JsonPropertyName("type")]
    public required RecordedEventType Type { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public long? BlockId { get; init; }

    public int? EntityId { get; init; }

    public int? PlayerId { get; init; }

    public string? EntityName { get; init; }

    public string? CardId { get; init; }

    public string? GameAccountId { get; init; }

    public string? Tag { get; init; }

    public string? Value { get; init; }

    public bool? IsCreationTag { get; init; }

    public string? Content { get; init; }

    public RecordedPowerBlock? Block { get; init; }

    public static RecordedEvent CreateMatchStarted(
        DateTimeOffset timestamp,
        int? localPlayerId = null) => new()
        {
            Type = RecordedEventType.MatchStarted,
            Timestamp = timestamp,
            PlayerId = localPlayerId,
        };

    public static RecordedEvent CreateMatchEnded(DateTimeOffset timestamp) => new()
    {
        Type = RecordedEventType.MatchEnded,
        Timestamp = timestamp,
    };

    public static RecordedEvent FromGameEvent(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            GameEntityDeclared declared => Base(
                RecordedEventType.GameEntityDeclared,
                declared) with
            {
                EntityId = declared.EntityId,
            },
            PlayerEntityDeclared declared => Base(
                RecordedEventType.PlayerEntityDeclared,
                declared) with
            {
                EntityId = declared.EntityId,
                PlayerId = declared.PlayerId,
                GameAccountId = declared.GameAccountId,
            },
            EntityCreated created => Base(
                RecordedEventType.EntityCreated,
                created) with
            {
                EntityId = created.EntityId,
                CardId = created.CardId,
            },
            EntityRevealed revealed => Base(
                RecordedEventType.EntityRevealed,
                revealed) with
            {
                EntityId = revealed.EntityId,
                EntityName = revealed.EntityName,
                CardId = revealed.CardId,
            },
            EntityChanged changed => Base(
                RecordedEventType.EntityChanged,
                changed) with
            {
                EntityId = changed.EntityId,
                EntityName = changed.EntityName,
                CardId = changed.CardId,
            },
            RawTagChanged tagChanged => Base(
                RecordedEventType.RawTagChanged,
                tagChanged) with
            {
                EntityId = tagChanged.EntityId,
                EntityName = tagChanged.EntityName,
                Tag = tagChanged.Tag,
                Value = tagChanged.Value,
                IsCreationTag = tagChanged.IsCreationTag,
            },
            BlockStarted started => Base(
                RecordedEventType.BlockStarted,
                started) with
            {
                Block = RecordedPowerBlock.FromProtocol(started.Block),
            },
            BlockEnded ended => Base(
                RecordedEventType.BlockEnded,
                ended) with
            {
                Block = RecordedPowerBlock.FromProtocol(ended.Block),
            },
            UnknownPowerEvent unknown => Base(
                RecordedEventType.UnknownPower,
                unknown) with
            {
                Content = unknown.Content,
            },
            _ => throw new NotSupportedException(
                $"Recording does not support normalized event '{gameEvent.GetType().Name}'."),
        };
    }

    internal GameEvent ToGameEvent() => Type switch
    {
        RecordedEventType.GameEntityDeclared => new GameEntityDeclared(
            Timestamp,
            BlockId,
            EntityId!.Value),
        RecordedEventType.PlayerEntityDeclared => new PlayerEntityDeclared(
            Timestamp,
            BlockId,
            EntityId!.Value,
            PlayerId!.Value,
            GameAccountId!),
        RecordedEventType.EntityCreated => new EntityCreated(
            Timestamp,
            BlockId,
            EntityId!.Value,
            CardId!),
        RecordedEventType.EntityRevealed => new EntityRevealed(
            Timestamp,
            BlockId,
            EntityId,
            EntityName,
            CardId!),
        RecordedEventType.EntityChanged => new EntityChanged(
            Timestamp,
            BlockId,
            EntityId,
            EntityName,
            CardId!),
        RecordedEventType.RawTagChanged => new RawTagChanged(
            Timestamp,
            BlockId,
            EntityId,
            EntityName,
            Tag!,
            Value!,
            IsCreationTag!.Value),
        RecordedEventType.BlockStarted => new BlockStarted(
            Timestamp,
            Block!.ToProtocol()),
        RecordedEventType.BlockEnded => new BlockEnded(
            Timestamp,
            Block!.ToProtocol()),
        RecordedEventType.UnknownPower => new UnknownPowerEvent(
            Timestamp,
            BlockId,
            Content!),
        _ => throw new InvalidOperationException(
            $"'{Type}' is a replay control event, not a normalized game event."),
    };

    private static RecordedEvent Base(RecordedEventType type, GameEvent gameEvent) => new()
    {
        Type = type,
        Timestamp = gameEvent.Timestamp,
        BlockId = gameEvent.BlockId,
    };
}

public sealed record RecordedPowerBlock(
    long Id,
    long? ParentId,
    int Depth,
    string Type,
    int? EntityId,
    string? EntityName,
    string EffectCardId,
    string Target,
    int? SubOption,
    string? TriggerKeyword)
{
    internal static RecordedPowerBlock FromProtocol(PowerBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return new RecordedPowerBlock(
            block.Id,
            block.ParentId,
            block.Depth,
            block.Type,
            block.EntityId,
            block.EntityName,
            block.EffectCardId,
            block.Target,
            block.SubOption,
            block.TriggerKeyword);
    }

    internal PowerBlock ToProtocol() => new(
        Id,
        ParentId,
        Depth,
        Type,
        EntityId,
        EntityName,
        EffectCardId,
        Target,
        SubOption,
        TriggerKeyword);
}
