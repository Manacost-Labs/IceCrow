using System.Text;
using System.Text.Json;

namespace IceCrow.ProfileSync;

/// <summary>
/// One idempotent profile event. The payload is serialized exactly once, at
/// creation, so the outbox and the wire envelope carry the same bytes.
/// </summary>
public sealed record ProfileEvent(
    Guid EventId,
    string Type,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    JsonElement Payload)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumPayloadBytes = 512 * 1024;

    /// <summary>
    /// A full collection is thousands of cards; it is state (latest-only in
    /// the outbox) so it gets its own larger bound, mirrored by the server.
    /// </summary>
    public const int MaximumCollectionPayloadBytes = 4 * 1024 * 1024;

    public static int MaximumPayloadBytesFor(string type) =>
        ProfileEventType.IsLatestOnly(type) ? MaximumCollectionPayloadBytes : MaximumPayloadBytes;

    public static ProfileEvent Create<TPayload>(
        string type,
        DateTimeOffset occurredAt,
        TPayload payload,
        Guid? eventId = null)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!ProfileEventType.IsKnown(type))
        {
            throw new ArgumentException($"Unknown profile event type '{type}'.", nameof(type));
        }

        var element = JsonSerializer.SerializeToElement(payload, ProfileJson.Options);
        var created = new ProfileEvent(
            eventId ?? Guid.CreateVersion7(),
            type,
            CurrentSchemaVersion,
            occurredAt,
            element);
        Validate(created);
        return created;
    }

    public static void Validate(ProfileEvent profileEvent)
    {
        ArgumentNullException.ThrowIfNull(profileEvent);
        if (profileEvent.EventId == Guid.Empty ||
            !ProfileEventType.IsKnown(profileEvent.Type) ||
            profileEvent.SchemaVersion != CurrentSchemaVersion ||
            profileEvent.Payload.ValueKind != JsonValueKind.Object ||
            Encoding.UTF8.GetByteCount(profileEvent.Payload.GetRawText()) > MaximumPayloadBytesFor(profileEvent.Type) ||
            !ProfileEventLimits.IsWithinLimits(profileEvent.Type, profileEvent.Payload))
        {
            throw new InvalidDataException("The profile event is outside the sync contract limits.");
        }
    }
}
