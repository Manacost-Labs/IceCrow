using IceCrow.App.History;
using IceCrow.ProfileSync;

namespace IceCrow.App.Tests;

public sealed class HearthPulseAccountViewStateTests
{
    [Fact]
    public void NotLinkedOffersConnectionWithoutHidingOfflineHistory()
    {
        var state = HearthPulseAccountViewState.Create(ProfileSyncStatus.Initial, null);

        Assert.Equal("Профиль не подключён", state.Title);
        Assert.Equal("Подключить HearthPulse", state.ActionText);
        Assert.True(state.IsActionEnabled);
        Assert.False(state.IsDisconnectAction);
        Assert.False(state.ShowCode);
        Assert.Contains("локально", state.Description, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void WaitingForApprovalShowsOnlyThePublicCodeAndCancelAction()
    {
        var expires = DateTimeOffset.Now.AddMinutes(5);
        var update = new ProfileLinkUpdate(
            ProfileLinkStage.WaitingForApproval,
            "ABCD-EFGH",
            new Uri("https://hearthpulse.test/connect/"),
            expires);

        var state = HearthPulseAccountViewState.Create(ProfileSyncStatus.Initial, update);

        Assert.Equal("Подтвердите вход", state.Title);
        Assert.Equal("ABCD-EFGH", state.UserCode);
        Assert.True(state.ShowCode);
        Assert.True(state.ShowCancel);
        Assert.False(state.IsActionEnabled);
        Assert.Equal("https://hearthpulse.test/connect/", state.VerificationUri?.AbsoluteUri);
    }

    [Fact]
    public void LinkedStateOffersDisconnectAndReportsTheBoundedQueue()
    {
        var status = new ProfileSyncStatus(ProfileSyncPhase.Idle, 3, 12, 0, 0, 0, null, null);

        var state = HearthPulseAccountViewState.Create(status, null);

        Assert.Equal("Профиль подключён", state.Title);
        Assert.Equal("Отключить HearthPulse", state.ActionText);
        Assert.True(state.IsDisconnectAction);
        Assert.Contains("3", state.Description, StringComparison.Ordinal);
        Assert.False(state.ShowCode);
    }
}
