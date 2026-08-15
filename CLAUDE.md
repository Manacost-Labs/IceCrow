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

For UI work read `docs/design-system.md`, `docs/design-tokens.md`, and
`docs/ui-performance-rules.md`. Use the design tokens in
`src/IceCrow.Overlay/Design`; never hardcode colours, add blur, or start a
per-frame loop. Iterate visually through the Debug-only design preview in
`src/IceCrow.App/DesignPreview`.

Do not add `Common`, `Utils`, a service locator, mediator, global event bus, or
new package/project without dependency evidence. Add a regression test for
every Hearthstone edge case. Run the standard Debug and Release quality gate,
format verification, whitespace check, and security review appropriate to the
changed boundary.

## C# / .NET tool routing

Prefer retrieval-led reasoning over model memory for .NET work.

### Exact C# navigation

Use C# LSP (`csharp-lsp`) / Serena first for definitions, references, callers,
implementations, overrides, and type hierarchy. Do not use broad grep when
symbol-aware navigation can answer the question exactly.

### Repository discovery

Use semantic codebase search (if configured) for concepts whose symbol name is
unknown, cross-module behavior, similar implementations, and architecture
ownership discovery. Use grep/ripgrep for exact text, configuration, generated
files, or when semantic tools are unnecessary.

### .NET guidance

Use the installed `dotnet`/`dotnet-test`/`dotnet-diag`/`dotnet-msbuild` skills
(and `dotnet-artisan` for deeper WPF/concurrency/testing specialist guidance)
for current C# patterns, async/concurrency/cancellation, API and type design,
WPF, testing, performance, diagnostics, and MSBuild. Prefer the dedicated
`csharp-lsp` plugin for C# semantic intelligence even if another .NET bundle
also advertises an LSP integration. IceCrow's own `AGENTS.md`, architecture
docs, and tested invariants override generic skill advice.

### Before editing

1. Read the owning module's guidance.
2. Resolve exact symbols and references.
3. Inspect nearby tests.
4. Inspect recent Git history if behavior is unclear.
5. Make the smallest coherent change.

### After editing

1. Run targeted tests.
2. Review diagnostics.
3. Check model/file growth.
4. Review `git diff`.
5. Commit the logical unit.
6. Continue with the next unit.

Do not accumulate a giant uncommitted diff.
