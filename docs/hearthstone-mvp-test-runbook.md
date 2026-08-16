# Hearthstone MVP test runbook

Operator guide for validating IceCrow against the real Hearthstone client.
Record every step as `PASS`, `FAIL`, `NOT RUN`, or `BLOCKED` — never mark a
step passed without executing it. Known Power.log limitations are listed at
the end; do not reinterpret them as bugs.

## Preparation

1. Check out a clean HEAD (`git status` reports nothing to commit).
2. Build the test client:

   ```powershell
   ./scripts/build-test-debug.ps1
   ```

3. Note the executable path the script prints
   (`src\IceCrow.App\bin\Debug\net10.0-windows\IceCrow.App.exe`).
4. Launch it; the developer window "IceCrow — Developer" opens (Debug only).
5. Confirm **Match capture** shows `Enabled: No · Session: Off ·
   Persistence: Idle` — capture always starts off.
6. Confirm `%LocalAppData%\IceCrow\private-captures\` does not exist or is
   empty before the first enabled capture.

## Scenario A — IceCrow before Hearthstone

- [ ] Launch IceCrow, then launch Hearthstone.
- [ ] Power.log discovery succeeds (status line in the developer window).
- [ ] Enter Battlegrounds; tracking activates only on Battlegrounds evidence.
- [ ] Overlay appears aligned to the game window and never steals focus.

## Scenario B — Hearthstone before IceCrow

- [ ] Launch Hearthstone first, then IceCrow.
- [ ] The existing log/session is discovered.
- [ ] If IceCrow attached after the decisive Battlegrounds signals, note
      honestly that classification may stay inactive (known limitation).

## Scenario C — one complete match (capture enabled)

Enable capture in the developer window before queueing.

- [ ] Session moves `Waiting` → `Recording` when the match starts.
- [ ] The lobby populates; turns increment; phases transition.
- [ ] The current opponent is detected; its board is captured on combat.
- [ ] A repeated opponent shows a board diff with honest confidence.
- [ ] The match ends exactly once; Session returns to `Waiting`.
- [ ] Persistence transitions `Saving` → `Saved`; pending returns to 0.
- [ ] Exactly one new file appears under `private-captures\`, named
      `<UTCtimestamp>_<32 hex>.icecrow.json` with no player identity.

## Scenario D — two consecutive matches

- [ ] Play a second match without restarting IceCrow.
- [ ] No leakage of entities, lobby, opponent memory, timeline, or recording
      events from match one.
- [ ] A second, separate capture file appears.

## Scenario E — disable/re-enable

- [ ] Disable capture mid-match: status shows a **notice** (intentional
      discard), never `Failed`, and no file appears for that match.
- [ ] Re-enable before the match ends: Session shows `Waiting`; the current
      match is not captured; the next match records normally.

## Scenario F — offline Manacost API

Block network access or the Manacost endpoint.

- [ ] Tracking, capture, and the overlay keep working.
- [ ] Card metadata falls back to cache or unknown-name rendering.
- [ ] No crash, no error loop.

## Scenario G — client restart / log rotation

- [ ] Close and restart Hearthstone between matches; the new log session is
      discovered without duplicating state.
- [ ] Note actual behavior on truncated/rotated logs; reconnect history gaps
      are a known limitation, not a test failure.

## Scenario H — shutdown mid-match

- [ ] Close IceCrow while a captured match is running.
- [ ] Shutdown completes promptly (bounded drain, then cancellation).
- [ ] No capture file appears for the incomplete match and no `.tmp` file
      survives the next start.

## Validating a saved capture

From the repository root, replay-validate and (after human privacy review)
import a capture as an anonymized fixture:

```powershell
dotnet run --project tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj -- `
  import `
  --input "$env:LOCALAPPDATA\IceCrow\private-captures\<capture>.icecrow.json" `
  --output tests\fixtures\battlegrounds\candidate-real-001 `
  --name real-solo-normal-001 `
  --source-type real-anonymized `
  --reason "First real solo Battlegrounds match" `
  --hearthstone-version "<version or unknown>"
```

Review `recording.icecrow.json`, `expected.json`, and `README.md` for
BattleTags, account values, usernames, paths, and tokens before staging.

## Performance observation (optional but recommended)

Attach counters during a representative session:

```powershell
dotnet-counters monitor --process-id <IceCrow PID>
```

Record CPU, working set, GC heap, allocation rate, and Gen collections at:
idle, Hearthstone menu, recruit, combat, capture disabled/enabled, save at
match end, and after two consecutive matches. Record machine specs, Windows
version, resolution/DPI, and build configuration alongside the numbers.

## Known Power.log limitations

- Exact game type may require client-state/reflection support; IceCrow does
  not invent `GAMETYPE` semantics.
- Late attach can miss the decisive Battlegrounds signals.
- Reconnect may provide truncated history; earlier lobby/board history is
  then unrecoverable.
- Undocumented phase tags are compatibility signals and can change after
  Hearthstone patches.
