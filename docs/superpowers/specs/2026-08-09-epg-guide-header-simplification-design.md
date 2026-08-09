# EPG Guide Header Simplification

## Goal

Make the EPG header user-facing rather than exposing the guide's internal time-window bounds, and remove the duplicate close target from the shared mobile/TV guide experience.

## Display

- Use title case for `Player.Epg.GuideTitle`: `Program Rehberi` in Turkish and `Program Guide` in English.
- Replace the `HH:mm – HH:mm` header subtitle with a localized label based on the current local calendar day: `Bugün · 8 Ağustos` / `Today · 8 August`.
- Format the date using the active application culture so month names follow the selected language.
- Keep the date value pattern in each supported language resource so locale-specific forms such as German `9. August` and Spanish `9 de agosto` remain natural.
- Base the label on the current local day, not `WindowStart`, because the eight-hour guide window can cross midnight.
- Refresh the label during the existing live update so it rolls over correctly at midnight.
- Apply the same date-label behavior to the desktop EPG header.

## Close behavior

- Keep one close target in the mobile/TV guide header, named `CloseGuideButton` and bound to `ToggleEpgPanelCommand`.
- Remove the close button from the video hero overlay; keep the hero play/pause control.
- Preserve the existing initial-focus fallback to `CloseGuideButton`, so remote/keyboard navigation has exactly one close destination.
- Preserve the desktop guide's existing header close control; the desktop transport EPG toggle remains the entry/exit command and is not part of the mobile hero duplication.

## Scope and verification

- Do not alter timeline geometry, scrolling, Now-line behavior, playback, or EPG loading.
- Do not add new regression/contract tests for this small UI adjustment.
- Verify with mobile and desktop builds and record the change in `CHANGELOG.md`.
