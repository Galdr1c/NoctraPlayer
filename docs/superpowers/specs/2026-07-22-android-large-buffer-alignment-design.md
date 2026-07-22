# Android Large Buffer Alignment Design

## Scope

Align the Android `Large` video buffer preset with the existing settings label and desktop behavior by changing only its minimum buffer duration from 15 seconds to 10 seconds.

## Preserved behavior

- Keep the Android maximum buffer at 60 seconds.
- Keep playback-start buffering at 1.5 seconds.
- Keep rebuffer recovery at 5 seconds.
- Keep the `Small` and `Normal` Android presets unchanged.
- Keep the shared settings model, mobile labels, and desktop values unchanged.

## Verification

- Add a focused source-level regression guard for the Android Media3 mapping.
- Run the focused test before and after the implementation change.
- Run the full test suite and build the Android project.
