# IceCrow contributor brief

Read `AGENTS.md`, `docs/module-boundaries.md`, and the nearest local `AGENTS.md`
before editing. IceCrow is a local-first Windows Hearthstone/Battlegrounds
tracker; deterministic tracking must work without WPF, HWND, network, or a
backend.

Use the local HDT checkout only for behavior research. Never copy complete
files or reproduce its legacy architecture. Normalize `Power.log` before state
reduction. Features consume immutable `TrackingUpdate`/`TrackingSnapshot`
outputs; do not turn `TrackingSession` into a feature registry. Keep all
external collections and work bounded, snapshots immutable, optional APIs
failure-isolated, and native/WPF code in their designated boundaries.

Do not add `Common`, `Utils`, a service locator, mediator, global event bus, or
new package/project without dependency evidence. Add a regression test for
every Hearthstone edge case. Run the standard Debug and Release quality gate,
format verification, whitespace check, and security review appropriate to the
changed boundary.
