using System.Text;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;
using IceCrow.Recording.Tests.Fixtures;

namespace IceCrow.Recording.Tests;

public sealed class RecordingSerializerTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        13,
        21,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task RoundTripPreservesEventsAndCheckpointsWithoutTypeMetadata()
    {
        var expected = DeterministicMatchFixture.Create();
        await using var stream = new MemoryStream();

        await RecordingSerializer.SerializeAsync(stream, expected);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        stream.Position = 0;
        var actual = await RecordingSerializer.DeserializeAsync(
            stream);

        Assert.Equal(expected.FormatVersion, actual.FormatVersion);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.Events, actual.Events);
        Assert.Equal(expected.Checkpoints, actual.Checkpoints);
        Assert.DoesNotContain("$type", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IceCrow.Hearthstone", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryCurrentNormalizedEventHasAnExplicitRoundTripDiscriminator()
    {
        var block = new PowerBlock(
            Id: 7,
            ParentId: null,
            Depth: 0,
            Type: "TRIGGER",
            EntityId: 1,
            EntityName: null,
            EffectCardId: string.Empty,
            Target: "0",
            SubOption: null,
            TriggerKeyword: null);
        GameEvent[] normalizedEvents =
        [
            new GameEntityDeclared(Timestamp, null, 1),
            new PlayerEntityDeclared(Timestamp, null, 2, 1, "account"),
            new EntityCreated(Timestamp, null, 3, string.Empty),
            new EntityRevealed(Timestamp, null, 3, "Minion", "CARD_1"),
            new EntityChanged(Timestamp, null, 3, "Minion", "CARD_2"),
            new RawTagChanged(Timestamp, null, 3, null, "ATK", "5", false),
            new BlockStarted(Timestamp, block),
            new BlockEnded(Timestamp, block),
            new UnknownPowerEvent(Timestamp, null, "FUTURE_EVENT value=1"),
        ];
        var match = new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            Timestamp,
            [
                RecordedEvent.CreateMatchStarted(Timestamp),
                .. normalizedEvents.Select(RecordedEvent.FromGameEvent),
                RecordedEvent.CreateMatchEnded(Timestamp),
            ]);
        await using var stream = new MemoryStream();

        await RecordingSerializer.SerializeAsync(stream, match);
        stream.Position = 0;
        var roundTrip = await RecordingSerializer.DeserializeAsync(
            stream);

        Assert.Equal(match.Events, roundTrip.Events);
        Assert.Equal(
            Enum.GetValues<RecordedEventType>().Except(
                [RecordedEventType.MatchStarted, RecordedEventType.MatchEnded]),
            roundTrip.Events
                .Where(static recordedEvent => recordedEvent.Type is not
                    RecordedEventType.MatchStarted and not
                    RecordedEventType.MatchEnded)
                .Select(static recordedEvent => recordedEvent.Type));
    }

    [Fact]
    public async Task UnsupportedFormatVersionIsRejected()
    {
        const string json = """
            {
              "formatVersion": 2,
              "startedAt": "2026-08-13T21:00:00+00:00",
              "events": []
            }
            """;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => Deserialize(json));

        Assert.Contains("formatVersion 2", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"futureRuntimeType\"")]
    [InlineData("0")]
    public async Task UnknownOrNumericEventDiscriminatorIsRejected(string discriminator)
    {
        var json = $$"""
            {
              "formatVersion": 1,
              "startedAt": "2026-08-13T21:00:00+00:00",
              "events": [
                {
                  "type": {{discriminator}},
                  "timestamp": "2026-08-13T21:00:00+00:00"
                }
              ]
            }
            """;

        await Assert.ThrowsAsync<InvalidDataException>(() => Deserialize(json));
    }

    [Fact]
    public async Task UnknownJsonPropertiesAreRejected()
    {
        const string json = """
            {
              "formatVersion": 1,
              "startedAt": "2026-08-13T21:00:00+00:00",
              "$type": "System.Object, System.Private.CoreLib",
              "events": []
            }
            """;

        await Assert.ThrowsAsync<InvalidDataException>(() => Deserialize(json));
    }

    [Theory]
    [InlineData("[{\"type\":\"matchEnded\",\"timestamp\":\"2026-08-13T21:00:00+00:00\"}]")]
    [InlineData("[{\"type\":\"matchStarted\",\"timestamp\":\"2026-08-13T21:00:00+00:00\"},{\"type\":\"matchStarted\",\"timestamp\":\"2026-08-13T21:00:01+00:00\"}]")]
    [InlineData("[{\"type\":\"matchStarted\",\"timestamp\":\"2026-08-13T21:00:00+00:00\"},{\"type\":\"matchEnded\",\"timestamp\":\"2026-08-13T21:00:01+00:00\"},{\"type\":\"gameEntityDeclared\",\"timestamp\":\"2026-08-13T21:00:02+00:00\",\"entityId\":1}]")]
    public async Task InvalidMatchLifecycleIsRejected(string events)
    {
        var json = $$"""
            {
              "formatVersion": 1,
              "startedAt": "2026-08-13T21:00:00+00:00",
              "events": {{events}}
            }
            """;

        await Assert.ThrowsAsync<InvalidDataException>(() => Deserialize(json));
    }

    [Fact]
    public void EventCountAndStringLengthsAreBounded()
    {
        var validEvent = RecordedEvent.CreateMatchEnded(Timestamp);
        var tooManyEvents = Enumerable
            .Repeat(validEvent, RecordingSerializer.MaximumEventCount + 1)
            .ToArray();
        var oversizedString = new RecordedEvent
        {
            Type = RecordedEventType.UnknownPower,
            Timestamp = Timestamp,
            Content = new string('x', RecordingSerializer.MaximumStringCharacters + 1),
        };

        Assert.Throws<InvalidDataException>(() => new RecordedMatch(
            RecordedMatch.CurrentFormatVersion,
            Timestamp,
            tooManyEvents));
        Assert.Throws<InvalidDataException>(() => RecordingSerializer.Validate(
            new RecordedMatch(
                RecordedMatch.CurrentFormatVersion,
                Timestamp,
                [oversizedString])));
    }

    [Fact]
    public async Task ExternalEventCountIsRejectedBeforeObjectDeserialization()
    {
        var events = string.Join(
            ',',
            Enumerable.Repeat("null", RecordingSerializer.MaximumEventCount + 1));
        var json = $$"""
            {
              "formatVersion": 1,
              "startedAt": "2026-08-13T21:00:00+00:00",
              "events": [{{events}}]
            }
            """;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => Deserialize(json));

        Assert.Contains("event limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileSizeIsRejectedBeforeReadingPayload()
    {
        await using var stream = new OversizedSeekableStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RecordingSerializer.DeserializeAsync(stream));

        Assert.Equal(0, stream.ReadCount);
    }

    [Fact]
    public async Task SaveAndLoadUseTheVersionedFileFormat()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"IceCrow-RecordingTests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "match.icecrow.json");
        try
        {
            var expected = DeterministicMatchFixture.Create();

            await RecordingSerializer.SaveAsync(
                path,
                expected);
            var actual = await RecordingSerializer.LoadAsync(
                path);

            Assert.Equal(expected.Events, actual.Events);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MatchRecorderEnforcesLifecycleAndAggregateRetentionBudget()
    {
        var recorder = new MatchRecorder(Timestamp);
        Assert.Throws<InvalidOperationException>(() => recorder.RecordMatchEnded(Timestamp));
        recorder.RecordMatchStarted(Timestamp);
        Assert.Throws<InvalidOperationException>(() => recorder.RecordMatchStarted(Timestamp));

        var largeContent = new string('x', RecordingSerializer.MaximumStringCharacters);
        var exception = Assert.Throws<InvalidOperationException>(FillRecorder);

        Assert.Contains("estimated bytes", exception.Message, StringComparison.Ordinal);
        Assert.True(recorder.EventCount < RecordingSerializer.MaximumEventCount);
        recorder.RecordMatchEnded(Timestamp);
        Assert.Throws<InvalidOperationException>(() =>
            recorder.Record(new GameEntityDeclared(Timestamp, null, 1)));

        void FillRecorder()
        {
            while (true)
            {
                recorder.Record(new UnknownPowerEvent(Timestamp, null, largeContent));
            }
        }
    }

    private static async Task<RecordedMatch> Deserialize(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await RecordingSerializer.DeserializeAsync(stream);
    }

    private sealed class OversizedSeekableStream : Stream
    {
        public int ReadCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => RecordingSerializer.MaximumFileBytes + 1;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}
