# Overlay boundary

This project renders immutable presentation view states with WPF and owns the
native interaction fail-safe. It must never read `Power.log`, query the entity
store, decode deckstrings, run Battlegrounds analysis, or perform HTTP.

- `Design/` holds the design system: `Colors.xaml` is the only file allowed to
  contain colour literals; everything else consumes semantic brushes. Merge
  `Design/IceCrowTheme.xaml`, not the individual dictionaries.
- `Themes/Generic.xaml` resolves the default styles for the controls in
  `Controls/`. A new control needs `DefaultStyleKeyProperty.OverrideMetadata`
  and a keyed style in `Design/Components.xaml`.
- Performance Mode, responsive density, and the image cache reach controls
  through the inherited attached properties in `Controls/OverlayVisuals.cs`.
  Do not add a singleton or an event bus for them.
- `Controls/OverlayAnimations.cs` is the complete animation vocabulary. Adding
  an animation means adding it there, keeping it finite, and keeping it inside
  the motion budget.
- Sizes shared with layout logic live in `IceCrow.Presentation.OverlayLayout`
  and are referenced from XAML with `x:Static`.

See `docs/design-system.md` and `docs/ui-performance-rules.md`. The mechanical
rules are enforced by `IceCrow.Architecture.Tests.UiPerformanceRuleTests`.
