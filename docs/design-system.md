# IceCrow design system

IceCrow should read as quiet tactical intelligence: a compact desktop instrument
that a Battlegrounds player stops noticing until it has something useful to say.
It is not a Hearthstone skin, a neon dashboard, or a chat client.

The system is deliberately cheap to render. Performance is part of the design,
not a mode bolted on afterwards: the identity comes from hierarchy, shape,
spacing, typography, and one cold accent, so it survives intact with every
optional effect switched off.

Companion documents:

- [Design tokens](design-tokens.md) and the canonical `design/tokens.json`
- [UI performance rules](ui-performance-rules.md) (enforced by architecture tests)
- [UI performance test plan](ui-performance-test-plan.md)

## Principles

1. **Low visual noise.** Borders separate; shadows are the exception.
2. **High information density.** Compact rows, not large cards.
3. **Strong hierarchy.** Every surface answers *what is this*, *what changed*,
   *what needs attention*, in that order.
4. **Fast recognition.** Numbers are found without reading sentences.
5. **Lightweight rendering.** Nothing animates unless state changed.

## Brand

Ice, crow, and precision, expressed through restraint:

- a cold graphite base and one pale ice-blue accent;
- angular, precise rhythm on a single spacing scale;
- exactly one signature motif — a thin ice-cut notch on the leading edge of the
  surface that currently matters.

No ice textures, no frozen frames, no fantasy ornament. If every branded pixel
were deleted, the result would still look like a finished professional tool.

## Colour

See [design-tokens.md](design-tokens.md) for values. The rules that matter:

- Semantic names only; controls never carry literal colours.
- The accent normally occupies well under ~10% of visible pixels.
- No pure `#000000` or `#FFFFFF` on large surfaces or long text runs.
- Status is never colour alone. `STALE` is a word before it is amber; a
  dangerous opponent shows its tier number before it shows an accent.

## Typography

Six roles — Caption, Label, Body, SectionTitle, Stat, HeroTitle — built from
system faces so text renders fast and hints correctly on Windows at every DPI.
Three weights (Regular, Medium, SemiBold). Uppercase is rare and short.

## Surfaces

Overlay panels use a nearly opaque background (95–98%), one hairline border, and
a modest radius. This is deliberately *not* 50% transparency plus blur: solid
panels stay readable over bright Hearthstone scenes and never require continuous
composition.

- **Shadow.** At most one small drop shadow per top-level floating panel, and
  only when `IcePanel.HasElevation` is set. Never on rows, tiles, badges, or
  labels. Never stacked. If a border separates well enough, use the border.
- **Blur.** None. Not optional-but-default-on; none at all.
- **Glow.** Reserved for brief important events, expressed as a short accent
  pulse on the signature notch.

## Components

The library is intentionally small, and every component has a real consumer
today. `IceToggle` and similar speculative controls are deliberately absent
until something needs them.

| Component | Role | Consumers |
| --- | --- | --- |
| `IcePanel` | Surface primitive: background, border, optional header, optional signature notch, optional single shadow | overlay status/detail/settings panels, developer window, design preview |
| `IceBadge` | Compact marker (`T5`, `2T`, `STALE`, progression steps) with Neutral/Accent/Positive/Warning/Danger tones | lobby rows, detail panel |
| `IceStat` | One labelled number, value first | detail panel |
| `IceCrow.Style.Button` | Button styling with Normal/Hover/Pressed/Disabled states (a style, not a new control type) | overlay toolbar and settings, developer window |
| `OpponentRow` | Compact lobby opponent: hero, health, tier; age only in the regular layout | overlay lobby, design preview |
| `MinionTile` | Compact minion: art, attack, health, deliberate placeholder | detail panel, design preview |

### Component states

Each component supports predictable states driven by tokens, never by ad-hoc
colours: Normal, Hover, Pressed, Selected, Disabled, and the data states
Attention and Stale.

## Information hierarchy

Lobby row (compact ⇄ regular):

