# Git-history privacy decision — Option B executed

The current tree is sanitized: privacy guard tests reject BattleTag-like
identifiers and local installation paths in protected files, and no private
capture has ever been committed. Earlier commits contained identifiers in
session notes written before the guards existed; the values themselves are
intentionally not reproduced here.

## Decision and outcome

**Option B (rewrite history) was chosen by the owner and executed on
2026-08-17.** The reachable history of `main` was rewritten and
force-pushed; the privacy guards pass over the full rewritten history, and
the identifiers are no longer reachable from any current branch.

All commit SHAs recorded before the rewrite (docs, CI run links, session
reports) refer to pre-rewrite history. Where such a SHA still appears in a
dated session document it is historical; current documents reference only
reachable commits.

## What the rewrite does NOT guarantee

Do not treat the rewrite as complete erasure:

- old Git objects may remain retrievable **by exact SHA** on GitHub for some
  time (cached pack data, PR references, API access) even though they are
  unreachable from any branch;
- external clones, forks, mirrors, or CI caches made before the rewrite
  still contain the old history and cannot be invalidated from this
  repository.

If complete server-side removal is required, the repository owner can
request garbage collection of unreachable objects via GitHub Support. That
is an owner action; no support contact or further force-push happens
without explicit instruction. Until that cleanup happens, treat the GC
request as the blocking follow-up before widening repository access, and
do not publish pre-rewrite commit ids in reachable documents — a current
doc that quotes an exact old SHA hands the reader the only key needed to
retrieve the purged history.

## Consequences already absorbed

- Every collaborator clone had to be re-based or re-cloned once.
- One CI push run failed after the rewrite because the whitespace step
  diffed against a pre-rewrite base revision; the step is now deterministic
  against the whole tree and immune to force-push history.
