using System.Diagnostics;
using System.Windows;
using IceCrow.App.Runtime;

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
        var workflow = new ProfileLinkWorkflow(new ProfileSyncLinkGateway(profileSync));
        var dispatcher = Application.Current?.Dispatcher;
        var stage = await workflow.RunAsync(update =>
        {
            if (update.Stage != ProfileLinkStage.WaitingForApproval || update.VerificationUri is null)
            {
                return;
            }

            void ShowApprovalInstructions()
            {
                OpenVerificationPage(update.VerificationUri);
                MessageBox.Show(
                    $"Введите код {update.UserCode} на странице HearthPulse. IceCrow продолжит ждать подтверждения.",
                    "Подключение HearthPulse",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            if (dispatcher is null || dispatcher.CheckAccess())
            {
                ShowApprovalInstructions();
            }
            else
            {
                dispatcher.Invoke(ShowApprovalInstructions);
            }
        }, cancellationToken).ConfigureAwait(true);

        if (stage == ProfileLinkStage.Linked)
        {
            MessageBox.Show("IceCrow подключён к HearthPulse.", "IceCrow");
            return true;
        }

        MessageBox.Show(
            stage == ProfileLinkStage.Unavailable
                ? "HearthPulse сейчас недоступен. Попробуйте позже."
                : "Запрос не был подтверждён или срок кода истёк.",
            "IceCrow",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public static async Task UnlinkAsync(ProfileSyncRuntime profileSync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileSync);
        await profileSync.UnlinkAsync(cancellationToken).ConfigureAwait(true);
        MessageBox.Show("IceCrow was unlinked from HearthPulse.", "IceCrow");
    }

    internal static bool OpenVerificationPage(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not open the browser: {exception.Message}");
            return false;
        }
    }
}
