using System.IO;
using System.Windows.Threading;
using IceCrow.App.Runtime;
using IceCrow.ProfileSync;
using IceCrow.ProfileSync.Collection;
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
        var outboxPath = Path.Combine(_directory, "profile", "outbox.json");
        var pending = await WaitForOutboxAsync(outboxPath);
        Assert.Equal(1, pending);
        Assert.Equal(ProfileSyncPhase.NotLinked, profileSync.Status.Phase);
    }

    [Fact]
    public async Task HeadlessProfileSyncAutoImportsAnExistingCollectionExportAtStartup()
    {
        var hdtData = Path.Combine(_directory, "hdt");
        var baseline = Path.Combine(
            hdtData,
            "HdtCollectionExporter",
            HdtCollectionExportLocator.SharedBaselineFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
        await File.WriteAllTextAsync(
            baseline,
            """
            {"exportedAt":"2026-09-11T12:00:00Z","version":3,"cards":[{"cardId":"COLLECTION_CARD","normal":2,"golden":0,"signature":0,"diamond":0}]}
            """);
        var locator = new HdtCollectionExportLocator(hdtData, Path.Combine(_directory, "documents"));
        await using var profileSync = new ProfileSyncRuntime(
            _directory,
            IceCrowRuntimeOptions.DefaultHearthPulseOrigin,
            static _ => { },
            "0.0.0-test",
            locator);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = profileSync.RunAsync(cancellation.Token);
        var outboxPath = Path.Combine(_directory, "profile", "outbox.json");

        Assert.Equal(1, await WaitForOutboxAsync(outboxPath));
        using var outbox = new ProfileOutbox(outboxPath);
        var pending = Assert.Single(await outbox.PeekBatchAsync(10));
        Assert.Equal(ProfileEventType.CollectionSnapshot, pending.Type);
        Assert.Equal("COLLECTION_CARD", pending.Payload.GetProperty("cards")[0].GetProperty("cardId").GetString());
        Assert.Equal(CollectionSyncOutcome.Enqueued, profileSync.CollectionStatus.LastOutcome);

        profileSync.Complete();
        cancellation.Cancel();
        await run;
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

    private static async Task<int> WaitForOutboxAsync(string outboxPath)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            using var outbox = new ProfileOutbox(outboxPath);
            var count = await outbox.CountAsync();
            if (count > 0)
            {
                return count;
            }

            await Task.Delay(50);
        }

        using var finalOutbox = new ProfileOutbox(outboxPath);
        return await finalOutbox.CountAsync();
    }

    private static bool IsOverlayAssemblyLoaded() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
            string.Equals(assembly.GetName().Name, "IceCrow.Overlay", StringComparison.Ordinal));
}
