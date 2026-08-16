# Git-history privacy decision (owner decision required)

The current tree is sanitized: privacy guard tests reject BattleTag-like
identifiers and local installation paths in protected files, and no private
capture has ever been committed. Earlier commits in this repository's
history, however, contain identifiers that today's guards would reject
(session notes written before the guards existed). The values themselves
are intentionally not listed here.

This is a decision for the repository owner; the tooling will not rewrite
history on its own.

## Option A — accept the existing history

- No disruption: clones, branches, forks, and commit references stay valid.
- The old identifiers remain reachable to anyone with repository access
  through `git log`/`git show` on historical commits.
- Reasonable while the repository stays private to trusted collaborators.

## Option B — rewrite history before wider adoption

- Removes the identifiers from reachable history (e.g. `git filter-repo`).
- Requires a force-push; every existing clone and branch must be re-based
  or re-cloned, and all recorded commit SHAs (docs, CI links, session
  reports) become stale.
- Anything already fetched by an external fork or mirror cannot be erased
  retroactively — rewriting is only effective before wider sharing.

## Recommendation timing

Decide before granting access beyond the current collaborators. If Option B
is chosen, schedule it as a dedicated maintenance window: rewrite, verify
the privacy guards over the full rewritten history, force-push, and
invalidate old clones in one sitting.

Decision: **PENDING (owner)**.
