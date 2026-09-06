using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IceCrow.App.Runtime;

/// <summary>
/// Explicit feature composition. The headless default keeps Power.log
/// tracking and profile sync on and never constructs the overlay; the Debug
/// build defaults the overlay on so the developer window keeps its preview.
/// </summary>
internal sealed record IceCrowRuntimeOptions(
    bool OverlayEnabled,
    bool ProfileSyncEnabled,
    Uri HearthPulseOrigin)
{
    public const string SettingsFileName = "settings.json";
    public const int MaximumSettingsBytes = 4 * 1024;
    public static readonly Uri DefaultHearthPulseOrigin = new("https://hearthpulse.net");

#if DEBUG
    public const bool DefaultOverlayEnabled = true;
#else
    public const bool DefaultOverlayEnabled = false;
#endif

    public static IceCrowRuntimeOptions Headless { get; } = new(false, true, DefaultHearthPulseOrigin);

    public static IceCrowRuntimeOptions Default { get; } = new(DefaultOverlayEnabled, true, DefaultHearthPulseOrigin);

    /// <summary>
    /// Reads the optional local settings file. Missing, oversized, or
    /// malformed settings fall back to the defaults so a bad file can never
    /// disable tracking.
    /// </summary>
    public static IceCrowRuntimeOptions Load(string localDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        var path = Path.Combine(localDataDirectory, SettingsFileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumSettingsBytes)
            {
                return Default;
            }

            var settings = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllBytes(path), SettingsJson.Options);
            if (settings is null)
            {
                return Default;
            }

            var origin = Uri.TryCreate(settings.HearthPulseOrigin, UriKind.Absolute, out var parsed) &&
                         parsed.Scheme == Uri.UriSchemeHttps
                ? parsed
                : DefaultHearthPulseOrigin;
            return new IceCrowRuntimeOptions(
                settings.OverlayEnabled ?? DefaultOverlayEnabled,
                settings.ProfileSyncEnabled ?? true,
                origin);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    private sealed record SettingsFile(
        bool? OverlayEnabled,
        bool? ProfileSyncEnabled,
        string? HearthPulseOrigin);

    private static class SettingsJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            MaxDepth = 4,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
    }
}
