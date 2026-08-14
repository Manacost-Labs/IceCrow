using System.Text;
using IceCrow.FixtureTool;
using IceCrow.Recording;

namespace IceCrow.FixtureTool.Tests;

public sealed class FixtureToolTests
{
    private static readonly DateTimeOffset Timestamp = new(
        2026,
        8,
        14,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task AnonymizationIsDeterministicAndPreservesProtocolIdentity()
    {
        var source = CreateSensitiveRecording();
        var first = new RecordingAnonymizer().Anonymize(source);
        var second = new RecordingAnonymizer().Anonymize(source);

        await using var firstJson = new MemoryStream();
        await using var secondJson = new MemoryStream();
        await RecordingSerializer.SerializeAsync(firstJson, first);
        await RecordingSerializer.SerializeAsync(secondJson, second);

        Assert.Equal(firstJson.ToArray(), secondJson.ToArray());
        var serialized = Encoding.UTF8.GetString(firstJson.ToArray());
        Assert.DoesNotContain("SecretHero#1234", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("hi=123 lo=456", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\private\capture.log", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("vs Alice", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_TOKEN_123", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("name=Alice", serialized, StringComparison.Ordinal);
        Assert.Contains("Player_1", serialized, StringComparison.Ordinal);
        Assert.Contains("Account_1", serialized, StringComparison.Ordinal);
        Assert.Contains("Checkpoint_0001", serialized, StringComparison.Ordinal);
        Assert.Contains("REDACTED_VALUE", serialized, StringComparison.Ordinal);
        Assert.Contains("Entity_17", serialized, StringComparison.Ordinal);
        Assert.Equal(17, first.Events[1].EntityId);
        Assert.Equal(3, first.Events[1].PlayerId);
        Assert.Equal("BG_TEST_CARD", first.Events[2].CardId);
        Assert.Equal("PLAYER_TECH_LEVEL", first.Events[3].Tag);
        Assert.Equal(source.Events.Select(static item => item.Timestamp),
            first.Events.Select(static item => item.Timestamp));
    }

    [Fact]
    public void ManifestRejectsPathTraversalAndUnknownSourceType()
    {
        var manifest = CreateManifest() with { InputFile = "../recording.json" };
        Assert.Throws<InvalidDataException>(() => FixtureManifestSerializer.Validate(manifest));

        manifest = CreateManifest() with { SourceType = "real" };
        Assert.Throws<InvalidDataException>(() => FixtureManifestSerializer.Validate(manifest));
    }

    [Fact]
    public async Task ManifestRejectsMalformedAndUnmappedJson()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "expected.json");
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"unexpected\":true}");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                FixtureManifestSerializer.LoadAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ImportCreatesValidatedCandidateWithoutOverwriting()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var input = Path.Combine(directory, "source.icecrow.json");
            var output = Path.Combine(directory, "candidate");
            await RecordingSerializer.SaveAsync(input, CreateSensitiveRecording());

            var created = await FixtureImporter.ImportAsync(
                input,
                output,
                new FixtureImportOptions(
                    "captured-match",
                    FixtureSourceTypes.RealAnonymized,
                    "Regression for SecretHero#1234 at C:\\Users\\private\\capture.log"));

            Assert.Equal(output, created);
            Assert.True(File.Exists(Path.Combine(output, FixtureImporter.RecordingFileName)));
            var result = await FixtureGoldenRunner.RunAsync(output);
            Assert.Equal("captured-match", result.FixtureName);
            Assert.Equal(FixtureSourceTypes.RealAnonymized, result.SourceType);
            var readme = await File.ReadAllTextAsync(Path.Combine(output, "README.md"));
            Assert.DoesNotContain("SecretHero#1234", readme, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users\\private", readme, StringComparison.Ordinal);

            await Assert.ThrowsAsync<IOException>(() => FixtureImporter.ImportAsync(
                input,
                output,
                new FixtureImportOptions(
                    "captured-match",
                    FixtureSourceTypes.RealAnonymized,
                    "Must not overwrite")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RecordedMatch CreateSensitiveRecording() => new(
        RecordedMatch.CurrentFormatVersion,
        Timestamp,
        [
            RecordedEvent.CreateMatchStarted(Timestamp, localPlayerId: 3),
            new RecordedEvent
            {
                Type = RecordedEventType.PlayerEntityDeclared,
                Timestamp = Timestamp.AddMilliseconds(1),
                EntityId = 17,
                PlayerId = 3,
                GameAccountId = "[hi=123 lo=456]",
            },
            new RecordedEvent
            {
                Type = RecordedEventType.EntityRevealed,
                Timestamp = Timestamp.AddMilliseconds(2),
                EntityId = 17,
                EntityName = "SecretHero#1234",
                CardId = "BG_TEST_CARD",
            },
            new RecordedEvent
            {
                Type = RecordedEventType.RawTagChanged,
                Timestamp = Timestamp.AddMilliseconds(3),
                EntityId = 17,
                EntityName = "SecretHero#1234",
                Tag = "PLAYER_TECH_LEVEL",
                Value = "2",
                IsCreationTag = false,
            },
            new RecordedEvent
            {
                Type = RecordedEventType.RawTagChanged,
                Timestamp = Timestamp.AddMilliseconds(4),
                EntityId = 17,
                Tag = "CUSTOM_DIAGNOSTIC",
                Value = "apiKey=PRIVATE_TOKEN_123",
                IsCreationTag = false,
            },
            new RecordedEvent
            {
                Type = RecordedEventType.BlockStarted,
                Timestamp = Timestamp.AddMilliseconds(5),
                Block = new RecordedPowerBlock(
                    Id: 1,
                    ParentId: null,
                    Depth: 0,
                    Type: "TRIGGER",
                    EntityId: 17,
                    EntityName: "Alice",
                    EffectCardId: "BG_TEST_CARD",
                    Target: "[name=Alice id=17]",
                    SubOption: null,
                    TriggerKeyword: "DEATHRATTLE"),
            },
            new RecordedEvent
            {
                Type = RecordedEventType.UnknownPower,
                Timestamp = Timestamp.AddMilliseconds(6),
                Content = @"source=C:\Users\private\capture.log token=not-retained",
            },
            RecordedEvent.CreateMatchEnded(Timestamp.AddMilliseconds(7)),
        ],
        [new ReplayCheckpoint("vs Alice", 7)]);

    private static FixtureManifest CreateManifest() => new()
    {
        SchemaVersion = FixtureManifest.CurrentSchemaVersion,
        Name = "fixture",
        SourceType = FixtureSourceTypes.Synthetic,
        IceCrowFormatVersion = RecordedMatch.CurrentFormatVersion,
        Reason = "test",
        InputType = FixtureInputTypes.NormalizedRecording,
        InputFile = FixtureImporter.RecordingFileName,
        ExpectedCheckpoints =
        [
            new FixtureCheckpointExpectation
            {
                Name = "start",
                EventIndex = 0,
                State = new FixtureStateExpectation { IsActive = true },
            },
        ],
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"icecrow-fixture-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
