# Battlegrounds regression workflow

This workflow turns an observed Hearthstone failure into a small, permanent,
deterministic regression test without committing private player data.

## 1. Capture evidence

Capture an IceCrow format-v1 `RecordedMatch` through the existing
`MatchRecorder` integration point while reproducing the bug. Keep the original
recording outside the repository. During an active match, observation must stay
passive: do not restart Hearthstone, alter `log.config`, click the game, or move
focus merely to improve the recording.

Automatic end-user capture/export is not wired into the WPF app yet. Until it
is, this step is a developer/instrumented-build operation. A raw `Power.log`
copy is private evidence and must never be committed directly.

## 2. Import and anonymize

Run the developer tool from the repository root. The output directory must not
already exist, and the source type is deliberately explicit:

```powershell
dotnet run --project tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj -- `
  import `
  --input C:\private\captures\match.icecrow.json `
  --output tests\fixtures\battlegrounds\candidate-bg-001 `
  --name bg-001 `
  --source-type real-anonymized `
  --reason "Reconnect lost the current opponent" `
  --hearthstone-version "known version or unknown"
```

The importer:

- validates the untrusted recording with the existing file, event, string,
  lifecycle, and retained-memory limits;
- deterministically replaces player/entity names and account identifiers;
- removes arbitrary unknown-event text and sanitizes user paths/BattleTags;
- preserves entity IDs, player IDs, card IDs, GameTags, event order, and
  timestamps;
- writes `recording.icecrow.json`, `expected.json`, and `README.md` into a new
  candidate directory;
- replays the candidate before publishing the directory;
- never runs Git commands and never overwrites an existing candidate.

Review all three generated files before staging them. Search for BattleTags,
account values, usernames, absolute paths, tokens, and private server data.
Tooling reduces risk; human privacy review remains required.

## 3. Make the failure explicit

`expected.json` contains small semantic checkpoints rather than a full object
graph. Keep only behavior needed to reproduce the defect, for example:

- phase and turn at a transition;
- lobby count/current opponent;
- latest remembered opponent board minion count;
- ended state after a game boundary.

Rename generated checkpoints so their intent is obvious. Change the relevant
expectation to the correct behavior and run:

```powershell
dotnet run --project tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj -- `
  validate --fixture tests\fixtures\battlegrounds\candidate-bg-001
```

Before the fix, validation should fail with the fixture, checkpoint, expected
field, and actual field shown. If it passes, the fixture does not reproduce the
bug yet.

## 4. Minimize manually when useful

Do not build a complex automatic reducer around an unproven case. Work on a
copy of the private recording and remove contiguous groups that are clearly
unrelated. Preserve:

- the initial `MatchStarted` event;
- declarations/tags required to create referenced entities;
- order and timestamps when timing/order is part of the bug;
- a valid lifecycle (nothing after `MatchEnded`);
- the event that demonstrates the failure.

After each reduction, import to a new candidate directory and confirm the same
golden checkpoint still fails. Never mutate the only copy of the original
evidence. Prefer a readable 20-event regression over a 20,000-event match when
both reproduce the same behavior, but keep the full anonymized recording when
reduction changes the result.

## 5. Fix and keep the evidence

Make the smallest parser/reducer/tracking correction, then run the candidate,
the complete Debug suite, and Release suite. Keep the anonymized minimized
fixture permanently beside its README. The README must state the observed
behavior, why the checkpoint matters, and the known Hearthstone version when
available.

The permanent flow is:

```text
Bug observed
  -> capture private recording
  -> validate and anonymize
  -> minimize if useful
  -> add the expected failing checkpoint
  -> reproduce
  -> fix
  -> keep the fixture
```

## Corpus status and target cases

Committed manifests distinguish `synthetic` from `real-anonymized`. Synthetic
files validate infrastructure but never count as production evidence. The
initial real corpus should eventually cover normal solo, armor damage, empty
and seven-minion boards, opponent death, triples, Tavern progression,
reconnect, log rotation/restart, consecutive matches, concede, and a long
combat. Coverage may be claimed only after reviewed real-anonymized fixtures
for those cases exist.
