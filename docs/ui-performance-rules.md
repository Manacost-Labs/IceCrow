# UI performance rules

IceCrow runs next to Hearthstone on machines that are already busy. The overlay
budget is "almost nothing while idle", and these rules keep it there by
construction. `IceCrow.Architecture.Tests.UiPerformanceRuleTests` enforces the
mechanical ones over every `.cs` and `.xaml` file under `src/`.

## Effect policy

| Effect | Status | Notes |
| --- | --- | --- |
| `DropShadowEffect` | Allowed, once | Declared exactly once, in `Design/Components.xaml`, and applied only by `IcePanel.HasElevation` on a top-level floating panel. Cleared by Performance Mode. Never on rows, tiles, badges, or labels. Never stacked. |
| `BlurEffect` | Forbidden | Not in production overlay, not behind a flag. |
| `BitmapEffect` | Forbidden | Legacy software rendering path. |
| Animated gradients | Forbidden | |
| Particles | Forbidden | |
| Continuous storyboards | Forbidden | `BeginStoryboard`, `<Storyboard>`, and `RepeatBehavior` are rejected outright; animations are started imperatively and always terminate. |
| `CompositionTarget.Rendering` | Forbidden | The overlay has no per-frame loop. |
| `LayoutTransform` | Forbidden | Forces a layout pass; use `RenderTransform`. |

## Rendering rules

- **No per-frame loop.** Being visible is not a reason to do work. UI changes are
  driven by view-state changes, user interaction, and short finite animations.
- **Animate transforms and opacity only**, never `Width`, `Height`, or `Margin`
  in frequently updated controls.
- **Every animation terminates** and uses `FillBehavior.Stop`, so no control is
  retained through a timeline.
- **Interaction budget**: 140–180 ms normally, 240 ms for an important event,
  350 ms absolute ceiling.
- **Hover is a state change**, not an animation. Instant token swaps are cheaper
  and feel more responsive than colour fades on eight rows.

## View-state rules

- Presentation view states are immutable and have value equality.
- `ApplyViewState` drops a value-equal update and records it as skipped.
- Only lobby rows whose value changed are replaced; the rest keep their
  containers.
- Do not rebuild an entire `ItemsSource` because one item changed.
- Do not reach into item containers from the window; drive state through bindings
  (for example the pinned-row selection converter).

## Image rules

- Decode near display size using the shared `OverlayLayout.ArtDecodeSteps`
  (64/96/128/192), never at full resolution for a 46–58 px tile.
- Decode off the UI thread; a cache miss returns immediately and the tile keeps
  its placeholder. Rendering never waits on disk.
- Cache decoded, frozen bitmaps keyed by (path, decode width), bounded with FIFO
  eviction.
- Deduplicate in-flight decodes and cap concurrent decodes, so rapid hovering
  cannot fan out into unbounded tasks.
- The overlay only ever reads a local path that the composition root has already
  resolved. The overlay performs no HTTP.

## Resource dictionary rules

- One shallow chain: tokens, then components, merged through
  `Design/IceCrowTheme.xaml`. Never one dictionary per screen.
- `PresentationOptions:Freeze="True"` on shared brushes, geometries, and effects.
- `StaticResource` by default. `DynamicResource` only where a value genuinely
  changes at runtime.
- Colours live only in `Design/Colors.xaml`.

## Boundary rules

The overlay renders view state. It must not read `Power.log`, query the entity
store, decode deckstrings, run Battlegrounds analysis, create an `HttpClient`, or
talk to the Manacost API. Architecture tests check both the project graph and
overlay source tokens.

## Instrumentation

`OverlayRenderDiagnostics` keeps bounded counters — view-state updates applied
and skipped, lobby rows replaced, animations started, image cache hits/misses,
and actual decodes. They surface in the Debug developer window. This is
deliberately a set of counters, not a rendering telemetry pipeline.

Useful invariants to check while developing:

- Idle overlay: applied stays flat, skipped may rise, animations do not.
- Seven tiles rendered repeatedly: decodes stop increasing after the first pass.
- Rapid hovering: decodes and animations stay bounded.
