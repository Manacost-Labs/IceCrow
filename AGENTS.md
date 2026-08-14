# IceCrow Engineering Guide

IceCrow is a Windows Hearthstone and Battlegrounds tracker. Keep the core deterministic, testable, and usable without a network or backend service.

## Clean-room boundary

The local Hearthstone Deck Tracker reference is at `../references/Hearthstone-Deck-Tracker`.

HDT is behavioral reference material only. It may be inspected to understand Hearthstone lifecycle transitions, GameTags, edge cases, and Windows overlay APIs. Never copy complete HDT files, copy large source blocks, mechanically rename HDT classes, or reproduce legacy architecture that IceCrow does not need. Prefer primary documentation when it is available and implement IceCrow behavior independently.

## Projects and responsibilities

| Project | Responsibility | Allowed IceCrow dependencies |
| --- | --- | --- |
| `IceCrow.App` | WPF composition root and application lifetime | `Overlay`, `Platform.Windows`, `Hearthstone.Logs`, `Recording` |
| `IceCrow.Platform.Windows` | Win32 and Windows-specific integration | None |
| `IceCrow.Overlay` | Overlay presentation boundary | `Battlegrounds`, `Battlegrounds.Memory`, `Platform.Windows` |
| `IceCrow.Hearthstone.Logs` | Raw Hearthstone log input | None |
| `IceCrow.Hearthstone.Protocol` | Normalized game events and protocol compatibility | None |
| `IceCrow.Hearthstone.Entities` | Hearthstone entity state | `Hearthstone.Protocol` |
| `IceCrow.Battlegrounds` | Battlegrounds state and reducers | `Hearthstone.Entities`, `Hearthstone.Protocol` |
| `IceCrow.Battlegrounds.Memory` | Immutable historical Battlegrounds snapshots | `Battlegrounds` |
| `IceCrow.Recording` | Offline capture and deterministic replay composition | `Hearthstone.Logs`, `Hearthstone.Protocol`, `Hearthstone.Entities`, `Battlegrounds`, `Battlegrounds.Memory` |

Test projects may reference only the production project under test and its transitive dependencies.

## Dependency rules

- Domain projects must not reference WPF, `System.Windows`, window handles, or UI controls.
- UI code must never parse `Power.log` directly.
- Raw log readers and parsers must never manipulate WPF directly.
- Keep normalized events between parsing and state reduction.
- Avoid global mutable state and prefer immutable snapshots for historical data.
- Keep numeric GameTag compatibility values inside explicit compatibility types.
- Core tracking must not require a network or backend service.
- Prefer the .NET BCL over new packages. Verify the version, license, and provenance before adding any package.
- Do not silently swallow important parser failures. Unknown Hearthstone events must not crash the tracker.

## Standard validation

Run from the repository root:

```powershell
dotnet restore IceCrow.sln
dotnet build IceCrow.sln --no-restore
dotnet test IceCrow.sln --no-build --no-restore
```

Before reporting completion, review the complete diff and run security checks when the change introduces security-sensitive behavior.

## Regression policy

Every discovered Hearthstone bug or edge case must receive a regression test in the closest matching test project. The test should describe the observed input and expected normalized or reduced state without depending on WPF, a live Hearthstone process, or a backend service.
