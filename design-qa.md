# IceCrow history window design QA

- Source visual truth: `C:\Users\zulut\AppData\Local\Temp\codex-clipboard-f033657a-8fcf-4ff6-b4c8-94cadc4e78ea.png`
- Implementation screenshot: `C:\Users\zulut\AppData\Local\Temp\icecrow-history-standard-final.png`
- Combined comparison: `C:\Users\zulut\AppData\Local\Temp\icecrow-design-qa-comparison.png`
- Viewport: 1180 × 760 logical pixels at the current Windows desktop scale.
- Source pixels: 1172 × 1179. Implementation pixels: 1180 × 760. The source is a cropped defect annotation and was compared at native density; no size-based fidelity judgement was made outside the shared match-history region.
- State: Standard match-history route, local account not linked.

## Full-view comparison evidence

The implementation preserves the HearthPulse parchment, red rail, gold divider, display type and compact card treatment. The annotated native mode selector has been removed and replaced by persistent mode navigation in the left rail. Native list scrollbars are hidden while mouse-wheel scrolling remains available. The standard Windows title bar and frame are replaced with an application-owned title bar using the same rail texture and colour tokens.

## Focused region comparison evidence

The comparison focused on the source's two annotated defects: the filter row and the match-list scrollbar. Both are absent in the implementation. The selected mode is visible in the left rail and repeated as the page title, so the current filter remains discoverable without the removed ComboBox. The custom title bar was additionally inspected in normal and maximized states.

## Required fidelity surfaces

- Fonts and typography: HearthPulse display font remains limited to titles and headings; Segoe UI remains legible for dense history and account content. No unexpected wrapping was observed at 1180 × 760.
- Spacing and layout rhythm: the 244 px rail, 38 px title bar, 30 px content inset and 16–18 px panel gaps remain consistent. Search now uses a stable 480 px width.
- Colours and tokens: every new surface uses existing HearthPulse brushes; no colour literal was added outside the design-token dictionaries.
- Image quality and assets: existing parchment, red rail and divider textures remain sharp and correctly cropped. No replacement placeholder or synthetic decorative asset was introduced.
- Copy and content: navigation names match the four supported modes; account copy explicitly preserves local history when disconnected.

## Interaction checks

- Standard, Arena and Battlegrounds routes opened from the left rail.
- Mode route selection updated the page title and filtered match rows.
- Custom maximize button expanded the window; a title-bar double click restored it to 1180 × 760.
- HearthPulse account page opened without starting or approving an OAuth flow.

## Findings

No actionable P0, P1 or P2 visual mismatch remains for the requested changes.

## Comparison history

- First pass: the overview list exposed a native Windows scrollbar once additional history rows appeared, and the search field collapsed to its minimum content width.
- Fixes: hid scrollbars on all history/deck lists, kept wheel scrolling, set search width to 480 px, and shortened the rail subtitle to prevent clipping.
- Post-fix evidence: the final 1180 × 760 capture shows no native selector or scrollbar, a stable search field, complete rail labels and application-owned chrome.

## Follow-up polish

- P3: replace the textual window controls with a licensed icon set if one is added to the product asset system later.

final result: passed
