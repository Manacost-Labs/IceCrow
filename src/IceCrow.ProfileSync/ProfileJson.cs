using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.ProfileSync;

/// <summary>Single serializer configuration for records, outbox, and wire envelopes.</summary>
public static class ProfileJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}
