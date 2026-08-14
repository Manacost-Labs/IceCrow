using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IceCrow.Hearthstone.Data;

namespace IceCrow.Infrastructure.ManacostApi;

internal static class SnapshotCodec
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string ComputeContentHash(
        IReadOnlyList<CardDefinition> cards,
        IReadOnlyList<BattlegroundsHeroDefinition> heroes,
        string dataVersion,
        string? hearthstoneBuild)
    {
        var content = new SnapshotContent(dataVersion, hearthstoneBuild, cards, heroes);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content, JsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal sealed record SnapshotContent(
        string DataVersion,
        string? HearthstoneBuild,
        IReadOnlyList<CardDefinition> Cards,
        IReadOnlyList<BattlegroundsHeroDefinition> Heroes);
}
