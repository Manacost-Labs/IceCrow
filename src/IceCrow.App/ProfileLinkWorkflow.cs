using IceCrow.App.Runtime;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.App;

internal enum ProfileLinkStage
{
    Starting,
    WaitingForApproval,
    Linked,
    Denied,
    Expired,
    Cancelled,
    Unavailable,
}

/// <summary>Secret-free state that can safely be rendered by the product window.</summary>
internal sealed record ProfileLinkUpdate(
    ProfileLinkStage Stage,
    string? UserCode = null,
    Uri? VerificationUri = null,
    DateTimeOffset? ExpiresAt = null);

internal interface IProfileLinkGateway
{
    Task<DeviceLinkStart?> StartAsync(CancellationToken cancellationToken);

    Task<DeviceLinkPoll> PollAsync(DeviceLinkStart start, CancellationToken cancellationToken);

    Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken);
}

internal sealed class ProfileSyncLinkGateway(ProfileSyncRuntime runtime) : IProfileLinkGateway
{
    public Task<DeviceLinkStart?> StartAsync(CancellationToken cancellationToken) =>
        runtime.Authorization.StartAsync(cancellationToken);

    public Task<DeviceLinkPoll> PollAsync(DeviceLinkStart start, CancellationToken cancellationToken) =>
        runtime.Authorization.PollAsync(start, cancellationToken);

    public Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken) =>
        runtime.SaveCredentialAsync(credential, cancellationToken);
}

/// <summary>
/// Runs the OAuth device flow without WPF, HWND, or access-token exposure.
/// The caller owns browser presentation and may cancel whenever its window closes.
/// </summary>
internal sealed class ProfileLinkWorkflow(
    IProfileLinkGateway gateway,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task<ProfileLinkStage> RunAsync(
        Action<ProfileLinkUpdate> onUpdate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        onUpdate(new ProfileLinkUpdate(ProfileLinkStage.Starting));
        var start = await gateway.StartAsync(cancellationToken).ConfigureAwait(false);
        if (start is null || !TryGetSafeVerificationUri(start, out var verificationUri))
        {
            onUpdate(new ProfileLinkUpdate(ProfileLinkStage.Unavailable));
            return ProfileLinkStage.Unavailable;
        }

        onUpdate(new ProfileLinkUpdate(
            ProfileLinkStage.WaitingForApproval,
            start.UserCode,
            verificationUri,
            start.ExpiresAt));

        var interval = start.Interval;
        while (_time.GetUtcNow() < start.ExpiresAt)
        {
            await _delay(interval, cancellationToken).ConfigureAwait(false);
            var poll = await gateway.PollAsync(start, cancellationToken).ConfigureAwait(false);
            interval = poll.NextInterval;
            if (poll.Outcome == DeviceLinkOutcome.Linked && poll.Credential is not null)
            {
                await gateway.SaveAsync(poll.Credential, cancellationToken).ConfigureAwait(false);
                onUpdate(new ProfileLinkUpdate(ProfileLinkStage.Linked));
                return ProfileLinkStage.Linked;
            }

            if (poll.Outcome is DeviceLinkOutcome.Denied or DeviceLinkOutcome.Expired)
            {
                var stage = poll.Outcome == DeviceLinkOutcome.Denied
                    ? ProfileLinkStage.Denied
                    : ProfileLinkStage.Expired;
                onUpdate(new ProfileLinkUpdate(stage));
                return stage;
            }
        }

        onUpdate(new ProfileLinkUpdate(ProfileLinkStage.Expired));
        return ProfileLinkStage.Expired;
    }

    private static bool TryGetSafeVerificationUri(DeviceLinkStart start, out Uri verificationUri)
    {
        var target = start.VerificationUriComplete ?? start.VerificationUri;
        if (Uri.TryCreate(target, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
        {
            verificationUri = parsed;
            return true;
        }

        verificationUri = null!;
        return false;
    }
}
