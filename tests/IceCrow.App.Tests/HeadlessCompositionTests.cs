using System.IO;
using System.Windows.Threading;
using IceCrow.App.Runtime;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Records;

namespace IceCrow.App.Tests;

/// <summary>
/// The Release product is headless: tracking and profile sync must compose
/// and work while the overlay is absent, and the overlay assembly must not
/// be loaded just by constructing the runtime.
/// </summary>
public sealed class HeadlessCompositionTests : IDisposable
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "icecrow-headless-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task HeadlessRuntimeNeverComposesOrLoadsTheOverlay()
    {
        var overlayLoadedBefore = IsOverlayAssemblyLoaded();

        await using var runtime = CreateRuntime(IceCrowRuntimeOptions.Headless);

        Assert.False(runtime.OverlayComposed);
        Assert.Null(runtime.OverlayDiagnostics);
        Assert.True(runtime.ProfileSyncComposed);
        Assert.Equal(overlayLoadedBefore, IsOverlayAssemblyLoaded());
    }

    [Fact]
    public async Task ProfileSyncPersistsRecordsToTheOutboxWithoutAnOverlay()
    {
        await using var runtime = CreateRuntime(IceCrowRuntimeOptions.Headless);
        var profileSync = Assert.IsType<ProfileSyncRuntime>(runtime.ProfileSync);
        runtime.Start();

        var queued = profileSync.TryQueue(ProfileEvent.Create(
            ProfileEventType.BattlegroundsMatch,
            Timestamp,
            new BattlegroundsMatchRecord(
                Guid.CreateVersion7(),
                BattlegroundsMode.Solo,
                null,
                null,
                Certainty.Unknown,
                "TB_BaconShop_HERO_41",
                4,
                Certainty.Exact,
                Timestamp,
                Timestamp.AddMinutes(15),
                900,
                12,
                null,
                224857)));

        Assert.True(queued);
        using var outbox = new ProfileOutbox(Path.Combine(_directory, "profile", "outbox.json"));
        var pending = await WaitForOutboxAsync(outbox);
        Assert.Equal(1, pending);
        Assert.Equal(ProfileSyncPhase.NotLinked, profileSync.Status.Phase);
    }

    [Fact]
    public void SettingsFileControlsCompositionAndFallsBackSafely()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, IceCrowRuntimeOptions.SettingsFileName);

        Assert.Equal(IceCrowRuntimeOptions.Default, IceCrowRuntimeOptions.Load(_directory));

        File.WriteAllText(path, "{\"overlayEnabled\": false, \"profileSyncEnabled\": false, \"hearthPulseOrigin\": \"http://insecure.example\"}");
        var loaded = IceCrowRuntimeOptions.Load(_directory);
        Assert.False(loaded.OverlayEnabled);
        Assert.False(loaded.ProfileSyncEnabled);
        Assert.Equal(IceCrowRuntimeOptions.DefaultHearthPulseOrigin, loaded.HearthPulseOrigin);

        File.WriteAllText(path, "{ not json");
        Assert.Equal(IceCrowRuntimeOptions.Default, IceCrowRuntimeOptions.Load(_directory));
    }

    [Fact]
    public async Task ProfileSyncCanBeDisabledIndependentlyOfTracking()
    {
        await using var runtime = CreateRuntime(IceCrowRuntimeOptions.Headless with { ProfileSyncEnabled = false });

        Assert.False(runtime.ProfileSyncComposed);
        Assert.Null(runtime.ProfileSync);
        Assert.False(runtime.OverlayComposed);
    }

    private IceCrowRuntime CreateRuntime(IceCrowRuntimeOptions options) => new(
        _directory,
        Dispatcher.CurrentDispatcher,
        options,
        static _ => { },
        static _ => { },
        static (_, _, _) => { },
        static _ => { },
        static _ => { },
        static _ => { },
        static _ => { },
        "0.0.0-test");

    private static async Task<int> WaitForOutboxAsync(ProfileOutbox outbox)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var count = await outbox.CountAsync();
            if (count > 0)
            {
                return count;
            }

            await Task.Delay(50);
        }

        return await outbox.CountAsync();
    }

    private static bool IsOverlayAssemblyLoaded() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
            string.Equals(assembly.GetName().Name, "IceCrow.Overlay", StringComparison.Ordinal));
}
