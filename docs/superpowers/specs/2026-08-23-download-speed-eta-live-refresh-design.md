# Download Speed and ETA Live Refresh Design

## Problem

The downloader persists changing `SpeedBytesPerSecond` and
`EstimatedSecondsRemaining` values, and the shared `MainViewModel` copies those
values into the realized `DownloadItem`. Desktop and mobile cards nevertheless
bind the whole item (`Binding .`) through converters. That object binding is
evaluated when the row/item is first created but is not reliably re-evaluated
for later named property changes, so the first visible speed and ETA remain
frozen while bytes and progress continue to update.

## Decision

Bind the desktop and mobile speed labels directly to
`SpeedBytesPerSecond`, and bind the ETA labels directly to
`EstimatedSecondsRemaining`. Update both platform converters to accept their
numeric property value instead of a complete `DownloadItem`.

This keeps the existing localized display formats, preserves row identity and
virtualization, and makes Avalonia subscribe to the exact properties that
change on every download snapshot.

## Data Flow

1. `ContentDownloadService` calculates and persists speed and ETA.
2. `MainViewModel.ApplyDownloadSnapshot` updates the realized `DownloadItem`.
3. `DownloadItem` raises the named property notification.
4. The direct property binding re-runs its converter.
5. Desktop and mobile labels display the latest localized value.

## Empty and Invalid Values

- Speed values that are missing, zero, negative, or not numeric render `-`.
- ETA values that are missing, zero, negative, or not numeric render an empty
  string, preserving the current UI behavior.

## Regression Coverage

- Assert both XAML views bind the exact numeric properties rather than
  `Binding .` for speed and ETA.
- Assert the existing `DownloadItem` named notifications remain present.
- Run the focused tests, the complete test suite, and both Android and desktop
  Debug builds.

## Scope

No download cadence, database schema, queue behavior, card recreation, or
localization key changes are included.
