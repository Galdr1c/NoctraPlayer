# Mobil ayarlar backend denetimi

Tarih: 22 Temmuz 2026

Bu denetim `MobileSettingsView` üzerindeki değiştirilebilir alanların UI bağını, `SettingsViewModel` kalıcılığını ve ayarı gerçekten tüketen servis/platform kodunu birlikte kontrol eder. Mobil ekran otomatik kaydeder; ayrı bir Kaydet düğmesine bağlı değildir.

## Ayarlar

| Ayar | Sonuç | Gerçek davranış |
|---|---|---|
| Tema | Çalışıyor | Seçim anında tema servisine uygulanır ve profile kaydedilir. |
| Uygulama dili | Çalışıyor | Yerelleştirme anında yenilenir ve yeniden açılış için kalıcıdır. Gerçek Android cihazda doğrulandı. |
| User-Agent | Çalışıyor | Kaydedilir; oynatıcı yeniden başlatıldığında Android medya isteklerinde, ayrıca M3U isteklerinde kullanılır. |
| Sonraki bölümü otomatik oynat | Çalışıyor | Bölüm sonu gezinme servisi bu değeri okur. |
| Oynatma kalitesi / veri kullanımı | Çalışıyor | Android ExoPlayer çözünürlük ve bitrate sınırlarına uygular. |
| Arabellek boyutu | Çalışıyor | Android/Core oynatıcı load-control ayarlarına uygulanır. |
| İzleme geçmişini kaydet | Çalışıyor | Kapalıyken geçmiş servisi yeni ilerleme kaydı yazmaz. |
| Arka planda oynatma | Çalışıyor | Android activity ve playback service arka plan yaşam döngüsünde bu ayarı okur. |
| Çıkışta geçmişi temizle | Mobilden kaldırıldı | Güvenilir bir Android “uygulamadan çıkış” olayı olmadığı için masaüstü davranışı mobilde sahte bir ayar olarak gösterilmiyor. Masaüstü ayarı korunur. |
| Altyazıları otomatik etkinleştir | Çalışıyor | Oynatıcı altyazı seçimini bu ayara göre yapar. |
| Tercih edilen altyazı dili | Çalışıyor | Track eşleştirme ve seçim hattı tarafından kullanılır. |
| Tercih edilen ses dili | Çalışıyor | Track eşleştirme ve seçim hattı tarafından kullanılır. |
| Yalnızca Wi-Fi ile indir | Düzeltildi | Film ve dizi bölümü indirmeleri başlatılırken artık masaüstü ağ kartlarını değil Android'in aktif Wi-Fi/Ethernet/Cellular durumunu kullanır. |
| İndirme kalitesi | Mobile uyarlandı | Genel indirici sağlayıcı akışını transcode edemediğinden yanıltıcı “Standart” seçimi kaldırıldı. Film ve bölümler sağlayıcının özgün akışıyla indirilir. |
| İndirme konumu | Mobile uyarlandı | Android uygulamaya özel klasörü pasif ve odaklanamayan bir bilgi alanında gösterilir; geçersiz serbest yol girilemez. |
| İndirme tamamlandı bildirimi | Düzeltildi | Modal diyalog yerine Android bildirim kanalı kullanır. Android 13+ için çalışma zamanı izni istenir; izin reddedilirse Toast geri dönüşü vardır. |
| Kanal yenileme sıklığı | Düzeltildi | Aktif timer kullanır; profil yeniden açıldığında veri zaten gecikmişse tam bir periyot daha beklemeden hemen yeniler. |
| EPG etkin | Düzeltildi | Kapalıyken önceden cache'lenmiş programlar da UI'ya dönmez. |
| EPG yenileme sıklığı | Çalışıyor | Aktif timer ve profil açılışındaki stale kontrolü tarafından kullanılır. |
| EPG saat farkı | Çalışıyor | Program zaman sorgularına uygulanır. |
| Özel EPG URL'leri | Çalışıyor | Ekleme/silme otomatik kaydedilir ve EPG kaynak çözümleyicisi tarafından kullanılır. |
| Geçmiş saklama süresi | Çalışıyor | Geçmiş ekranı yüklenirken eski kayıtlar temizlenir. |

Canlı yayın indirme kapsamına alınmamıştır. Oynatıcı indirme düğmesini `IsLiveContent` için gizler; indirme kayıtları yalnızca `ChannelType.VOD` veya `ChannelType.Series` üretir.

## İşlem düğmeleri

| İşlem | Sonuç | Backend |
|---|---|---|
| Profil seçimine dön | Çalışıyor | Mobil navigation/profile servisine gider. |
| Kanalları şimdi yenile | Çalışıyor | Seçili playlist için provider yenilemesini çağırır. |
| Gizli kategoriyi göster | Çalışıyor | Profil ayarındaki gizli grup listesinden kaldırır ve filtreyi yeniler. |
| EPG'yi şimdi yenile | Çalışıyor | EPG kaynaklarını tekrar indirip işler. |
| Tüm izleme geçmişini temizle | Çalışıyor | Onay sonrasında aktif profil kayıtlarını siler. |
| Güncellemeleri kontrol et | Çalışıyor | Android Google Play update servisine bağlıdır. |
| Promosyon kodu uygula | Çalışıyor | Lisans servisine gönderilir ve lisans durumu yenilenir. |
| Cache temizle | Çalışıyor | Görsel/geçici/EPG cache temizleme servislerini çağırır. |
| Hata bildir | Çalışıyor | Android paylaşım/e-posta intent'i ile tanı raporu hazırlar. |
| Varsayılanlara sıfırla | Çalışıyor | Profil ayarlarını varsayılana döndürür; Android'in yönettiği indirme yolu korunur. |
| Gizlilik ve kullanım koşulları | Çalışıyor | Mobil legal doküman görünümünü açar. |

## Doğrulama

- Otomatik testler: 1.277 testin tamamı geçti.
- Android debug build: başarılı, 0 hata.
- Huawei DBY-W09: APK güncellendi; Settings ekranı gerçek profille açıldı, mobil dışı “çıkışta temizle” kontrolü görünmüyor, kalite seçici yerine özgün kalite açıklaması ve salt-okunur uygulama depolama yolu görünüyor.
- Android profil JSON'ları: dil, ağ/indirme, bildirim ve oynatma ayarlarının kalıcı alanlara yazıldığı backend üzerinden kontrol edildi.

Android işletim sistemi uygulama süreci tamamen kapalıyken uygulama içi timer çalıştırmaz. Kanal ve EPG tarafında doğru mobil karşılık, profil yeniden açıldığında gecikmiş veriyi yakalayan stale kontroldür.
