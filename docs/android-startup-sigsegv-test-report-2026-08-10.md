# Android Başlangıç SIGSEGV — Test Raporu (2026-08-10)

## 1. Problem

Huawei DBY-W09NM (Android 12, HarmonyOS tabanlı kernel 4.19.157-perf+) üzerinde
uygulama başlangıcında aralıklı SIGSEGV.

- Runtime: .NET 10.0.9, Mono (`libmonosgen-2.0.so`, BuildId `202aa6dd`)
- Tüm crash'lerde aynı runtime BuildId — tek sürüm sabit

## 2. Crash imzaları

Cihaz dropbox geçmişinden toplam **11 SIGSEGV** + 1 ANR (07.08):

| İmza | Sayı | Detay |
|---|---|---|
| `.NET TP Worker` + `mono_runtime_invoke_checked+140`, addr `0x0` | **9** | null pointer dereference, process uptime **0s** (başlangıç anında) |
| Ana thread (`o.kynora.noctra`), addr `0x28` | 1 | farklı yapıda, seyrek |
| Ana thread, addr `0x600000000` | 1 | farklı yapıda, seyrek |

Dominant imza (9/11) tüm denemelerde birebir aynı: aynı frame, aynı adres, aynı
libmonosgen BuildId.

## 3. Hipotez testleri

| Hipotez | Test | Sonuç |
|---|---|---|
| Null-check optimizasyonu hatası | `MONO_DEBUG=explicit-null-checks` | **Çürütüldü** — crash tekrarladı |
| HotAvalonia (modifiable assemblies + interpreter) | `-p:HotAvalonia=false -p:HotAvaloniaAutoEnable=false`; APK doğrulandı: `HotAvalonia.InjectionType=none`, `DOTNET_MODIFIABLE_ASSEMBLIES` yok, HotAvalonia.Monitor paketlenmedi | **Çürütüldü** — 15 soğuk başlatmada 1. denemede aynı imzayla crash |
| Assembly yükleme / JIT path race'i | Debug vs Release karşılaştırması | **Destekleniyor** (aşağıda) |

## 4. Soğuk başlatma testleri

Yöntem: `am force-stop` → `am start -W` → 22 sn bekleme → `pidof` + logcat crash taraması.
Öncesinde temiz uninstall + `bin`/`obj` temizliği + temiz rebuild.

| Build | Sonuç |
|---|---|
| Debug (HotAvalonia kapalı, temiz kurulum, `EmbedAssembliesIntoApk=true`) | **1/15 crash** (ilk başlatmada, aynı imza) |
| Release (trim + assembly sıkıştırma, arm64-only, AOT'suz) | **25/25 başarılı** — UI'a ulaştı (mResumedActivity doğrulandı) |

Not: `EmbedAssembliesIntoApk` her iki config'de de zaten `true` idi;
`AndroidEnableProfiledAot` her iki config'de `false`.

## 5. Sonuç

- Crash **uygulama mantığı değil, Mono runtime başlangıç race'i**:
  TP worker üzerinde `mono_runtime_invoke_checked` null deref, uptime 0s.
- HotAvalonia ve Debug'e özgü modifiable-assembly ayarları tetikleyici değil.
- Release (trimmer + assembly compression + tek ABI) crash'i ortadan
  kaldırıyor gibi: Debug 1/15 vs Release 25/25.
- Örneklem küçük; Release'te ~%7'lik bir oranı ekarte etmek için ~50+ soğuk
  başlatma gerekir.

## 6. Öneriler

1. Bu cihaz için **Release** kullan (cihazda Release kurulu, çalışır durumda).
2. Debug stabilitesi istenirse: Debug'da `AndroidEnableProfiledAot=true` ile
   A/B test veya daha yeni .NET 10 patch sürümüyle test.
3. Cihaz özgüllüğünü doğrulamak için farklı cihaz/emülatör testi.

## 7. Veri kaybı notu

Uninstall öncesi alınan backup (tar, 529MB) geri yüklenemedi. Nedeni:
PowerShell `adb exec-out ... > file` yönlendirmesi binary çıktıyı UTF-16LE + CP857
üzerinden metne çevirdi; CP857 eşlemesi bijective olmadığından (byte 0xD5)
geri döndürülemez. Cihazdaki 258MB'lık DB / profil verisi sıfırlandı; tüm
testler boş DB ile yapıldı.

Gelecek backup için: binary-safe yöntem (örn. `adb exec-out` çıktısını
PowerShell'de `Set-Content -AsByteStream` ile değil, doğrudan dosyaya yönlendiren
bir araç / `cmd` üzerinden `> ` kullanımı veya `adb pull`).
