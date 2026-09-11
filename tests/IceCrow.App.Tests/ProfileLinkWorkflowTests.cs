using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Transport;

namespace IceCrow.App.Tests;

public sealed class ProfileLinkWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunPersistsCredentialOnlyAfterBrowserApproval()
    {
        var credential = Credential();
        var gateway = new FakeGateway(
            Start("https://hearthpulse.test/connect/?user_code=ABCD-EFGH"),
            new DeviceLinkPoll(DeviceLinkOutcome.Pending, null, TimeSpan.Zero),
            new DeviceLinkPoll(DeviceLinkOutcome.Linked, credential, TimeSpan.Zero));
        var updates = new List<ProfileLinkUpdate>();
        var workflow = CreateWorkflow(gateway);

        var result = await workflow.RunAsync(updates.Add, CancellationToken.None);

        Assert.Equal(ProfileLinkStage.Linked, result);
        Assert.Same(credential, gateway.SavedCredential);
        Assert.Collection(
            updates,
            update => Assert.Equal(ProfileLinkStage.Starting, update.Stage),
            update =>
            {
                Assert.Equal(ProfileLinkStage.WaitingForApproval, update.Stage);
                Assert.Equal("ABCD-EFGH", update.UserCode);
                Assert.Equal("https://hearthpulse.test/connect/?user_code=ABCD-EFGH", update.VerificationUri?.AbsoluteUri);
            },
            update => Assert.Equal(ProfileLinkStage.Linked, update.Stage));
        Assert.DoesNotContain("access-secret", string.Join('|', updates), StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", string.Join('|', updates), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeviceLinkOutcome.Denied, "Denied")]
    [InlineData(DeviceLinkOutcome.Expired, "Expired")]
    public async Task RunStopsWithoutSavingWhenApprovalFails(DeviceLinkOutcome outcome, string expected)
    {
        var gateway = new FakeGateway(Start(), new DeviceLinkPoll(outcome, null, TimeSpan.Zero));
        var workflow = CreateWorkflow(gateway);

        var result = await workflow.RunAsync(static _ => { }, CancellationToken.None);

        Assert.Equal(expected, result.ToString());
        Assert.Null(gateway.SavedCredential);
        Assert.Equal(1, gateway.PollCount);
    }

    [Fact]
    public async Task RunRejectsAnInsecureVerificationPageBeforePolling()
    {
        var gateway = new FakeGateway(Start("http://hearthpulse.test/connect/"));
        var updates = new List<ProfileLinkUpdate>();

        var result = await CreateWorkflow(gateway).RunAsync(updates.Add, CancellationToken.None);

        Assert.Equal(ProfileLinkStage.Unavailable, result);
        Assert.Equal(0, gateway.PollCount);
        Assert.Null(gateway.SavedCredential);
        Assert.Equal(ProfileLinkStage.Unavailable, updates[^1].Stage);
    }

    [Theory]
    [InlineData("https://evil.test/connect/")]
    [InlineData("https://hearthpulse.test.evil.test/connect/")]
    [InlineData("https://hearthpulse.test@evil.test/connect/")]
    public async Task RunRejectsACrossOriginVerificationPageBeforePolling(string verificationUri)
    {
        var gateway = new FakeGateway(Start(verificationUri));

        var result = await CreateWorkflow(gateway).RunAsync(static _ => { }, CancellationToken.None);

        Assert.Equal(ProfileLinkStage.Unavailable, result);
        Assert.Equal(0, gateway.PollCount);
        Assert.Null(gateway.SavedCredential);
    }

    [Fact]
    public async Task RunPropagatesCancellationWithoutSaving()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new FakeGateway(Start());
        var workflow = new ProfileLinkWorkflow(
            gateway,
            new FixedTimeProvider(Now),
            (_, token) => Task.FromCanceled(token));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            workflow.RunAsync(static _ => { }, cancellation.Token));

        Assert.Equal(0, gateway.PollCount);
        Assert.Null(gateway.SavedCredential);
    }

    private static ProfileLinkWorkflow CreateWorkflow(FakeGateway gateway) =>
        new(gateway, new FixedTimeProvider(Now), static (_, _) => Task.CompletedTask);

    private static DeviceLinkStart Start(string verificationUri = "https://hearthpulse.test/connect/") =>
        new("device-secret", "ABCD-EFGH", verificationUri, verificationUri, Now.AddMinutes(5), TimeSpan.Zero);

    private static ProfileCredential Credential() =>
        new("https://hearthpulse.test", "access-secret", Now.AddMinutes(15), "refresh-secret", ["profile.read", "tracker.write"], Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeGateway(DeviceLinkStart? start, params DeviceLinkPoll[] polls) : IProfileLinkGateway
    {
        private readonly Queue<DeviceLinkPoll> _polls = new(polls);

        public int PollCount { get; private set; }

        public ProfileCredential? SavedCredential { get; private set; }

        public Uri ServerOrigin { get; } = new("https://hearthpulse.test");

        public Task<DeviceLinkStart?> StartAsync(CancellationToken cancellationToken) => Task.FromResult(start);

        public Task<DeviceLinkPoll> PollAsync(DeviceLinkStart linkStart, CancellationToken cancellationToken)
        {
            _ = linkStart;
            PollCount++;
            return Task.FromResult(_polls.Dequeue());
        }

        public Task SaveAsync(ProfileCredential credential, CancellationToken cancellationToken)
        {
            SavedCredential = credential;
            return Task.CompletedTask;
        }
    }
}
