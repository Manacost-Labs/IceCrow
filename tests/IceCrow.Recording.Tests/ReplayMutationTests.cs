using System.Text;
using System.Text.Json.Nodes;
using IceCrow.Recording.Tests.Fixtures;

namespace IceCrow.Recording.Tests;

public sealed class ReplayMutationTests
{
    [Fact]
    public async Task DeterministicFixtureMutationsFailClosed()
    {
        var validJson = await SerializeAsync(DeterministicMatchFixture.Create());
        var mutations = new List<(string Name, string Json)>
        {
            ("event-order", Mutate(validJson, root =>
            {
                var events = root["events"]!.AsArray();
                var first = events[0]!.DeepClone();
                events.RemoveAt(0);
                events.Insert(1, first);
            })),
            ("missing-required-field", Mutate(validJson, root =>
            {
                FindEvent(root, "rawTagChanged").Remove("value");
            })),
            ("unknown-discriminator", Mutate(validJson, root =>
            {
                FindEvent(root, "rawTagChanged")["type"] = "futureRuntimeType";
            })),
            ("huge-string", Mutate(validJson, root =>
            {
                FindEvent(root, "rawTagChanged")["entityName"] =
                    new string('x', RecordingSerializer.MaximumStringCharacters + 1);
            })),
            ("numeric-enum", Mutate(validJson, root =>
            {
                FindEvent(root, "rawTagChanged")["type"] = 999;
            })),
            ("invalid-checkpoint", Mutate(validJson, root =>
            {
                var eventCount = root["events"]!.AsArray().Count;
                root["checkpoints"]!.AsArray()[0]!["eventIndex"] = eventCount;
            })),
            ("type-metadata", Mutate(validJson, root =>
            {
                FindEvent(root, "rawTagChanged")["$type"] =
                    "System.Object, System.Private.CoreLib";
            })),
            ("truncated-json", validJson[..^Math.Min(16, validJson.Length)]),
        };

        foreach (var mutation in mutations)
        {
            var exception = await Record.ExceptionAsync(() => DeserializeAsync(mutation.Json));
            Assert.True(
                exception is InvalidDataException,
                $"Mutation '{mutation.Name}' was not rejected safely. Actual: {exception}");
        }
    }

    private static JsonObject FindEvent(JsonObject root, string type) =>
        root["events"]!
            .AsArray()
            .Select(static node => node!.AsObject())
            .First(node => string.Equals(
                node["type"]?.GetValue<string>(),
                type,
                StringComparison.Ordinal));

    private static string Mutate(string json, Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static async Task<string> SerializeAsync(RecordedMatch match)
    {
        await using var stream = new MemoryStream();
        await RecordingSerializer.SerializeAsync(stream, match);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<RecordedMatch> DeserializeAsync(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await RecordingSerializer.DeserializeAsync(stream);
    }
}
