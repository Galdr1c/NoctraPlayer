# Third-Party Notices

This file lists the third-party software components distributed with Noctra
(desktop: Windows; mobile: Android), together with their licenses and the
obligations that apply to this distribution. It must be reviewed and updated
whenever dependencies change.

> **Release checklist owner:** verify this file against the actual
> `<PackageReference>` set in all `.csproj` files before every public release.

## Summary

| Component | Version | License | Shipped in |
|---|---|---|---|
| FFmpeg (libavcodec/libavutil/libswresample) | 6.0.1 | **LGPL-2.1-or-later** | Android (`libffmpegJNI.so`) |
| media3 `decoder_ffmpeg` JNI wrapper + extension classes | 1.4.1 | Apache-2.0 | Android (`ffmpeg-extension.jar`, JNI source) |
| Avalonia (+ Desktop/Android/Themes.Fluent/Markup.Xaml.Loader) | 12.1.0 | MIT | Desktop + Mobile |
| Inter font (via Avalonia.Fonts.Inter) | — | SIL OFL-1.1 | Desktop + Mobile |
| Material.Icons.Avalonia / Material Design Icons | 3.0.2 | MIT (code); icons: Pictogrammers Free License | Desktop + Mobile |
| LibVLCSharp | 3.9.5 | **LGPL-2.1-or-later** (commercial available) | Desktop + Core |
| VideoLAN.LibVLC.Windows (libVLC binaries) | 3.0.23 | LGPL-2.1+ core (some GPL plugins), dynamically linked | Desktop |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | All managed projects |
| EF Core (Microsoft.EntityFrameworkCore.Sqlite) | 8.0.2 | MIT | Core |
| SQLitePCLRaw.bundle_e_sqlite3 (e_sqlite3 → SQLite) | 3.0.3 | Apache-2.0 (bundle); SQLite is public domain | Core + Mobile |
| Microsoft.Extensions.* (DI/Logging/Caching) | 8.0.x | MIT | Core |
| System.Security.Cryptography.ProtectedData | 8.0.0 | MIT | Core |
| .NET runtime / Mono runtime | net8/net10 | MIT | All |
| Xamarin.AndroidX.* bindings (Media3 ExoPlayer/HLS/DASH/SS/RTSP, Session, SplashScreen) | 1.4.1.1 etc. | Apache-2.0 (AndroidX); bindings MIT | Android |
| Xamarin.Google.Android.Play.Review | 2.x | Apache-2.0 | Android |
| Xamarin.Android.Google.BillingClient | 9.1.0.1 | Apache-2.0 | Android |
| Xamarin.GooglePlayServices.Ads (Google Mobile Ads SDK) | 125.4.0.2 | Google Play Services SDK license (Apache-2.0-based terms) | Android |
| Xamarin.Google.UserMessagingPlatform (UMP) | 4.0.0.3 | Apache-2.0 | Android |
| Huawei HMS Ads / AdsConsent kits | 3.4.67.302 | Huawei HMS Core SDK Terms | Android (HMS devices) |
| Google.Apis.Auth | 1.68.0 | Apache-2.0 | Billing.Api (server-side, not shipped in apps) |
| DotNetEnv | 3.1.1 | MIT | Desktop (dev/.env only) |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | MIT | Desktop (toast notifications) |
| HotAvalonia | 3.x | MIT | Debug builds only |

## FFmpeg — LGPL compliance notes (Android)

The vendored `Noctra.Android/libs/ffmpeg/<abi>/libffmpegJNI.so` contains a
**statically linked** FFmpeg 6.0.1 build. The build configuration was verified
during compilation:

```
License: LGPL version 2.1 or later   (arm64-v8a and x86_64 alike)
```

* Built with `--disable-everything` plus only these **native** FFmpeg decoders:
  aac, mp3, ac3, eac3, truehd, dca, vorbis, opus, amrnb, amrwb, flac, alac,
  pcm_mulaw, pcm_alaw, h264, hevc.
* No GPL-only components (`--enable-gpl` libraries such as libx264/libx265),
  no `--enable-nonfree`, no external libraries are linked — therefore the
  binary is **LGPL-2.1-or-later**, not GPL.
* avformat/swscale/postproc/avfilter are disabled; only decoding paths ship.

Because the linkage is static inside one shared object, LGPL §4 applies:
users must be able to modify FFmpeg and relink. Compliance measures for this
repository:

1. The exact build recipe (media3 `decoder_ffmpeg` @1.4.1 JNI source +
   FFmpeg n6.0.1 + configure flags + decoder list) is published as
   [`tools/android/build-ffmpeg-16k.sh`](tools/android/build-ffmpeg-16k.sh).
2. The unmodified upstream sources used (FFmpeg 6.0.1 tarball,
   androidx/media tag 1.4.1) are publicly downloadable from their official
   locations referenced by that script.
3. **Before public distribution:** provide a written offer (or direct download)
   for the complete corresponding object files / relinkable artifacts of
   `libffmpegJNI.so`. Concretely: publish the unstripped `.so` and/or the
   compiled object archives next to the release, or state an e-mail contact
   that fulfils requests for three years (LGPL §6). Suggested contact to fill
   in before release: `kynora.studio@gmail.com`.

## libVLC / LibVLCSharp — desktop notes

Desktop playback links **dynamically** against `VideoLAN.LibVLC.Windows`
(libVLC 3.x). The libVLC core (`libvlc`/`libvlccore`) is LGPL-2.1+; some
VLC plugin modules in that package are GPL-licensed but are consumed as
separately-loaded plugins of the unmodified library, which keeps the Noctra
application code unaffected. Do not statically link VLC modules into Noctra
binaries. LibVLCSharp itself is LGPL-2.1-or-later (dual-licensed with a paid
commercial option from Videolabs).

## Trademarks

Google Play, Google Mobile Ads, Android, ExoPlayer and Media3 are trademarks
of Google LLC; Huawei and HMS are trademarks of Huawei Technologies Co., Ltd.;
VLC and libVLC are trademarks of VideoLAN; FFmpeg is a trademark of Fabrice
Bellard (originally). Their use here identifies interoperability only and does
not imply endorsement.
