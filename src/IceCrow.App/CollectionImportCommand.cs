using System.Windows;
using IceCrow.App.Runtime;
using IceCrow.ProfileSync.Collection;

namespace IceCrow.App;

/// <summary>
/// Explicit collection import for the headless application. A full export can
/// be selected with <c>--import-collection path</c>; <c>--refresh-collection</c>
/// retries the documented auto-discovery locations.
/// </summary>
internal static class CollectionImportCommand
{
    public const string ImportArgument = "--import-collection";
    public const string RefreshArgument = "--refresh-collection";

    public static bool IsRequest(IReadOnlyList<string> arguments) =>
        arguments.Contains(ImportArgument, StringComparer.OrdinalIgnoreCase) ||
        arguments.Contains(RefreshArgument, StringComparer.OrdinalIgnoreCase);

    public static async Task<CollectionSyncOutcome> ExecuteAsync(
        ProfileSyncRuntime profileSync,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileSync);
        ArgumentNullException.ThrowIfNull(arguments);
        var path = GetImportPath(arguments);
        var outcome = await profileSync.RefreshCollectionAsync(
            CollectionRefreshTrigger.Manual,
            path,
            cancellationToken).ConfigureAwait(true);

        var status = profileSync.CollectionStatus;
        var message = outcome switch
        {
            CollectionSyncOutcome.Enqueued or CollectionSyncOutcome.Replaced =>
                $"Collection imported: {status.CardCount} owned card entries are queued for HearthPulse.",
            CollectionSyncOutcome.Unchanged =>
                $"Collection is unchanged: {status.CardCount} owned card entries.",
            CollectionSyncOutcome.Unavailable =>
                "No valid complete collection export was available. Export JSON schema v3 with the Manacost HDT Collection Exporter and retry.",
            CollectionSyncOutcome.Rejected =>
                "The collection was read but rejected by the bounded profile queue.",
            _ => throw new InvalidOperationException($"Unsupported collection outcome '{outcome}'."),
        };
        MessageBox.Show(
            message,
            "IceCrow collection",
            MessageBoxButton.OK,
            outcome is CollectionSyncOutcome.Enqueued or CollectionSyncOutcome.Replaced or CollectionSyncOutcome.Unchanged
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
        return outcome;
    }

    private static string? GetImportPath(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], ImportArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new ArgumentException($"{ImportArgument} requires a JSON file path.", nameof(arguments));
            }

            return arguments[index + 1];
        }

        return null;
    }
}
