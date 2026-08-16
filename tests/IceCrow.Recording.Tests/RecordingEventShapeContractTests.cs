using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests;

/// <summary>
/// Deterministic writer-to-reader contract matrix: every RecordedEventType is
/// exercised in its minimum shape, with near-limit strings, escaped ASCII,
/// non-ASCII text, nullable optional fields, block payloads, and both numeric
/// and name-only entity references. The guarantee is a contract-tested upper
/// bound — every generated match the writer accepts must deserialize — not a
/// mathematical identity between the two estimators.
/// </summary>
public sealed class RecordingEventShapeContractTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        17,
        10,
        0,
        0,
        TimeSpan.Zero);

    private static readonly string NearLimitString =
        new('x', RecordingSerializer.MaximumStringCharacters - 1);

    private static readonly string EscapedAscii =
        string.Concat(Enumerable.Repeat("\"\\\n\t<>&", 64));

    private static readonly string NonAscii =
        string.Concat(Enumerable.Repeat("Игрок№Ёж-таверна", 32));

    [Fact]
    public async Task EveryAcceptedEventShapeRoundTripsExactly()
    {
        var events = new List<RecordedEvent>
        {
            RecordedEvent.CreateMatchStarted(Timestamp, localPlayerId: 4),
        };
        events.AddRange(GenerateShapeMatrix().Select(RecordedEvent.FromGameEvent));
        events.Add(RecordedEvent.CreateMatchEnded(Timestamp.AddHours(1)));
        var match = new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            Timestamp,
            events);

        RecordingSerializer.Validate(match);
        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, match);
        Assert.True(stream.Length <= RecordingSerializer.MaximumFileBytes);
        stream.Position = 0;
        var roundTrip = await RecordingSerializer.DeserializeAsync(stream);

        Assert.Equal(match.Events, roundTrip.Events);
        Assert.Equal(
            Enum.GetValues<RecordedEventType>().Order(),
            roundTrip.Events.Select(static recordedEvent => recordedEvent.Type)
                .Distinct()
                .Order());
    }

    private static IEnumerable<GameEvent> GenerateShapeMatrix()
    {
        string[] textVariants = ["a", EscapedAscii, NonAscii];

        yield return new GameCreated(Timestamp);

        yield return new GameEntityDeclared(Timestamp, null, EntityId: 1);
        yield return new GameEntityDeclared(Timestamp, long.MaxValue, int.MaxValue);

        foreach (var account in textVariants)
        {
            yield return new PlayerEntityDeclared(Timestamp, null, 2, 4, account);
        }

        yield return new EntityCreated(Timestamp, null, 3, string.Empty);
        foreach (var cardId in textVariants)
        {
            yield return new EntityCreated(Timestamp, 7, 3, cardId);
        }

        foreach (var name in textVariants)
        {
            // Numeric, name-only, and combined entity references.
            yield return new EntityRevealed(Timestamp, null, 5, name, "CARD_1");
            yield return new EntityRevealed(Timestamp, null, null, name, "CARD_1");
            yield return new EntityChanged(Timestamp, null, 5, name, "CARD_2");
            yield return new EntityChanged(Timestamp, null, null, name, "CARD_2");
        }

        foreach (var value in textVariants)
        {
            yield return new RawTagChanged(Timestamp, null, 6, null, "TURN", value, false);
            yield return new RawTagChanged(Timestamp, null, null, value, "2022", "0", true);
        }

        yield return new RawTagChanged(
            Timestamp,
            null,
            null,
            NearLimitString,
            "STEP",
            "MAIN_READY",
            IsCreationTag: false);

        foreach (var block in GenerateBlockVariants())
        {
            yield return new BlockStarted(Timestamp, block);
            yield return new BlockEnded(Timestamp, block);
        }

        foreach (var content in textVariants)
        {
            yield return new UnknownPowerEvent(Timestamp, null, content);
        }
    }

    private static IEnumerable<PowerBlock> GenerateBlockVariants()
    {
        yield return new PowerBlock(
            Id: 1,
            ParentId: null,
            Depth: 0,
            Type: "TRIGGER",
            EntityId: null,
            EntityName: null,
            EffectCardId: string.Empty,
            Target: "0",
            SubOption: null,
            TriggerKeyword: null);
        yield return new PowerBlock(
            Id: long.MaxValue,
            ParentId: long.MaxValue - 1,
            Depth: 1_024,
            Type: EscapedAscii,
            EntityId: int.MaxValue,
            EntityName: NonAscii,
            EffectCardId: "BG_EFFECT_001",
            Target: NonAscii,
            SubOption: int.MaxValue,
            TriggerKeyword: EscapedAscii);
    }
}
