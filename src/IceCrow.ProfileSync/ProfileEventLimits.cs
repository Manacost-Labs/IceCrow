using System.Collections.Frozen;
using System.Text.Json;
using IceCrow.ProfileSync.Records;

namespace IceCrow.ProfileSync;

/// <summary>
/// Structural bounds enforced on every serialized payload, independent of
/// which factory produced it: list lengths and card-id lengths from
/// <see cref="ProfileRecordLimits"/>. The server applies the same bounds.
/// </summary>
public static class ProfileEventLimits
{
    private static readonly FrozenDictionary<string, (string Path, int Maximum)[]> ListLimits =
        new Dictionary<string, (string, int)[]>(StringComparer.Ordinal)
        {
            [ProfileEventType.ConstructedMatch] =
            [
                ("playerMulligan.initial", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.kept", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.replaced", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.after", ProfileRecordLimits.MaximumMulliganCards),
                ("opponentDeck.observedCards", ProfileRecordLimits.MaximumObservedOpponentCards),
            ],
            [ProfileEventType.ArenaMatch] =
            [
                ("playerMulligan.initial", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.kept", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.replaced", ProfileRecordLimits.MaximumMulliganCards),
                ("playerMulligan.after", ProfileRecordLimits.MaximumMulliganCards),
            ],
            [ProfileEventType.ArenaDraftPick] = [("offeredCardIds", ProfileRecordLimits.MaximumArenaOffers)],
            [ProfileEventType.ArenaRun] = [("finalDeckCardIds", ProfileRecordLimits.MaximumArenaPicks)],
            [ProfileEventType.BattlegroundsMatch] = [("finalBoard.minions", ProfileRecordLimits.MaximumBoardMinions)],
            [ProfileEventType.CollectionSnapshot] = [("cards", ProfileRecordLimits.MaximumCollectionCards)],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool IsWithinLimits(string type, JsonElement payload)
    {
        if (!ListLimits.TryGetValue(type, out var limits))
        {
            return false;
        }

        foreach (var (path, maximum) in limits)
        {
            if (!TryResolve(payload, path, out var list))
            {
                continue;
            }

            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > maximum || !ItemsAreBounded(list))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemsAreBounded(JsonElement list)
    {
        foreach (var item in list.EnumerateArray())
        {
            var cardId = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("cardId", out var property)
                    ? property.GetString()
                    : null;
            if (cardId is { Length: > ProfileRecordLimits.MaximumCardIdLength })
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolve(JsonElement payload, string path, out JsonElement element)
    {
        element = payload;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return element.ValueKind != JsonValueKind.Null;
    }
}
