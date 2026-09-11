using System.Security.Cryptography;
using System.Text;

namespace IceCrow.ProfileSync;

/// <summary>
/// Creates restart-stable local identities for completed matches. Hearthstone
/// does not expose an authoritative game handle in Power.log, so the identity
/// is derived only from the normalized match type and its observed time span.
/// </summary>
public static class ProfileMatchIdentity
{
    private const string IdentityVersion = "IceCrow.ProfileMatchIdentity.v1";

    public static Guid CreateMatchId(
        string eventType,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt) =>
        Create(eventType, "match", startedAt, endedAt);

    public static Guid CreateEventId(
        string eventType,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt) =>
        Create(eventType, "event", startedAt, endedAt);

    internal static bool TryGetKey(ProfileEvent profileEvent, out ProfileMatchKey key)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        if (!IsMatchType(profileEvent.Type) ||
            !profileEvent.Payload.TryGetProperty("startedAt", out var started) ||
            !profileEvent.Payload.TryGetProperty("endedAt", out var ended) ||
            !started.TryGetDateTimeOffset(out var startedAt) ||
            !ended.TryGetDateTimeOffset(out var endedAt))
        {
            key = default;
            return false;
        }

        key = new ProfileMatchKey(
            profileEvent.Type,
            startedAt.UtcDateTime.Ticks,
            endedAt.UtcDateTime.Ticks);
        return true;
    }

    internal static int CollapseDuplicateMatches(List<ProfileEvent> events)
    {
        var keys = new HashSet<ProfileMatchKey>();
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < events.Count; readIndex++)
        {
            var profileEvent = events[readIndex];
            if (TryGetKey(profileEvent, out var key) && !keys.Add(key))
            {
                continue;
            }

            if (writeIndex != readIndex)
            {
                events[writeIndex] = profileEvent;
            }

            writeIndex++;
        }

        var removed = events.Count - writeIndex;
        if (removed > 0)
        {
            events.RemoveRange(writeIndex, removed);
        }

        return removed;
    }

    private static Guid Create(
        string eventType,
        string purpose,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        if (!IsMatchType(eventType))
        {
            throw new ArgumentException("A stable identity can only be created for a match event.", nameof(eventType));
        }

        var canonical = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{IdentityVersion}\n{purpose}\n{eventType}\n{startedAt.UtcDateTime.Ticks}\n{endedAt.UtcDateTime.Ticks}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(canonical), hash);

        // RFC 9562 UUIDv8: a custom deterministic UUID with RFC variant bits.
        hash[6] = (byte)((hash[6] & 0x0f) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    private static bool IsMatchType(string eventType) =>
        string.Equals(eventType, ProfileEventType.ConstructedMatch, StringComparison.Ordinal) ||
        string.Equals(eventType, ProfileEventType.ArenaMatch, StringComparison.Ordinal) ||
        string.Equals(eventType, ProfileEventType.BattlegroundsMatch, StringComparison.Ordinal);
}

internal readonly record struct ProfileMatchKey(
    string EventType,
    long StartedAtUtcTicks,
    long EndedAtUtcTicks);
