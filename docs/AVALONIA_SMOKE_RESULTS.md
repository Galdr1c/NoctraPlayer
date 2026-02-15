# Avalonia Smoke Sonuclari

Bu dosya CLI seviyesinde calistirilan dogrulama adimlarini ve sonucunu tutar.

## Build Dogrulama

1. `dotnet build Noctra.Avalonia/Noctra.Avalonia.csproj --no-restore`
   - Sonuc: Basarili (`0` hata, `0` uyari)
   - Not: NuGet kaynak kesintisi sonrasinda once asagidaki komutla cache restore yapildi:
   - `dotnet restore Noctra.Avalonia/Noctra.Avalonia.csproj --ignore-failed-sources`

2. `dotnet build Noctra.Core/Noctra.Core.csproj --no-restore`
   - Sonuc: Basarili (`0` hata, `0` uyari)

3. `dotnet build Noctra.Tests/Noctra.Tests.csproj --no-restore`
   - Sonuc: Basarili (`0` hata, `0` uyari)

4. `dotnet build Noctra/Noctra.csproj --no-restore`
   - Sonuc: Basarili (`0` hata, `6` uyari, mevcut WPF dosyalarinda)
   - Not: Uyarilar Noctra (WPF) tarafinda ve Avalonia gecis degisikliklerinden bagimsiz.

## Test Dogrulama

1. `dotnet test Noctra.Tests/Noctra.Tests.csproj --no-build`
   - Sonuc: Basarili (`13/13` test gecti)

## Paketleme Denemesi

1. `dotnet publish Noctra.Avalonia/Noctra.Avalonia.csproj -c Release -r win-x64 --self-contained false --no-restore`
   - Sonuc: Basarili (publish cikti dizini olustu)

2. `dotnet restore Noctra.Avalonia/Noctra.Avalonia.csproj -r win-x64`
   - Sonuc: Basarili (yukseltilmis izin ile)

3. `dotnet publish Noctra.Avalonia/Noctra.Avalonia.csproj -c Release -r win-x64 /p:PublishSingleFile=true --self-contained false --no-restore`
   - Sonuc: Basarili (single-file publish cikti dizini olustu)

Publish cikti:
- `Noctra.Avalonia/bin/Release/net8.0/win-x64/publish/`
