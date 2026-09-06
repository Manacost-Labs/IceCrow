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
            Encoding.UTF8.GetByteCount(profileEvent.Payload.GetRawText()) > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The profile event is outside the sync contract limits.");
        }
    }
}
