# Desktop Download Center Virtualization Design

## Goal

Keep the desktop Downloads screen responsive when a season adds many active, queued, or failed download rows, while preserving the existing desktop card layout and commands.

## Design

- Keep the shared `MainViewModel` and `ContentDownloadService` behavior unchanged.
- Replace the three download-center `ItemsControl` instances with bounded `ListBox` controls using `VirtualizingStackPanel`.
- Retain the current active, queued, and failed card templates, section headers, and command bindings.
- Allow each bounded list to scroll and chain to the containing page, matching the proven mobile download-center behavior.
- Clear transient selection immediately so virtualization does not introduce persistent selected-row styling.

## Verification

- A source-level architecture test requires all three bound collections to use virtualized `ListBox` controls and rejects the old `ItemsControl` declarations.
- Existing download notification and command tests remain green.
- The desktop project must build successfully.

