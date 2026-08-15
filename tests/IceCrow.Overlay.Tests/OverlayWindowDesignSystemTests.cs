using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using IceCrow.Overlay.Controls;
using IceCrow.Presentation;

namespace IceCrow.Overlay.Tests;

/// <summary>
/// Smoke tests for the design system. They construct the real overlay window on
/// an STA thread, which is the only way to prove that every design token,
/// component template, and binding actually resolves at runtime.
/// </summary>
public sealed class OverlayWindowDesignSystemTests
{
    [Fact]
    public void OverlayWindowResolvesEveryDesignTokenAndComponentTemplate() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();

            host.Window.ApplyViewState(CreateViewState(triples: 1));
            host.Render();

            Assert.Single(host.FindVisualChildren<OpponentRow>());
            Assert.NotEmpty(host.FindVisualChildren<IceBadge>());
        });

    [Fact]
    public void OpponentDetailTemplateRendersTheBoardStatsAndProgression() =>
        StaTestRunner.Run(() =>
        {
            using var host = new TemplateHostFixture(CreateOpponent(2, triples: 2));

            Assert.Single(host.FindVisualChildren<MinionTile>());
            Assert.Equal(3, host.FindVisualChildren<IceStat>().Count);
            Assert.NotEmpty(host.FindVisualChildren<IceBadge>());
        });

    [Fact]
    public void OpponentDetailTemplateRendersTheSinceLastFightSection() =>
        StaTestRunner.Run(() =>
        {
            using var host = new TemplateHostFixture(CreateOpponent(
                2,
                triples: 2,
                changes: CreateChanges()));

            var textBlocks = host.FindVisualChildren<TextBlock>();
            Assert.Contains(textBlocks, text => text.Text == "Since last fight");
            Assert.Contains(textBlocks, text => text.Text == "Previous · Turn 7");
            Assert.Contains(textBlocks, text => text.Text == "18/22 → 24/31");
            Assert.Contains(textBlocks, text => text.Text == "+6/+9");
            Assert.Contains(textBlocks, text => text.Text == "Malchezaar");
        });

    [Fact]
    public void OpponentDetailTemplateHidesTheSectionBeforeASecondFight() =>
        StaTestRunner.Run(() =>
        {
            using var host = new TemplateHostFixture(CreateOpponent(2, triples: 2));

            Assert.DoesNotContain(
                host.FindVisualChildren<TextBlock>(),
                text => text.Text == "Since last fight" && text.IsVisible);
        });

    [Fact]
    public void LobbyRowShowsTheCompactChangeSummary() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();

            host.Window.ApplyViewState(new BattlegroundsOverlayViewState(
                true,
                [CreateOpponent(2, triples: 1, changes: CreateChanges())]));
            host.Render();

            Assert.Contains(
                host.FindVisualChildren<TextBlock>(),
                text => text.Text == "2 changes");
        });

    [Fact]
    public void ValueEqualViewStatesAreSkippedInsteadOfRebuildingTheTree() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();

            host.Window.ApplyViewState(CreateViewState(triples: 1));
            host.Render();
            var appliedAfterFirst = host.Window.Diagnostics.ViewStateUpdatesApplied;
            var rowsAfterFirst = host.Window.Diagnostics.OpponentRowsReplaced;

            host.Window.ApplyViewState(CreateViewState(triples: 1));

            Assert.Equal(appliedAfterFirst, host.Window.Diagnostics.ViewStateUpdatesApplied);
            Assert.Equal(rowsAfterFirst, host.Window.Diagnostics.OpponentRowsReplaced);
            Assert.Equal(1, host.Window.Diagnostics.ViewStateUpdatesSkipped);
        });

    [Fact]
    public void OnlyChangedLobbyRowsAreReplaced() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();

            host.Window.ApplyViewState(new BattlegroundsOverlayViewState(
                true,
                [CreateOpponent(2, triples: 1), CreateOpponent(3, triples: 0)]));
            host.Render();
            var rowsAfterFirst = host.Window.Diagnostics.OpponentRowsReplaced;

            host.Window.ApplyViewState(new BattlegroundsOverlayViewState(
                true,
                [CreateOpponent(2, triples: 1), CreateOpponent(3, triples: 1)]));

            Assert.Equal(rowsAfterFirst + 1, host.Window.Diagnostics.OpponentRowsReplaced);
        });

    [Fact]
    public void PerformanceModeClearsShadowsAndSuppressesAnimations() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();
            host.Render();

            host.Window.SetRenderingSettings(OverlayRenderingSettings.Default);
            host.Render();
            Assert.Contains(
                host.FindVisualChildren<Border>(),
                border => border.Effect is DropShadowEffect);

            host.Window.SetRenderingSettings(OverlayRenderingSettings.Performance);
            host.Render();
            Assert.DoesNotContain(
                host.FindVisualChildren<Border>(),
                border => border.Effect is not null);

            var animationsBefore = host.Window.Diagnostics.AnimationsStarted;
            host.Window.ApplyViewState(CreateViewState(triples: 1));
            host.Window.ApplyViewState(CreateViewState(triples: 2));
            Assert.Equal(animationsBefore, host.Window.Diagnostics.AnimationsStarted);
        });

    [Fact]
    public void ImportantEventPulseIsFiniteAndOnlyRunsOnARealChange() =>
        StaTestRunner.Run(() =>
        {
            using var host = new OverlayWindowFixture();
            host.Render();

            host.Window.ApplyViewState(CreateViewState(triples: 1));
            var animationsBefore = host.Window.Diagnostics.AnimationsStarted;

            host.Window.ApplyViewState(CreateViewState(triples: 2));

            Assert.Equal(animationsBefore + 1, host.Window.Diagnostics.AnimationsStarted);
        });

    private static BattlegroundsOverlayViewState CreateViewState(int triples) =>
        new(true, [CreateOpponent(2, triples)]);

    private static OpponentOverlayViewState CreateOpponent(
        int playerId,
        int triples,
        OpponentChangesViewState? changes = null) => new(
        playerId,
        heroName: "Reno Jackson",
        heroCardId: "TB_BaconShop_HERO_41",
        tavernTier: 5,
        health: 28,
        armor: 0,
        presence: OpponentPresence.Seen,
        lastSeenTurn: 8,
        turnsAgo: 2,
        triples: triples,
        progressionRows: ["T3·5", "T4·7"],
        board: [MinionTileViewState.Create(1, "MINION", "Alleycat", 3, 4, 1, null)],
        changes: changes);

    private static OpponentChangesViewState CreateChanges() => new(
        previousTurn: 7,
        currentTurn: 8,
        isMajorChange: false,
        rows:
        [
            MinionChangeViewState.Added("Malchezaar", 9, 9),
            MinionChangeViewState.Changed("Brann", 18, 22, 24, 31),
        ]);

    private sealed class OverlayWindowFixture : IDisposable
    {
        public OverlayWindowFixture()
        {
            // A real size is required for control templates and item containers
            // to be created. The window is parked off-screen for the test run.
            Window = new OverlayWindow
            {
                Left = -4000,
                Top = -4000,
                Width = 1920,
                Height = 1080,
            };
            Window.Show();
            Render();
        }

        public OverlayWindow Window { get; }

        public void Render() => VisualTreeSearch.Render(Window);

        public List<T> FindVisualChildren<T>()
            where T : DependencyObject =>
            VisualTreeSearch.FindChildren<T>(Window);

        public void Dispose() => Window.Close();
    }

    /// <summary>
    /// Hosts the shared Opponent Memory detail template outside the overlay so
    /// the same surface used by the developer design preview is covered too.
    /// </summary>
    private sealed class TemplateHostFixture : IDisposable
    {
        private readonly Window _window;

        public TemplateHostFixture(OpponentOverlayViewState opponent)
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/IceCrow.Overlay;component/Design/IceCrowTheme.xaml",
                    UriKind.Absolute),
            };
            var panel = new IcePanel
            {
                Width = 360,
                Header = "Detail",
                Content = opponent,
                ContentTemplate = (DataTemplate)theme["IceCrow.Template.OpponentDetail"],
            };
            _window = new Window
            {
                Left = -4000,
                Top = -4000,
                Width = 480,
                Height = 640,
                Content = panel,
            };
            _window.Resources.MergedDictionaries.Add(theme);
            _window.Show();
            VisualTreeSearch.Render(_window);
        }

        public List<T> FindVisualChildren<T>()
            where T : DependencyObject =>
            VisualTreeSearch.FindChildren<T>(_window);

        public void Dispose() => _window.Close();
    }
}