```
┌───────────────────────────────┐
│ ◣ Reno Jackson            T5 │
│   28 HP  STALE            2T │   ← the second line is dropped when compact
└───────────────────────────────┘
```

Opponent Memory detail — the visual benchmark for the whole system:

```
┌─────────────────────────────────────┐
│ ◣ RENO JACKSON                  T5 │
│   Last seen · Turn 8            2T │
│                                     │
│   ▣ ▣ ▣ ▣ ▣ ▣ ▣                    │
│                                     │
│   28 HP     T5        2             │
│   Health    Tavern    Triples       │
│                                     │
│   Progression                       │
│   [T3·5] [T4·7] [T5·9]              │
│   2 triples                         │
└─────────────────────────────────────┘
```

Deep information belongs in a temporary detail surface. The lobby row shows who,
how healthy, and what tier; hovering or pinning reveals board and history. This
is simultaneously a minimalism, density, and performance decision.

## Minion tiles

A tile prioritises art, attack, and health. Name and tier are secondary and
appear in the detail surface, never inside a small overlay tile. Full Hearthstone
card frames are never rendered in overlay panels.

Missing art is a designed state, not a failure state: the tile shows a dark
surface with the card's initials. There is no broken-image glyph.

## Animation

Animation communicates a state change and nothing else.

| Allowed | Budget |
| --- | --- |
| Panel entrance (opacity + 6 px translate) | 140 ms |
| Important-event pulse on the signature notch | 240 ms |
| Hover / pressed / selected | instant state change, no animation |

Forbidden: continuous animation, animated backgrounds or gradients, particles,
looping shimmer, breathing glow, and anything past 350 ms. Hover is an instant
token swap because that is both cheaper and more responsive than a fade.

Animations are started imperatively from `OverlayAnimations` with
`FillBehavior.Stop`, so no control stays reachable through a retained storyboard
and a disabled effect costs nothing.

## Update model

The overlay has no render loop. It is event-driven end to end:

```
TrackingUpdate → Presentation projection → view state changed?
                                            ├─ no  → nothing happens
                                            └─ yes → WPF update
```

`BattlegroundsOverlayViewState` and `OpponentOverlayViewState` have value
equality. `OverlayWindow.ApplyViewState` drops a value-equal update, and only the
lobby rows whose value changed are replaced. An eight-row lobby does not need
virtualization, but it must not rebuild every container per snapshot.

## Responsive and DPI behaviour

Density is derived from the Hearthstone client width
(`OverlayLayout.FromClientWidth`) and published down the element tree as an
inherited `OverlayVisuals.LayoutMode`. Compact shrinks thumbnails, rows, and
panels and drops the secondary line; it never scales the UI as one bitmap and
never hides core information.

All sizes are device-independent units with `UseLayoutRounding`, so 100%, 125%,
150%, and 200% scaling all work without pixel assumptions.

## Performance Mode

`OverlayRenderingSettings` is an immutable record with one input
(`PerformanceMode`) and three derived policies: panel shadow, entrance
animation, and event pulse. Performance Mode never changes layout, hierarchy, or
which information is shown — it only removes optional work. Normal mode is
already inexpensive and never assumes a gaming GPU.

## Accessibility and readability

Minimalism must not mean tiny, low-contrast, or hidden. Body text stays at
readable sizes at every supported DPI, panels stay near-opaque so contrast does
not depend on the scene behind them, and no important state is signalled by
colour alone.

## Developer design preview

`src/IceCrow.App/DesignPreview` is a Debug-only gallery of every component and
state, including live Performance Mode and layout-mode toggles. It makes visual
iteration possible without running Hearthstone and is removed from Release
builds by the project file (enforced by an architecture test).

## Future macOS adaptation

`design/tokens.json` is the specification; the WPF dictionaries are one
implementation. A macOS client should preserve the visual identity, spacing
rhythm, hierarchy, component set, and interaction model. It does not need
pixel-identical rendering, and it should use the platform's own system faces for
the typography roles rather than reproducing Windows font names.

## Theme support

One dark theme. Semantic tokens keep a second theme possible, but no light mode
will be added merely for completeness.
