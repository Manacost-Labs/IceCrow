# Design tokens

`design/tokens.json` is the canonical, platform-neutral token set. This document
explains what each token means and how the current WPF implementation maps to
it. WPF resource keys are an implementation detail: a future macOS client must
implement the semantics below, not the resource names.

Rules:

- Controls consume semantic tokens only. `src/IceCrow.Overlay/Design/Colors.xaml`
  is the single file allowed to contain literal colour values, and an
  architecture test enforces it.
- Values live on one small scale. A value that is not on the scale needs a
  reason, not a new constant.
- Sizes that both the layout code and the design tokens need are C# constants in
  `IceCrow.Presentation.OverlayLayout` and are referenced from XAML with
  `x:Static`, so the two cannot drift.

## Colour

| Token | Value | Meaning | WPF key |
| --- | --- | --- | --- |
| `surface.base` | `#0B0F14` | Window/background plane. Never pure black. | `IceCrow.Brush.Surface` |
| `surface.raised` | `#131A22` | Content sitting on the base plane. | `IceCrow.Brush.SurfaceRaised` |
| `surface.hover` | `#1B242F` | Pointer-over state. | `IceCrow.Brush.SurfaceHover` |
| `surface.selected` | `#212C39` | Selected/pinned state. | `IceCrow.Brush.SurfaceSelected` |
| `surface.panel` | 95% `#131A22` | Floating overlay panel over the game. | `IceCrow.Brush.PanelSurface` |
| `surface.panelStrong` | 98% `#0E141B` | Detail/settings panels that must stay readable over bright scenes. | `IceCrow.Brush.PanelSurfaceStrong` |
| `border.subtle` | `#243039` | Default separation. Preferred over shadow. | `IceCrow.Brush.Border` |
| `border.strong` | `#35454F` | Hover/emphasis separation. | `IceCrow.Brush.BorderStrong` |
| `text.primary` | `#E8EEF4` | Names and numbers. Cold near-white, never `#FFFFFF`. | `IceCrow.Brush.TextPrimary` |
| `text.secondary` | `#A7B7C6` | Supporting lines. | `IceCrow.Brush.TextSecondary` |
| `text.muted` | `#6F8090` | Captions and placeholders. | `IceCrow.Brush.TextMuted` |
| `accent.primary` | `#7FD8E8` | The ice accent. Signature notch, selection, single markers. | `IceCrow.Brush.Accent` |
| `accent.strong` | `#A8ECF7` | Accent text on an accent-soft background. | `IceCrow.Brush.AccentStrong` |
| `accent.soft` | 24% accent | Accent badge background. | `IceCrow.Brush.AccentSoft` |
| `status.positive` | `#7FC79A` | Confirmed good state. | `IceCrow.Brush.Positive` |
| `status.warning` | `#E0B36A` | Outdated or uncertain information. | `IceCrow.Brush.Warning` |
| `status.danger` | `#D9737A` | Immediate risk. | `IceCrow.Brush.Danger` |
| `shadow` | `#05080B` | Tint of the single allowed drop shadow. | `IceCrow.Color.Shadow` |

The accent should stay under roughly 10% of visible overlay pixels. If a screen
needs more accent than that, its hierarchy is wrong.

## Spacing, radius, border

| Token | Value | WPF key |
| --- | --- | --- |
| `spacing.xs` … `spacing.xxl` | 4, 8, 12, 16, 24, 32 | `IceCrow.Space.Xs` … `IceCrow.Space.Xxl` |
| `radius.sm` / `md` / `lg` | 3 / 6 / 10 | `IceCrow.Radius.Small` / `.Medium` / `.Large` |
| `border.hairline` | 1 | `IceCrow.BorderThickness.Hairline` |

Composed paddings (`IceCrow.Padding.Panel`, `.Row`, `.Badge`, `.Button`) are
built only from the spacing scale.

## Typography

System faces only; no font file ships with IceCrow.

| Role | Size | Weight | Default colour | WPF style |
| --- | --- | --- | --- | --- |
| `caption` | 10.5 | Regular | `text.muted` | `IceCrow.Text.Caption` |
| `label` | 11.5 | Regular | `text.secondary` | `IceCrow.Text.Label` |
| `body` | 12.5 | Regular | `text.primary` | `IceCrow.Text.Body` |
| `sectionTitle` | 13 | Medium | `text.primary` | `IceCrow.Text.SectionTitle` |
| `stat` | 15 | SemiBold | `text.primary` | `IceCrow.Text.Stat` |
| `heroTitle` | 17 | SemiBold | `text.primary` | `IceCrow.Text.HeroTitle` |

Only Regular, Medium, and SemiBold are used. Uppercase is reserved for the
product mark and very short technical labels such as `STALE`.

## Opacity

| Token | Value | Use |
| --- | --- | --- |
| `opacity.disabled` | 0.42 | Disabled controls |
| `opacity.muted` | 0.7 | De-emphasised content |
| `opacity.signature` | 0.6 | Resting opacity of the signature notch |

## Motion

| Token | Value | Use |
| --- | --- | --- |
| `motion.fast` | 140 ms | Panel entrance |
| `motion.normal` | 180 ms | Ordinary transitions |
| `motion.event` | 240 ms | Important transient event pulse |
| `motion.maximumInteraction` | 350 ms | Hard ceiling for any overlay interaction |

Only `opacity`, `translate`, and `scale` may be animated. Every animation is
finite; nothing loops.

## Layout

`IceCrow.Presentation.OverlayLayout` owns these values so they are testable
without WPF.

| Token | Compact | Regular |
| --- | --- | --- |
| `minionTileWidth` | 46 | 58 |
| `opponentRowWidth` | 168 | 196 |
| `detailPanelWidth` | 288 | 348 |

`breakpoint.compactMaximumClientWidth` is 1180 device-independent pixels of
Hearthstone client width. `artDecodeSteps` (64/96/128/192) are the only decode
widths used for minion art, so a DPI or breakpoint change reuses cached decodes
instead of re-decoding at an arbitrary size.

## Signature

One motif: a thin angular ice-cut notch (`M0,0 L3,4 L3,17 L0,21 Z`) on the
leading edge of the surface that currently matters — the status panel, the
pinned opponent panel, a selected lobby row, or a row that requires attention.
There is no second brand decoration, and removing the notch leaves a complete,
professional interface.
