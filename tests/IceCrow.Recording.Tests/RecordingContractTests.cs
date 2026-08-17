using System.Globalization;
using IceCrow.Hearthstone.Protocol;
using IceCrow.Hearthstone.Protocol.Events;

namespace IceCrow.Recording.Tests;

/// <summary>
/// Write/read contract: any recording the official write path accepts must
/// load again through the official read path. The shapes mirror the ways a
/// real capture stresses the read preflight hardest relative to the write
/// budget: token-overhead-dominated tag floods, escape-inflated non-ASCII
/// strings, and escape-heavy ASCII content.
/// </summary>
public sealed class RecordingContractTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        16,
        21,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task MaximumEventCountTagFloodRoundTrips()
    {
        // The real-capture shape: token overhead dominates because the events
        // carry almost no string payload. This is the exact shape that the
        // old preflight rejected at 61,543 events.
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);
        for (var index = 0; index < RecordingSerializer.MaximumEventCount - 2; index++)
        {
            recorder.Record(new RawTagChanged(
                Timestamp.AddMilliseconds(index),
                BlockId: null,
                EntityId: (index % 500) + 1,
                EntityName: null,
                Tag: "1710",
                Value: (index % 7).ToString(CultureInfo.InvariantCulture),
                IsCreationTag: false));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));

        var roundTrip = await RoundTripAsync(recorder.CreateMatch());

        Assert.Equal(RecordingSerializer.MaximumEventCount, roundTrip.Events.Count);
    }

    [Fact]
    public async Task EscapeInflatedNonAsciiStringsRoundTrip()
    {
        // Non-ASCII text is escaped as \uXXXX on disk: six encoded bytes per
        // character against the two bytes the write budget charged.
        var name = new string('В', 400); // 'В'
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);
        for (var index = 0; index < 4_000; index++)
        {
            recorder.Record(new EntityRevealed(
                Timestamp.AddMilliseconds(index),
                BlockId: null,
                EntityId: index + 1,
                EntityName: name,
                CardId: "BG_REAL_SHAPE_001"));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));

        var roundTrip = await RoundTripAsync(recorder.CreateMatch());

        Assert.Equal(name, roundTrip.Events[1].EntityName);
    }

    [Fact]
    public async Task EscapeHeavyAsciiContentRoundTrips()
    {
        var content = string.Concat(Enumerable.Repeat("\"\\\n\t", 100));
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);
        for (var index = 0; index < 4_000; index++)
        {
            recorder.Record(new UnknownPowerEvent(
                Timestamp.AddMilliseconds(index),
                BlockId: null,
                Content: content));
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));

        var roundTrip = await RoundTripAsync(recorder.CreateMatch());

        Assert.Equal(content, roundTrip.Events[1].Content);
    }

    [Fact]
    public void RetainedBudgetAcceptsUpToTheCapAndRejectsTheCrossingEvent()
    {
        // One shared max-length string keeps the test cheap in real memory
        // while every event still charges the full per-event estimate
        // (256 bytes base + 2 bytes per string character).
        var content = new string('x', RecordingSerializer.MaximumStringCharacters);
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);

        var accepted = 0;
        var recordUntilRejected = () =>
        {
            while (accepted < 1_000)
            {
                recorder.Record(new UnknownPowerEvent(
                    Timestamp.AddMilliseconds(accepted),
                    BlockId: null,
                    Content: content));
                accepted++;
            }
        };
        var exception = Assert.Throws<InvalidOperationException>(recordUntilRejected);

        Assert.Contains("retain", exception.Message, StringComparison.Ordinal);
        var perEventEstimate = 256L + (2L * content.Length);
        Assert.True(accepted > 0);
        Assert.True(recorder.RetainedBytes <= RecordingSerializer.MaximumRetainedBytes);
        Assert.True(
            recorder.RetainedBytes > RecordingSerializer.MaximumRetainedBytes - perEventEstimate,
            "The recorder must accept events until the next one would cross the retained budget.");
        Assert.Equal(accepted + 1, recorder.EventCount);
    }

    [Theory]
    [InlineData("tag-flood")]
    [InlineData("cyrillic-names")]
    public async Task RealShapedRecordingsScaleToTheRetainedBudgetWithinTheFileCap(string shape)
    {
        // Contract-tested upper bound, not a mathematical guarantee: for the
        // realistic capture shapes (token-dominated tag floods and short
        // non-ASCII entity names), bytes-on-disk grow no faster relative to
        // the retained estimate than MaximumFileBytes relative to
        // MaximumRetainedBytes — so a full-retained-budget match of these
        // shapes serializes under the 128 MiB file cap. Adversarial
        // max-length non-ASCII strings can exceed the cap and fail closed at
        // save time instead (see the serializer relationship comment).
        var recorder = new MatchRecorder(Timestamp);
        recorder.RecordMatchStarted(Timestamp, localPlayerId: 4);
        for (var index = 0; index < 20_000; index++)
        {
            if (shape == "tag-flood")
            {
                recorder.Record(new RawTagChanged(
                    Timestamp.AddMilliseconds(index),
                    BlockId: null,
                    EntityId: (index % 500) + 1,
                    EntityName: null,
                    Tag: "1710",
                    Value: (index % 7).ToString(CultureInfo.InvariantCulture),
                    IsCreationTag: false));
            }
            else
            {
                recorder.Record(new EntityRevealed(
                    Timestamp.AddMilliseconds(index),
                    BlockId: null,
                    EntityId: index + 1,
                    EntityName: "Богатый мертвяк",
                    CardId: "BG_REAL_SHAPE_001"));
            }
        }

        recorder.RecordMatchEnded(Timestamp.AddHours(1));
        var retainedBytes = recorder.RetainedBytes;

        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, recorder.CreateMatch());

        Assert.True(
            stream.Length * RecordingSerializer.MaximumRetainedBytes <=
            RecordingSerializer.MaximumFileBytes * retainedBytes,
            $"Shape '{shape}': {stream.Length} disk bytes for {retainedBytes} retained bytes " +
            "would exceed the file cap when scaled to the full retained budget.");
    }

    [Fact]
    public async Task HostileTokenFloodIsStillRejectedByThePreflight()
    {
        // A hand-written non-schema document with millions of tiny tokens
        // must still fail fast before materialization.
        await using var hostile = new MemoryStream();
        await using (var writer = new StreamWriter(hostile, leaveOpen: true))
        {
            await writer.WriteAsync(
                "{\"formatVersion\":1,\"startedAt\":\"2026-08-16T21:00:00+00:00\",\"events\":[");
            await writer.WriteAsync("{\"a\":[");
            for (var index = 0; index < 26_000_000; index++)
            {
                await writer.WriteAsync("0,");
            }

            await writer.WriteAsync("0]}]}");
        }

        hostile.Position = 0;

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RecordingSerializer.DeserializeAsync(hostile));
        Assert.Contains("preflight materialization", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<RecordedMatch> RoundTripAsync(RecordedMatch match)
    {
        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, match);
        Assert.True(stream.Length <= RecordingSerializer.MaximumFileBytes);
        stream.Position = 0;
        return await RecordingSerializer.DeserializeAsync(stream);
    }
}
