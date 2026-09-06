using System.Diagnostics;
using System.Globalization;
using System.Windows;
using IceCrow.App.Runtime;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.App;

/// <summary>
/// One-time device linking driven from the command line
/// (<c>IceCrow.App.exe --link-hearthpulse</c> / <c>--unlink-hearthpulse</c>).
/// The user sees the short code in a plain message box, approves it on the
/// HearthPulse connect page in their browser, and IceCrow stores only the
/// revocable device credential. No overlay is involved.
/// </summary>
internal static class ProfileLinkCommand
{
    public const string LinkArgument = "--link-hearthpulse";
    public const string UnlinkArgument = "--unlink-hearthpulse";

    public static bool IsLinkRequest(IReadOnlyList<string> arguments) =>
        arguments.Contains(LinkArgument, StringComparer.OrdinalIgnoreCase);

    public static bool IsUnlinkRequest(IReadOnlyList<string> arguments) =>
        arguments.Contains(UnlinkArgument, StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> LinkAsync(ProfileSyncRuntime profileSync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileSync);
        var start = await profileSync.Authorization.StartAsync(cancellationToken).ConfigureAwait(true);
        if (start is null)
        {
            MessageBox.Show(
                "HearthPulse is not reachable right now. Try again later.",
                "IceCrow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        OpenVerificationPage(start);
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Open {start.VerificationUri} in your browser and enter the code:\n\n{start.UserCode}\n\nThe code expires at {start.ExpiresAt.ToLocalTime():t}. Keep this window open until the link completes.");
        MessageBox.Show(message, "Link IceCrow to HearthPulse", MessageBoxButton.OK, MessageBoxImage.Information);

        var interval = start.Interval;
        while (DateTimeOffset.UtcNow < start.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(true);
            var poll = await profileSync.Authorization.PollAsync(start, cancellationToken).ConfigureAwait(true);
            interval = poll.NextInterval;
            switch (poll.Outcome)
            {
                case DeviceLinkOutcome.Linked when poll.Credential is not null:
                    await profileSync.SaveCredentialAsync(poll.Credential, cancellationToken).ConfigureAwait(true);
                    MessageBox.Show("IceCrow is linked. Your matches will appear on your HearthPulse profile.", "IceCrow");
                    return true;
                case DeviceLinkOutcome.Expired:
                case DeviceLinkOutcome.Denied:
                    MessageBox.Show("The link request was not approved.", "IceCrow", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                case DeviceLinkOutcome.Pending:
                case DeviceLinkOutcome.Unavailable:
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported link outcome '{poll.Outcome}'.");
            }
        }

        MessageBox.Show("The link code expired before it was approved.", "IceCrow", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    public static async Task UnlinkAsync(ProfileSyncRuntime profileSync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileSync);
        await profileSync.UnlinkAsync(cancellationToken).ConfigureAwait(true);
        MessageBox.Show("IceCrow was unlinked from HearthPulse.", "IceCrow");
    }

    private static void OpenVerificationPage(DeviceLinkStart start)
    {
        // The complete URI carries the code; the browser is the user's own.
        var target = start.VerificationUriComplete ?? start.VerificationUri;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open the browser: {exception.Message}");
        }
    }
}
