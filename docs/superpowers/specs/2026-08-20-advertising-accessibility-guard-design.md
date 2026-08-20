# Android banner accessibility guard

## Finding

The persistent AdMob banner introduced after commit `12d0d906` embeds a native `AdView` through Avalonia `NativeControlHost`. Avalonia Android 12.1's interop automation peer throws `NotImplementedException` when an accessibility service enumerates child peers.

## Guard

`BannerNativeControlHost` returns Avalonia's `NoneAutomationPeer`. The native AdView remains attached and owns its Android-side behavior, while Avalonia does not attempt to traverse an unsupported embedded automation tree.

## Acceptance

- Banner host source contract requires `NoneAutomationPeer(this)`.
- Android arm64 build succeeds.
- Two device `uiautomator dump` operations complete with the process alive and no `NotImplementedException`/fatal process entry.
