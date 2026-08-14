# UI performance test plan

Overlay cost cannot be measured honestly without a real Hearthstone client, a
real GPU, and a real desktop composition path. This document is the manual
procedure for producing that evidence, plus the structural facts that *are*
verifiable today.

Nothing here claims zero performance impact. An always-on-top WPF window over a
DirectX game always costs something; the goal is that the cost is small, bounded,
and does not scale with time.

## What is already verified automatically

`IceCrow.Overlay.Tests` constructs the real overlay window on an STA thread and
asserts:

- every design token, component template, and binding resolves at runtime;
- a value-equal view state is skipped instead of touching WPF;
- only the lobby row whose value changed is replaced;
- Performance Mode removes every effect from the visual tree and suppresses
  animations;
- the important-event pulse runs exactly once, on a real change.

`IceCrow.Architecture.Tests.UiPerformanceRuleTests` asserts the effect policy,
the single shadow declaration, the absence of hardcoded colours, the overlay's
boundary, and the exclusion of the developer preview from Release.

These are structural guarantees. They are not a substitute for the manual runs
below.

## Structural before/after for the design-system milestone

| Property | Before | After |
| --- | --- | --- |
| Continuous render loop | none | none |
| Dispatcher timers | 1 s lifecycle, 33 ms modifier poll | unchanged |
| WPF work for a snapshot with no visible change | full `ItemsSource` reassignment, every container rebuilt | update dropped by value equality |
| WPF work when one opponent changes | every container rebuilt | one row container replaced |
| Effects in the visual tree | none | at most one `DropShadowEffect` per floating panel, zero in Performance Mode |
| Animations | none | two finite animations (140 ms entrance, 240 ms event pulse), none looping |
| Image decoding | not implemented | bounded off-thread decode at 64/96/128/192 px, cached and deduplicated |
| Runtime blur | none | none |

The 33 ms modifier poll is the one periodic cost, and it is unchanged by this
milestone. It reads keyboard modifier state; it does not invalidate visuals.

## Manual scenarios

Run each on the same machine, same Hearthstone settings, same IceCrow build
configuration. Record the configuration next to the numbers.

1. IceCrow running, Hearthstone closed.
2. Hearthstone at the main menu, overlay connected, no lobby.
3. Battlegrounds lobby with eight players visible.
4. Opponent detail panel open with seven minion thumbnails.
5. Rapid hovering across all eight rows for 30 seconds.
6. Hearthstone windowed, then windowed fullscreen, then fullscreen.
7. A full Battlegrounds match, start to finish.
8. Performance Mode on, repeating scenarios 3–5.

Repeat the relevant scenarios at 1920×1080, 2560×1440, and 3840×2160, and at
100%, 125%, 150%, and 200% display scaling.

## What to measure

| Measurement | How |
| --- | --- |
| IceCrow CPU (idle and active) | Task Manager details view or `Get-Counter` on the process |
| Working set / private bytes | same |
| Handle count | same, watched for growth over a full match |
| GPU usage, if the machine reports it | Task Manager GPU column |
| Frame responsiveness | subjective, but recorded: does Hearthstone feel unchanged? |
| Overlay counters | Debug developer window, "Overlay rendering" section |

The overlay counters are the cheapest signal and should be checked first:

- during scenario 2, `applied` should stay flat;
- during scenario 4, `decoded` should stop rising once seven tiles have loaded;
- during scenario 5, `decoded` and `anim` must stay bounded;
- `skipped` rising while `applied` stays flat is the healthy pattern.

## Recording results

| Scenario | Config | CPU idle | CPU active | Working set | Handles | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |

Fill this table in during the real-client acceptance run and keep the numbers
next to the run's build hash. Until then, `docs/performance.md` is correct that
IceCrow has no trustworthy idle-CPU or steady-state memory baseline, and no
numeric limit should be invented from a desk estimate.

## When to re-run

- Any new overlay component that updates more than once per turn.
- Any change to view-state diffing, image decoding, or the animation set.
- Any change to the effect policy in `docs/ui-performance-rules.md`.
