# Masaüstü profil seçici görsel uyumluluğu

## Amaç

Mobildeki `39979f9` ve `83823ab` commitleriyle gelen profil seçici görsel güncellemelerini, masaüstünün farklı pencere ve fare düzenini koruyarak `ProfilesWindow` ekranına taşımak.

## Kapsam

- Normal mod başlığında uygulama logosunu merkeze almak ve kalem düğmesini sağda tutmak.
- Yönet modunda geri, ortalanmış başlık, Bitti, Ayarlar ve Kapat düğmelerinin masaüstü penceresine uyumlu hizalanması.
- Profil başlığı ve kart alanı boşluklarını mobildeki görsel hiyerarşiye yaklaştırmak; masaüstünün sabit genişlikli wrap düzenini korumak.
- “Profil Ekle” kartını profil kartıyla aynı kare alanı kullanan, yeni kenarlık/artı ikon görünümüne geçirmek.
- Mevcut profil kartlarının seçim, PIN, silme geri sayımı, hover/pressed animasyonları ve tıklama olaylarını korumak.

## Kapsam dışı

- Profil kurulum formunun mobil dar ekran boşluklarını masaüstüne kopyalamak.
- Profil ViewModel, profil erişim akışı veya veri modelini değiştirmek.
- Ayarlar/Kapat düğmelerini kaldırmak veya mobil dokunmatik davranışı masaüstüne zorlamak.

## Uygulama yaklaşımı

Değişiklik `Noctra.Avalonia/Views/ProfilesWindow.axaml` içindeki mevcut stiller ve yerleşim üzerinde yapılacak. Masaüstü kart ölçüleri ve `WrapPanel` korunacak; yalnızca başlık hizası, içerik boşlukları, ekleme kartı şablonu ve buna bağlı hover/pressed görsel durumları mobil tasarımla uyumlu hale getirilecek.

## Doğrulama

- Profil penceresi için mevcut davranış/markup sözleşme testleri güncellenecek veya eklenecek.
- `Noctra.Tests` tam paketi çalıştırılacak.
- `Noctra.Avalonia` Debug derlemesi alınacak ve XAML derleme hatası/uyarısı kontrol edilecek.
