# Mobile User-Agent Placeholder Design

## Scope

Change only the User-Agent `TextBox` placeholder in `MobileSettingsView.axaml` to:

`VLC/3.0.18 LibVLC/3.0.18`

The XML entity renders as the requested Turkish `Ornek` abbreviation with an uppercase O-umlaut.

Do not add helper text and do not change the saved value, Android fallback User-Agent, desktop UI, or networking behavior.

## Verification

Add a mobile guard assertion for the exact placeholder, run the focused test, then run the full test suite and Android build.
