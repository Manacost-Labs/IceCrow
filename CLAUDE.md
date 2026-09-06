# IceCrow — Claude Code notes

`AGENTS.md` is the single canonical project policy for every agent. Read it
completely, then `docs/module-boundaries.md`, then the nearest local
`AGENTS.md` under `src/` before editing. Do not duplicate or weaken those
rules here; this file carries only Claude-host-specific guidance.

## Claude-host specifics

- The `csharp-lsp` plugin is the language server referenced by the routing
  rules in `AGENTS.md`; prefer it over broad grep for symbol questions.
- The official `dotnet/skills` plugins (`dotnet`, `dotnet-diag`,
  `dotnet-test`, `dotnet-msbuild`) are installed from the
  `dotnet-agent-skills` marketplace; `dotnet-artisan` is a supplementary
  bundle. Load only the groups the `AGENTS.md` router names for the phase.
- Iterate on overlay UI through the Debug-only design preview in
  `src/IceCrow.App/DesignPreview`; use the Browser/desktop tools only to
  observe, never to implement UI changes.
