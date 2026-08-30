# Player-entry immersive fullscreen hardening

## Goal

Android player yüzeyi açılır açılmaz, video henüz loading/buffering durumunda
olsa bile status ve navigation barları gizlenmelidir. Kullanıcının kenardan
swipe etmesiyle sistem çubuklarının geçici görünmesi korunur.

## Approaches considered

1. Yalnız `IsFullScreen` property-change zincirine güvenmek: mevcut davranış;
   Android 15/16 lifecycle ve edge-to-edge geçişlerinde kırılgan.
2. Player girişinde doğrudan, idempotent `SetFullScreenMode(true)` çağrısı:
   seçilen yaklaşım. Küçük, mevcut servisle uyumlu ve loading ekranını kapsıyor.
3. Yeni fullscreen koordinatörü/refactor: şu anki tek çağrı noktası için gereksiz
   kapsam ve regresyon riski.

## Design

- `PlaySelectedChannelAsync` aynı `IPlayerWindowService` örneğini kullanır.
- Sıra: native overlay aktif → `PlayerHost` görünür → ViewModel fullscreen true
  → doğrudan native fullscreen true → chrome state güncellemesi.
- PropertyChanged yolu ve focus/resume `ReapplyImmersiveMode` savunma katmanı
  olarak kalır; çağrılar idempotenttir.
- Player kapanışında `IsFullScreen=false` mevcut property-change yoluyla sistem
  çubuklarını geri getirir.
- Tema duyarlı shell/system-bar ve API 35+ safe-area normalizasyonu değişmez.

## Crash investigation boundary

API 36 emulator playback kapanması bu değişiklikten ayrı incelenir. Temiz
`logcat` kaydında managed `FATAL EXCEPTION`, native `SIGSEGV/SIGABRT`, MediaCodec,
ExoPlayer ve surface olayları ayrıştırılmadan decoder/surface kodu değiştirilmez.

## Verification

- Source contract çağrı sırasını doğrular.
- Yeni x86_64 APK veri silmeden kurulur.
- Live karta basıldığında loading ekranında system bars hemen gizlenir.
- Kenar swipe sonrası çubuklar yalnız geçici görünür.
- Player kapanınca shell tema renkleri ve sistem ikon kontrastı geri gelir.
