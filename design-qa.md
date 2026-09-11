# IceCrow deck tracking design QA

- Source visual truth: `C:\Users\zulut\AppData\Local\Temp\codex-clipboard-0b44e086-e931-4648-8859-9f29ac18d911.png`
- Match implementation: `C:\Users\zulut\Documents\IceCrow\.git\testagent\history-preview\history-matches.png`
- Deck implementation: `C:\Users\zulut\Documents\IceCrow\.git\testagent\history-preview\history-decks.png`
- Source pixels: 990 × 940. Implementation viewport: 1180 × 760 logical pixels at 96 DPI.
- State: Standard history plus the active-deck and deck-statistics route.

## Full-view comparison evidence

The source is a cropped current-state screenshot, so it is evidence of the raw-data problem rather than a desired full-window reference. The implementation was inspected at the application's normal 1180 × 760 viewport. It preserves the HearthPulse parchment, red rail, gold dividers, compact cards and application-owned title bar.

The match list now presents user-facing outcomes (`Победа`, `Поражение`, `Неполная запись`), a readable deck label and compact date/time. Raw card identifiers are replaced with a loading-safe hero description until the card database resolves a localized name. The detail panel explains whether the result was confirmed or the log ended without one.

## Deck statistics evidence

The deck route visibly contains:

- the active deck name and mode;
- a labelled name field and Hearthstone deck-code/export field;
- an explicit action to use or clear the deck;
- grouped statistics for the same canonical deck code;
- win rate calculated only from known wins and losses;
- total games, win-loss record and a separate count of games without a result.

The captured example shows `Контроль воин`, `Винрейт 50,0 %`, `3 матча · 1–1 · без итога: 1`, and the evidence label `Выбрана вручную перед матчем`.

## Required fidelity surfaces

- Typography: display type remains limited to headings; dense content uses the existing readable UI font.
- Spacing: the 244 px rail, 30 px content inset and panel gaps remain aligned at 1180 × 760.
- Colours and assets: the change reuses existing HearthPulse brushes and textures; no colour literal or placeholder artwork was added.
- Overflow: no horizontal or native list scrollbar is visible; mouse-wheel scrolling remains available.
- Copy: raw enum names, confidence values, card IDs and deck hashes are not exposed in the inspected states.

## Findings

No actionable P0, P1 or P2 visual mismatch remains for the requested states.

- P3: localized hero names depend on the existing card-data load; a neutral human message is shown until it completes.
- P3: automatic current-deck discovery still requires a licensed client-state adapter. The shipped workflow is an explicit one-time Hearthstone deck import and says so in the UI.

final result: passed
