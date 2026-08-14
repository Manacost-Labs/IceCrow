using System.Windows;
using System.Windows.Controls;
using IceCrow.Overlay;
using IceCrow.Overlay.Controls;
using IceCrow.Presentation;

namespace IceCrow.App.DesignPreview;

/// <summary>
/// Developer-only gallery of every IceCrow component and its states.
/// </summary>
/// <remarks>
/// This window exists so visual iteration does not require a running
/// Hearthstone client. It is excluded from Release builds by the project file.
/// </remarks>
public partial class DesignPreviewWindow : Window
{
    private readonly OverlayRenderDiagnostics _diagnostics = new();
    private OverlayRenderingSettings _renderingSettings = OverlayRenderingSettings.Default;
    private OverlayLayoutMode _layoutMode = OverlayLayoutMode.Regular;

    public DesignPreviewWindow()
    {
        InitializeComponent();
        OverlayVisuals.SetImageCache(this, new OverlayImageCache(_diagnostics));
        BuildGalleries();
        ApplyRenderingSettings(_renderingSettings);
        ApplyLayoutMode(_layoutMode);
    }

    private void BuildGalleries()
    {
        foreach (var (opponent, caption) in CreateRowStates())
        {
            RowGallery.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 12, 0),
                Children =
                {
                    new OpponentRow
                    {
                        ViewState = opponent,
                        IsSelected = caption == "Selected",
                    },
                    CreateCaption(caption),
                },
            });
        }

        TileGallery.ItemsSource = CreateSampleBoard();

        DetailGallery.Children.Add(CreateDetailPanel("Seen recently", CreateSeenOpponent()));
        DetailGallery.Children.Add(CreateDetailPanel("Never fought", CreateNotFoughtOpponent()));
    }

    private static IEnumerable<(OpponentOverlayViewState Opponent, string Caption)> CreateRowStates() =>
    [
        (CreateSeenOpponent(), "Normal"),
        (CreateSeenOpponent(), "Selected"),
        (CreateStaleOpponent(), "Stale"),
        (CreateNotFoughtOpponent(), "Not fought"),
    ];

    private static OpponentOverlayViewState CreateSeenOpponent() => new(
        playerId: 2,
        heroName: "Reno Jackson",
        heroCardId: "TB_BaconShop_HERO_41",
        tavernTier: 5,
        health: 28,
        armor: 0,
        presence: OpponentPresence.Seen,
        lastSeenTurn: 8,
        turnsAgo: 2,
        triples: 2,
        progressionRows: ["T3·5", "T4·7", "T5·9"],
        board: CreateSampleBoard());

    private static OpponentOverlayViewState CreateStaleOpponent() => new(
        playerId: 3,
        heroName: "Sire Denathrius",
        heroCardId: null,
        tavernTier: 3,
        health: 14,
        armor: 5,
        presence: OpponentPresence.Seen,
        lastSeenTurn: 4,
        turnsAgo: 6,
        triples: 0,
        progressionRows: ["T2·3", "T3·6"],
        board: [MinionTileViewState.Create(1, "TILE_1", "Alleycat", 4, 5, 1, null)]);

    private static OpponentOverlayViewState CreateNotFoughtOpponent() => new(
        playerId: 4,
        heroName: "Player 4",
        heroCardId: null,
        tavernTier: 0,
        health: 30,
        armor: 0,
        presence: OpponentPresence.NotFought,
        lastSeenTurn: null,
        turnsAgo: null,
        triples: 0,
        progressionRows: [],
        board: []);

    private static IReadOnlyList<MinionTileViewState> CreateSampleBoard() =>
    [
        MinionTileViewState.Create(1, "TILE_1", "Bristleback Knight", 24, 31, 5, null),
        MinionTileViewState.Create(2, "TILE_2", "Cave Hydra", 18, 22, 4, null),
        MinionTileViewState.Create(3, "TILE_3", "Alleycat", 9, 9, 1, null),
        MinionTileViewState.Create(4, "TILE_4", "Sr. Tomb Diver", 12, 14, 3, null),
        MinionTileViewState.Create(5, "TILE_5", "Baron Rivendare", 6, 8, 5, null),
        MinionTileViewState.Create(6, "TILE_6", "Murloc Tidehunter", 4, 4, 1, null),
        MinionTileViewState.Create(7, "TILE_7", "Kalecgos", 11, 13, 6, null),
    ];

    private static TextBlock CreateCaption(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 6, 0, 0),
        Style = (Style)Application.Current.Resources["IceCrow.Text.Caption"],
    };

    private IcePanel CreateDetailPanel(string header, OpponentOverlayViewState opponent) => new()
    {
        Header = header,
        IsSignature = opponent.RequiresAttention,
        HasElevation = _renderingSettings.AllowPanelShadow,
        Width = OverlayLayout.DetailPanelWidth(_layoutMode),
        Margin = new Thickness(0, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Content = opponent,
        ContentTemplate = (DataTemplate)Application.Current.Resources["IceCrow.Template.OpponentDetail"],
    };

    private void OnTogglePerformanceMode(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        ApplyRenderingSettings(_renderingSettings.PerformanceMode
            ? OverlayRenderingSettings.Default
            : OverlayRenderingSettings.Performance);
    }

    private void OnToggleLayoutMode(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        ApplyLayoutMode(_layoutMode == OverlayLayoutMode.Regular
            ? OverlayLayoutMode.Compact
            : OverlayLayoutMode.Regular);
    }

    private void OnPulseSignature(object sender, RoutedEventArgs eventArgs)
    {
        _ = sender;
        _ = eventArgs;
        SignaturePanel.PulseSignature(_diagnostics);
    }

    private void ApplyRenderingSettings(OverlayRenderingSettings settings)
    {
        _renderingSettings = settings;
        OverlayVisuals.SetEffectsEnabled(this, !settings.PerformanceMode);
        ElevatedPanel.HasElevation = settings.AllowPanelShadow;
        foreach (var panel in DetailGallery.Children.OfType<IcePanel>())
        {
            panel.HasElevation = settings.AllowPanelShadow;
        }

        PerformanceModeButton.Content = settings.PerformanceMode
            ? "Performance mode: on"
            : "Performance mode: off";
    }

    private void ApplyLayoutMode(OverlayLayoutMode mode)
    {
        _layoutMode = mode;
        OverlayVisuals.SetLayoutMode(this, mode);
        foreach (var panel in DetailGallery.Children.OfType<IcePanel>())
        {
            panel.Width = OverlayLayout.DetailPanelWidth(mode);
        }

        LayoutModeButton.Content = $"Layout: {mode}";
    }
}
