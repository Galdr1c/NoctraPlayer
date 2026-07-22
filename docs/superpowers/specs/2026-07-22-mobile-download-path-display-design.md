# Mobil indirme konumu pasif gösterim tasarımı

Tarih: 22 Temmuz 2026

## Amaç

Mobil ayarlar ekranındaki sabit Android indirme yolunun düzenlenebilir bir alan gibi odaklanmasını, caret ve metin seçim tutamaçları göstermesini engellemek.

## Kapsam

- İndirme konumu değiştirilemeyecek.
- Android uygulamaya özel mevcut indirme dizini ve backend davranışı aynen korunacak.
- Mevcut yol kullanıcıya bilgi olarak gösterilmeye devam edecek.
- Masaüstü klasör seçimi etkilenmeyecek.

## UI davranışı

`MobileSettingsView` içindeki salt-okunur `TextBox`, tema ile uyumlu bir `Border` içindeki çok satırlı `TextBlock` ile değiştirilecek. Gösterge:

- klavye odağı alamaz;
- caret veya metin seçim tutamaçları üretmez;
- dokunma sırasında vurgu/focus çerçevesi göstermez;
- uzun yolu satıra böler;
- üzerinden başlayan dikey kaydırma hareketinin sayfaya ulaşmasına izin verir.

## Testler

- Mobil release-guard testi indirme yolunun `TextBox` olmadığını ve pasif `TextBlock` olduğunu doğrulayacak.
- Ayarlar testleri çalıştırılacak.
- Android proje derlemesi yapılacak.
- DBY-W09 üzerinde yol alanına dokunma/uzun basma ve alan üzerinden sayfa kaydırma kontrol edilecek.

## Kapsam dışı

- Klasör seçici
- SAF/MediaStore entegrasyonu
- Mevcut indirmeleri taşıma
- İndirme dizini backend değişikliği
