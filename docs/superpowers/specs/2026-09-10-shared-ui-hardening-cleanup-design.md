# Shared UI hardening and cleanup design

## Scope

Keep the Windows shell as a left navigation rail with the existing minimum
window width. Harden the shared UI migration without changing Android native
playback, PiP, EPG, advertising, downloads, or platform-specific window hosts.

## Design

- The shared theme picker owns one adaptive two-card layout. It uses larger
  balanced cards on desktop and stacks them when the available width is below
  340 logical pixels.
- Shared controls detach ViewModel event handlers on visual detach and restore
  them on visual attach, even when DataContext itself did not change.
- Dynamically created desktop navigation actions bind to localization keys;
  language changes flow through the existing localization source.
- Runtime migration adapters use named XAML anchors. They never infer semantic
  controls from child indexes or the first three matching borders.
- Playback speed and sleep-timer choices retain a visible selected background
  after moving to the shared player sheets.
- A platform UI file may be deleted only after a repository-wide source search
  proves there is no consumer outside the file itself. Tests then target the
  canonical shared component.

## Verification

Source-contract tests cover reattachment, adaptive card geometry, localization
bindings, named anchors, selected sheet states, and deleted legacy files. The
full solution build and both test projects must pass with no warnings.
