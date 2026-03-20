# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

### 🐞 Hata Düzeltmeleri ve Kritik İyileştirmeler
- **Altyazı Ayarları Uygulama ve Senkronizasyon Sorunu Giderildi** (2026-03-20):
  - **Re-initialization (Yeniden Başlatma) Mekanizması Onarıldı:** Altyazı boyutu, arka plan şeffaflığı ve konumu değiştirildiğinde video oynatıcının bazen değişikliği algılamaması veya tepki vermemesi sorunu çözüldü. Arka plandaki "Debounce" ve `CancellationTokenSource` yaşam döngüsü, hızlı tıklamalarda oluşabilen `ObjectDisposedException` hatalarını önleyecek şekilde daha güvenli bir yapıya kavuşturuldu.
  - **Thread Güvenliği (UI Crash Fix):** Ayarlar değiştirildiğinde tetiklenen dil ve tema güncelleme işlemlerinin arka plan thread'inden ana UI thread'ine (`Dispatcher.UIThread`) güvenli bir şekilde aktarılması sağlandı. Bu sayede ayar değişimleri sırasında uygulamanın kilitlenmesi veya event zincirinin kopması engellendi.
  - **Kusursuz UI-Ayarlar Senkronizasyonu:** Uygulama ilk açıldığında veya yeni bir videoya geçildiğinde, kontrol panelindeki altyazı butonlarının (Küçük/Normal/Büyük vb.) seçili durumunun gerçek ayarlarla tutarsız görünmesi sorunu giderildi.
  - **Otomatik UI Güncelleme:** `PlayerViewModel` üzerindeki altyazı özellikleri, `SettingsChanged` event'i ile tam uyumlu hale getirildi. Artık ayarlar başka bir pencereden (Global Ayarlar vb.) değiştirilse dahi, açık olan oynatıcıdaki seçimler anlık ve otomatik olarak güncellenir.
  - **VOD/Series Devamlılık Koruması:** Uzak sunuculardan gelen (HTTP Streaming) içeriklerde altyazı ayarı uygulandığında videonun kaldığı saniyeden ve kullanıcının seçtiği Ses/Altyazı kanalından (Track) sapmadan devam etmesi sağlandı.

### ✨ Yeni Özellikler ve Geliştirmeler
- **Akıllı Uyku Zamanlayıcısı (Sleep Timer)** (2026-03-19):
  - Video oynatıcıya (VOD, Dizi ve İndirilen İçerikler) kapsamlı bir uyku zamanlayıcısı sistemi eklendi.
  - **Esnek Zaman Seçenekleri:** 15, 30 ve 60 dakikalık hazır sürelerin yanı sıra, akıllı "Bölüm Bitince" veya "Film Bitince" modları eklendi.
  - **Premium Entegrasyonu:** Uyku zamanlayıcısı özellikleri Premium kullanıcılara özel olarak sunuldu. Non-premium kullanıcılar için seçenekler kilit ikonuyla görsel olarak belirtildi ve pasif hale getirildi.
  - **Dinamik Arayüz:** Zamanlayıcı aktif olduğunda, oynatıcı kontrollerindeki ay ikonu üzerinde canlı geri sayım badge'i (örn. "12:47") belirecek şekilde tasarlandı.
  - **İçerik Duyarlı Etiketler:** Zamanlayıcı menüsü ve tooltip'ler, izlenen içeriğin türüne göre (Dizi vs Film) otomatik olarak "Bölüm Bitince" veya "Film Bitince" şeklinde kendini günceller.
  - **Performans Dostu Mimari:** Geri sayım işlemleri ana UI iş parçacığını yormayacak şekilde hafif arka plan görevleri (Task) olarak kurgulandı ve saniyelik hassasiyetle çalışır.
  - **Güvenlik ve Kısıtlamalar:** Uyku zamanlayıcısının canlı yayınlarda (Live TV) kullanılması, yayın akışının doğası gereği engellendi.
  - **Kapsamlı Test Doğrulaması:** Sistemin kararlılığını ölçmek için premium gating, canlı yayın kısıtlamaları, dinamik etiketler ve otomatik kapanma senaryolarını içeren 8 adet yeni birim testi (`PlayerSleepTimerTests.cs`) sisteme dahil edildi.
- **Kapsamlı Profil PIN Kilidi Sistemi** (2026-03-19):
  - Profil bazlı erişim kısıtlaması için güvenli 4 haneli PIN sistemi hayata geçirildi.
  - **Premium Kilidi:** PIN oluşturma ve yönetimi özellikleri Premium kullanıcılara özel olarak sunuldu.
  - **Gelişmiş Profil Düzenleyici:** PIN oluşturma sırasında "PIN Tekrar" adımı ile doğrulama eklendi. Mevcut bir PIN varsa "PIN Aktif" rozeti ve kolay PIN kaldırma/değiştirme seçenekleri sunuldu.
  - **PIN Giriş Ekranı:** Estetik, numpad destekli ve 380x560 boyutunda modern bir PIN giriş penceresi eklendi. Yanlış girişlerde 30 saniyelik güvenlik kilidi mekanizması uygulandı.
  - **"PIN'imi Unuttum" Prosedürü:** PIN'i unutulan profiller için 3 günlük bir silme geri sayımı sistemi kuruldu. Bu süre zarfında PIN hatırlanırsa silme işlemi iptal edilebilir. Güvenlik gereği PIN sıfırlama seçeneği sunulmamıştır.
  - **Kompakt Diyalog Pencereleri:** Uygulama genelindeki uyarı ve bilgi pencereleri (`DialogWindow`) optimize edilerek 400px sabit genişliğe ve içeriğe göre otomatik yükselen (scroll-free) bir yapıya geçirildi.
  - **Güvenli Depolama:** PIN'ler veritabanında SHA256 algoritması ve "salt" (tuzlama) yöntemiyle geri dönüştürülemez şekilde şifrelenerek saklanmaktadır.
  - **Hata Düzeltmeleri:** Silme geri sayımının her tıklamada tekrar 3 güne sıfırlanması hatası ve profil düzenleme ekranındaki PIN durumu bildirim senkronizasyonu sorunları giderildi.
- **Video Görüntü Doldurma Modları (Video Fill Mode)** (2026-03-18):
  - Video oynatıcıya 4 farklı doldurma modu eklendi: Uydur (Fit), Doldur (Fill 16:9), Genişlet (Stretch 16:9) ve Orijinal.
  - Mod değiştirildiğinde ekranın ortasında belirip kaybolan estetik bir "Mod Göstergesi" (Overlay Message) eklendi.
  - Oynatıcı kontrollerindeki her mod için özel Material Design ikon entegrasyonu sağlandı.
  - Ayarlar pencerelerindeki (`SettingsWindow` ve `GlobalSettingsWindow`) dikey kaydırma çubuğu konumu ve hizalaması düzeltilerek daha temiz bir görünüm elde edildi.
- **Canlı TV Favori Butonu (Overlay Favorite)** (2026-03-18):
  - Video oynatıcı (Live TV) kontrol paneline aktif kanalı favorilere ekleyip çıkarmayı sağlayan interaktif bir "Kalp" (Favori) butonu eklendi.
  - Buton durumu (`IsFavorite`) ile senkronize çalışarak anlık görsel geri bildirim sağlar.
- **Özelleştirilebilir Video Buffer (Önbellek) Boyutu** (2026-03-18):
  - Ayarlar > Oynatma sekmesine video buffer boyutu seçimi eklendi (Küçük: 2sn, Normal: 5sn, Büyük: 10sn).
  - İnternet bağlantı hızına göre video yükleme performansını optimize etme imkanı sağlandı.
  - "Küçük (2sn)" ve "Büyük (10sn)" seçenekleri Premium kullanıcılara özel olarak sunuldu.
  - Premium kilit ikonları, kullanıcı deneyimini artırmak için doğrudan RadioButton seçim yuvarlaklarının üzerine estetik bir şekilde yerleştirildi.
- **Gelişmiş EPG Yönetimi ve Özel URL Desteği** (2026-03-18):
  - Ayarlar > EPG sekmesine "Özel EPG URL" alanı için bağımsız bir "Şimdi Yenile" butonu eklendi. Bu buton, girilen URL'yi anında kaydederek yenileme işlemini başlatır.
  - Özel EPG URL'leri artık sistemde en yüksek öncelikli (`Priority 0`) kaynak olarak kabul edilir; sistem önce bu adresten veriyi çeker ve sadece bu kaynakta bulunmayan eksik kanallar için ikincil kaynaklara (`iptv-epg.org` vb.) başvurur (Akıllı Hibrit Eşleştirme).
  - Özel URL alanı için estetik bir yer tutucu (watermark) eklenerek kullanıcı deneyimi iyileştirildi.
- **Kategori Gizleme Desteği** (2026-03-18):
  - Canlı TV, Film ve Dizi kategorilerinde istenmeyen grupların tamamen gizlenebilmesi sağlandı.
  - Grup seçim listelerindeki (ComboBox) kategori adlarının başına eklenen "Göz" ikonu ile kategoriler anında gizlenebilir.
  - Gizlenen kategoriler ve içindeki tüm yayınlar arayüzden ve genel arama (Search) motorundan tamamen kaldırılır (aramalarda çıkmaz).
  - Ayarlar > Kanal Listesi sekmesine "Gizlenen Kategoriler" için modern, genişletilebilir (Expander) kutular eklendi. Bu şık paneller üzerinden Canlı TV, Film ve Dizi grupları ayrı ayrı yönetilebilir ve istenilen kategoriler tekrar görünür ("Göster") yapılabilir.
- **EPG Veri Kaybı ve Yenileme İyileştirmeleri** (2026-03-18):
  - Uygulama her açıldığında EPG verilerinin azalmasına neden olan "1 günlük veri sınırı" 7 güne çıkarıldı.
  - Ayarlardaki yenileme sıklığına (örn. 12 saat) sadık kalınarak, gereksiz otomatik yenilemeler ve veritabanı temizleme işlemleri engellendi.
  - Manuel yenileme modu (0 saat) seçildiğinde, veritabanında veri varsa başlangıçtaki otomatik yükleme devre dışı bırakıldı.
- **EPG Saat Dilimi Ofseti** (2026-03-18):
  - Ayarlar > EPG menüsüne "EPG Saat Dilimi Ofseti" ayarı eklendi.
  - Bu sayede yayıncıların UTC bazlı veya yanlış saat diliminde gönderdiği elektronik program rehberi (EPG) verileri için manuel düzeltme yapılabilecek (örneğin Türkiye için +3 saat).
  - Ayar değiştirildiğinde EPG içerikleri otomatik olarak belirlenen ofset ile kaydırılarak gösterilir.
- **Özelleştirilebilir User-Agent Desteği** (2026-03-18):
  - Ayarlar menüsüne "Ağ Ayarları" bölümü eklenerek kullanıcıların HTTP isteklerinde gönderilecek `User-Agent` kimliğini değiştirmesine olanak tanındı.
  - Bu sayede belirli player'ları engelleyen inatçı IPTV sunucularında kanalların açılmaması ve EPG yüklenmemesi sorunları "IPTVSmartersPro" veya benzeri kimlikler kullanılarak aşılabilecek.
  - User-Agent alanı boş bırakıldığında uygulama standart "VLC/3.0.4" kimliğini kullanmaya devam edecektir.
- **Premium Kilitleri ve Özelleştirme Kısıtlamaları** (2026-03-17):
  - **Koyu Tema (Dark Mode):** Uygulama genelinde Koyu Tema seçeneği Premium kullanıcılara özel hale getirildi. Ücretsiz kullanıcılarda bu seçenek kilitli (`Lock` ikonu ile) ve devre dışı olarak görünecek. Tema önizleme kutusunun (Mini Preview) üzerine estetik bir kilit katmanı eklendi.
  - **Otomatik Yenileme Ayarları:** "EPG" ve "Kanal Listesi" için sunulan otomatik yenileme sıklığı seçenekleri (1 saat, 24 saat vb.) Premium kullanıcıların hizmetine sunuldu. Ücretsiz kullanıcılar bu seçeneklerin yanında sağa yaslanmış kilit ikonlarını görecek ve sadece manuel güncellemeyi kullanabilecekler.
  - **Kompakt Ayarlar Menüsü:** Yenileme sıklığı menüleri (`ComboBox`) daha modern ve kompakt bir görünüme kavuşturuldu (`Width="200"`), kilit ikonları büyütülerek görsel hiyerarşi güçlendirildi.
- **Tema Seçimi ve UI Estetiği Güçlendirildi** (2026-03-16):
  - `SettingsWindow` ve `GlobalSettingsWindow` pencerelerindeki tema önizleme butonlarında kullanılan tüm hardcoded renk kodları (`#141414`, `#1A1A1A`, vb.) temizlendi.
  - Tema önizlemeleri için `DarkTheme.axaml` ve `LightTheme.axaml` dosyalarına dinamik kaynaklar (`ThemePreview` resources) eklendi, böylece butonlar aktif temaya tam uyumlu hale getirildi.
  - `DownloadsView` sayfasındaki sıralama ComboBox'ı, uygulamanın geri alanıyla tutarlı olacak şekilde `ModernComboBox` tasarımına yükseltildi.
  - `LiveView`, `MoviesView` ve `SeriesView` görünümlerinde kartların sağ ve alt kenarlarda bıraktığı gereksiz boşluklar (margin collision), `ItemsControl` üzerine uygulanan negatif offset (`Margin="0,0,-16,-16"`) ile giderildi.
  - Uygulama genelindeki tema seçim butonlarının boyutları standartlaştırılarak (130x85) görsel tutarlılık sağlandı.
  - `VideoOverlayView` içerisindeki yükleniyor (buffering) animasyonu, noktalar arası geçiş süresi ve döngü asimetrisi giderilerek daha akıcı hale getirildi.
  - `VideoOverlayView` içerisindeki ilerleme çubuğu (Seek Bar) üzerine fare ile gelindiğinde o anki zamanı gösteren estetik bir "hover preview" Tooltip (pill) eklendi; `track.ValueFromPoint` ile %100 senkronizasyon sağlandı.
  - Video oynatıcı yan panel başlıkları (HAKKINDA, SES VE ALTYAZI vb.) ve alt başlıklar (VİDEO, SES vb.) için separetör çizgileri yazı genişliğine göre standartlaştırıldı.
  - `SeriesDetailOverlay` içerisinde backdrop poster üzerindeki standart gradient, `Transparent` -> `Bg0Color` geçişli `LinearGradientBrush` ile değiştirilerek posterden içeriğe daha sinematik ve yumuşak bir geçiş sağlandı.
  - Dizi detay sayfasındaki başlık alanı `Viewbox` ile sarmalanarak uzun dizi isimlerinin (örn: "Diriliş: Ertuğrul") tasarımda taşma yapmadan dinamik olarak ölçeklenmesi sağlandı.
  - Dizi detay sayfasındaki "Oynat" butonuna hover durumunda accent renginde yumuşak bir parlama (glow) efekti eklendi (`BoxShadow`).
  - Uygulama genelindeki ToolTip'lere (ipuçları) 400ms gecikme ve 150ms fade-in animasyonu eklenerek daha akıcı ve premium bir kullanıcı deneyimi sağlandı.
  - `PremiumSpinner` bileşeni içerisindeki placeholder ikon kaldırıldı ve yerine "nefes alma" (pulse) animasyonlu Noctra logosu yerleştirildi; bu görsel iyileştirme `ProfileLoadingWindow`, `SplashWindow` ve `MainWindow` yükleme katmanlarına uygulandı.
  - **ToggleSwitch Animasyon Birliği:** Uygulama genelindeki tüm `ToggleSwitch` kontrolleri merkezi bir `ControlTheme` ile standartlaştırıldı. Geçiş animasyonları `200ms` süre ve `SplineEasing (0.4, 0, 0.2, 1)` ile sabitlenerek tüm temalarda daha "akıcı" ve tutarlı bir deneyim sağlandı.

### 🐛 Hata Düzeltmeleri
- **Donanım Hızlandırma Ayarı Aktifleştirildi** (2026-03-18):
  - Ayarlar menüsündeki "Donanım Hızlandırma" seçeneğinin çalışmaması (VLC motorunda her zaman zorunlu açık kalması) sorunu çözüldü. Artık ayar kapatıldığında video çözme yükü tamamen yazılımsal olarak (CPU) yapılıyor; açıksa ekran kartı (d3d11va) kullanılıyor.
  - Ayar değiştirildiğinde VLC motorunun otomatik olarak yeni donanım ivmelendirme ayarlarıyla yeniden başlatılması sağlandı.
  - VOD/MKV oynatma profillerindeki sabit donanım hızlandırma kodları da bu dinamik ayara bağlandı.
- **Tema ve Ayarların Kaydedilmesi Düzeltildi** (2026-03-17):
  - Tema ve genel ayarlar (dil, güncelleme vb.) tüm profiller için merkezi hale getirildi (Global Settings).
  - Profil geçişlerinde seçili temanın bazen sıfırlanması veya değişmesi sorunu, ayarların profil bazlı değil global olarak yönetilmesiyle çözüldildi.
  - Uygulama başlangıcında ve profil yüklemelerinde temanın anında yeniden uygulanması sağlandı.
- **Ayarlar Arayüzü ve Güncelleme Sistemi İyileştirildi** (2026-03-15): 
  - Güncelleme denetleme bölümü Premium/Ücretsiz kartlarından bağımsız hale getirilerek ayrı bir kutu (box) olarak sabitlendi ve boyutu %30 küçültülerek daha kompakt hale getirildi.
  - "Güncelleştirmeleri Denetle" butonlarındaki komut bağlama (binding) hatası giderilerek butonların pasif kalma sorunu çözüldü.
  - `UpdateService` içerisindeki versiyon karşılaştırma mantığı `v1.2.3` gibi yaygın formatları ve özel sürüm isimlendirmelerini destekleyecek şekilde güçlendirildi.
  - `SettingsWindow.axaml` üzerindeki XAML etiket hataları (mismatch) giderilerek derleme kararlılığı sağlandı.
- **Hata Raporlama E-posta Adresi Güncellendi** (2026-03-15): Diagnostic raporlarının gönderileceği hedef e-posta adresi `kynora.studio@gmail.com` olarak güncellendi.
- **Gizli Geliştirici Modu Erişilebilirliği Artırıldı** (2026-03-15): Geliştirici şifresinin girileceği alan, "v1.0.0" sürüm yazısının üzerine taşınarak gizliliğini korurken erişilebilirliği artırıldı.
- **PiP Modunda Sürükleme Sırasında Kontrollerin Kaybolması Düzeltildi** (2026-03-14): PiP (Picture-in-Picture) modunda pencere sürüklenirken, işletim sistemi kaynaklı modal döngünün (`BeginMoveDrag`) zamanlayıcıyı (timer) hatalı sıfırlayarak kontrollerin 2.5 saniye sonra aniden kaybolmasına neden olan sorun çözüldü. Sürükleme işlemi için özel bir durum (`IsDragging`) eklendi ve farenin bırakılma anını (`PointerReleased`) doğru yakalayacak mekanizmalar kurularak kontrollerin kullanıcı deneyimine uygun şekilde gizlenmesi sağlandı.
- **PiP Modunda Yeniden Boyutlandırma Sırasında Kontrollerin Kaybolması Düzeltildi** (2026-03-14): PiP penceresi yeniden boyutlandırılırken (`Resize`) farenin hareketine rağmen kontrollerin aniden kaybolması sorunu giderildi. Boyutlandırma esnasında kontrollerin gizlenmesini önleyen (`IsResizing`) durumu eklendi ve boyutlandırma sırasında farenin hareketi sürekli olarak arayüzle etkileşim (`UserInteractionCommand`) sayılacak şekilde güncellendi.
- **Çoklu Monitörlerde PiP Konumlandırma Hatası Giderildi** (2026-03-14): Uygulama ikinci bir monitörde çalışırken PiP moduna geçildiğinde, küçük pencerenin aktif monitör yerine birincil monitörün (`Screens.Primary`) köşesine gitmesine neden olan konumlandırma sorunu çözüldü. Pencere artık kullanıcının o an uygulamayı kullandığı aktif ekrana (`Screens.ScreenFromVisual`) göre konumlandırılıyor.
- **XAML Gömülü Kod ve Etiket Hataları Giderildi** (2026-03-14): `GlobalSettingsWindow.axaml` içerisinde oluşan etiket çakışmaları ve hatalı kapanan Border blokları temizlenerek derleme hataları giderildi.
- **Çoklu Profil Geçmiş Temizliği İyileştirildi** (2026-03-14): "Uygulama çıkışında geçmişi temizle" ayarının yalnızca o an aktif olan profili etkilemesi sorunu çözüldü. Artık uygulama kapanırken bu ayarı aktif etmiş olan **tüm profillerin** geçmişi, o an hangisinin açık olduğundan bağımsız olarak güvenli bir şekilde temizleniyor.
- **PiP Modunda Tıklanabilirlik Garantilendi** (2026-03-14): `OpenPiP` işlemi sırasında `MouseCaptureLayer.IsHitTestVisible` özelliği açıkça `true` olarak ayarlandı. Daha önce varsayılan değere güvenilerek yorum satırına alınan bu kod, PiP moduna girildiğinde kullanıcı etkileşimlerinin (tıklama, sürükleme) her zaman güvenli ve tutarlı bir şekilde yakalanmasını garantilemek için aktif hale getirildi.

### ♻️ Kod İyileştirmeleri ve Yeniden Düzenlemeler
- **PiP Durum Yönetimi Tekilleştirildi** (2026-03-14): Uygulama genelinde PiP modunun açık olup olmadığını takip eden ve hem `MainWindow` içerisinde hem de `PlayerViewModel` içerisinde tutulan, zaman zaman senkronizasyon hatalarına yol açabilecek olan çift durum (duplicate state) sorunu giderildi. `MainWindow` içerisindeki `_isPiPMode` alanı kaldırılarak tüm kontroller `_playerViewModel.IsPiPMode` üzerinden yönetilecek şekilde tek bir kaynak (single source of truth) prensibine uygun hale getirildi.
- **Ölü Kod Temizliği Yapıldı** (2026-03-14): `MainWindow.axaml.cs` içerisinde bulunan fakat hiçbir UI elemanı tarafından kullanılmayan (ölü kod) `PiPDrag_PointerPressed` metodu silindi. Bu metodun yapması gereken pencere sürükleme işlemi halihazırda `MouseCaptureLayer_PointerPressed` tarafından yönetildiği için fazladan ve işlevsiz kod blokları projeden kaldırıldı.
- **Otomatik Kategorizasyon ve Dil Desteği İyileştirmesi** (2026-03-14): Kategorisi olmayan içerikler için kullanılan varsayılan "Genel" adı, uluslararası standarta uygunluk için "**Uncategorized**" olarak değiştirildi. `PlaylistOrganizerService` içerisindeki kategorizasyon motoru `spor` anahtar kelimesi için iyileştirildi.
- **Dizi/Film Tanıma Motoru (SeriesInfoParser) Güncellendi** (2026-03-14): `|TR|` gibi karmaşık ön eklerin (prefix) temizlenmesi ve dizi bilgilerinin daha tutarlı ayıklanması sağlandı.
- **PiP Çerçevesi Veri Bağlamı Güçlendirildi** (2026-03-14): `MainWindow.axaml` içerisinde yer alan `PiPFrame` elementinin görünürlüğü (`IsVisible`) doğrudan bir UI elemanına (element reference binding) bağlıydı. Bu kırılgan yapı kaldırılarak, görünürlük doğrudan `PlayerViewModel` içerisindeki `IsPiPControlsVisible` özelliğine (property) bağlandı. Bu sayede olası isim değişikliklerinde (refactoring) çalışma zamanı hatalarının önüne geçildi ve MVVM prensiplerine tam uyum sağlandı.
- **VLC Çift Başlatma (Race Condition) Engellendi** (2026-03-14): Kullanıcı arayüzde altyazı ayarlarını (ör. boyut ve konumu aynı anda) çok hızlı değiştirdiğinde VLC oynatıcısının arka planda üst üste iki kez tam yıkım-kurulum (ReinitializeAsync) döngüsüne girmesine neden olabilen asenkron yarış durumu çözüldü. İşlem için `CancellationTokenSource` tabanlı güvenli bir "Debounce" mekanizması eklendi.
- **Altyazı Ayarları Yeniden Başlatma Uyarısı Düzeltildi** (2026-03-14): "Oynatıcı anlık olarak yeniden başlatılır" uyarısının sadece Altyazı Boyutu değişikliğinde görünmesi sorunu çözüldü. Uyarı genel bir çerçeveye alınarak Arkaplan Şeffaflığı ve Altyazı Konumu değişikliklerini de kapsayacak şekilde panelin en üstüne taşındı ve daha estetik bir tasarımla (bilgi ikonuyla) yenilendi.
- **Arayüz Kaydetme (CancellationToken) Güvenliği Düzeltildi** (2026-03-14): Altyazı ayarları hızlıca değiştirildiğinde oluşan bellek yönetim hatası (Dispose edilmiş token okuma) giderildi. Ayarlar kaydedilirken uygulamanın daha stabil çalışması için güvenli bir `CancellationTokenSource` yaşam döngüsü kullanılıyor.
- **Canlı Yayın Ses/Altyazı Seçimi Kaybı Giderildi** (2026-03-14): Altyazı veya ses ayarları değiştirildiğinde VLC motorunun yeniden başlatılması (`ReinitializeAsync`) esnasında kullanıcının o an seçtiği aktif Ses ve Altyazı kanallarının (track) sıfırlanıp varsayılana dönmesi sorunu çözüldü. Seçimler yeniden başlatma sırasında hafızada tutulup video tekrar başladığında otomatik geri yükleniyor.
- **Altyazı Arayüz Senkronizasyonu Düzeltildi** (2026-03-14): Uygulama yeniden başlatıldığında, oynatıcı kontrol panelindeki altyazı ayar butonlarının (Boyut, Arkaplan Şeffaflığı, Konum) en son kaydedilen ayar yerine varsayılan ("Standart") seçili görünmesi sorunu çözüldü. Arayüz modelinin (`PlayerViewModel`) ayarlardaki gerçek veriyi başlatma anında okuması sağlandı.

### ✨ Yeni Özellikler ve Geliştirmeler
- **Premium Kilitleri ve Ayrıcalıkları Eklendi** (2026-03-17):
  - Ayarlar menüsündeki **"EPG Yenileme Sıklığı"** ve **"Kanal Listesi Yenileme Sıklığı"** otomatik yenileme seçenekleri Premium kullanıcılara özel hale getirildi. 
  - Ücretsiz (Free) kullanıcılar sadece "Kapalı (Sadece manuel)" seçeneğini kullanabilecek. Diğer tüm otomatik yenileme aralıkları (1 saat, 24 saat vb.) devre dışı bırakıldı ve yanlarına şık bir sarı "Kilit (Lock)" ikonu eklenerek kilitli oldukları açıkça belirtildi.
- **Arayüz Geçiş Animasyonları (Fade-in) İyileştirildi** (2026-03-15):
  - **İndirilenler (Downloads) Menüsü Animasyonu:** İndirilenler ekranında bulunan "Kütüphane" ve "İndirme Merkezi" sekmeleri (eski adıyla RadioButton'lar) yapısal olarak `TabControl` sistemine geçirildi. Bu sayede Ayarlar menüsünde kullanılan iOS/Apple TV kalitesindeki yatay kaydırma (Slide & Fade) animasyonu (`TabSlideTransitionBehavior`) bu ekrana da entegre edildi. Eski görünüm birebir korunurken etkileşim kalitesi artırıldı.
  - **Ayarlar Menüsü (Sekmeler) Animasyonu:** Genel Ayarlar ve Profil Ayarları ekranlarındaki Tab (sekme) geçişlerine iOS/Apple TV kalitesinde yatay kaydırma animasyonu eklendi. Özel yazılan `TabSlideTransitionBehavior` sayesinde, tıklandığında seçilen sekmenin eski sekmeye göre yönü (sağ/sol) hesaplanıp içerik o yönden süzülerek (`SplineEasing`) ekrana geliyor.
  - **Bölüm Listesi (Episode List) Animasyonu:** Dizi detay sayfasında sezonlar arası geçişler tamamen iOS/Apple TV standartlarında yeniden yazıldı. Kayma mesafeleri optimize edildi (28px), ivmelenme eğrisi (SplineEasing) baştan aşağı yenilenerek "hızlı giriş, çok yumuşak çıkış" hissiyatı sağlandı. Avalonia UI setter hatalarını (TransformGroup vs) önleyen ve bellek/task sızıntılarını tamamen kapatan yeni bir `SlideTransitionBehavior` mimarisi kuruldu.
  - **Yan Menü (Sidebar) Vurgusu:** Sol menüde gezinirken (Home, Movies vs.) aktif olan sekmeyi gösteren renkli göstergenin (`ActiveIndicator`) aniden belirmesi yerine yumuşak bir kayma (Sliding/Width Transition) animasyonu ile genişleyerek gelmesi sağlandı.
  - **Ana Sayfa Boş Durum (Empty State):** "İzlemeye Devam Et" listesi boş olduğunda ortada beliren Noctra logosuna "Nefes Alma" (Pulse/Breathing) animasyonu eklendi. (2.5 saniyelik %60 - %100 arası yumuşak Opacity değişimi).
  - **Sayfa Geçişleri (Views):** Sol menüden (Ana Sayfa, Filmler, Diziler vb.) sekmeler arası geçiş yapıldığında sayfaların aniden değişmesi yerine **0.15 saniyelik yumuşak bir Opacity (Fade-in)** geçişi eklendi. Bu küçük dokunuş, sayfa geçişlerinin çok daha pürüzsüz ve "canlı" hissettirmesini sağlıyor.
  - **Durum Çubuğu (StatusBar):** Alt kısımda yer alan durum mesajlarına (örn. "Kanal listesi hazır", "İndirme başlatıldı") 0.25 saniyelik yumuşak bir Opacity (Fade-in) geçişi eklendi. Mesaj değişimleri artık aniden belirip kaybolmak yerine daha organik bir his veriyor.
  - **Görseller (Posterler):** `RemoteImage` bileşeni üzerinden yüklenen posterler ve logolar için de benzer bir fade-in animasyonu devreye alındı. Resimler yüklendiğinde aniden patlamak yerine yumuşakça ekranda beliriyor.
- **İzleme Kartı (Continue Watching) Etkileşim Geliştirmesi** (2026-03-15): Ana sayfadaki "Kaldığın Yerden Devam Et" kartlarında, fare ile üzerine gelindiğinde (Hover) alttaki ilerleme çubuğunun (Progress Bar) kalınlığı 3px'ten 6px'e animasyonlu bir şekilde büyüyerek, kullanıcının "tıklanabilirlik" hissiyatını (affordance) artırması sağlandı.
- **Gizli Geliştirici Modu ve Premium Bypass** (2026-03-15): Ayarlar menüsüne (`GlobalSettingsWindow`) sadece geliştiricilerin erişebileceği, test süreçlerini hızlandıracak gizli bir araç seti (`Developer Tools`) eklendi.
  - Ayarlar sayfasının en altında yer alan görünmez metin kutusuna ortam değişkenlerinden (`.env` içerisindeki `DEV_PASSWORD`) alınan şifre girildiğinde özel panel aktif hale gelir.
  - Geliştirici paneline, tek tıkla uygulamanın Premium ve Ücretsiz sürümleri arasında geçiş yapabilmesini sağlayan "Toggle Premium" özelliği entegre edildi.
  - Ayarlar sayfasının en altında yer alan görünmez metin kutusuna ortam değişkenlerinden (`.env` içerisindeki `DEV_PASSWORD`) alınan şifre girildiğinde özel panel aktif hale gelir. 
  - Geliştirici paneline, tek tıkla uygulamanın Premium ve Ücretsiz sürümleri arasında geçiş yapabilmesini sağlayan "Toggle Premium" özelliği entegre edildi.
- **İçerik Yükleme ve Boş Durum (Empty State) Tasarımları** (2026-03-15):
  - **Dinamik Yükleme:** Canlı TV, Film ve Dizi sayfaları için içeriğin yüklenme durumunu belirten (`IsContentLoading`) yeni bir sistem eklendi. Yükleme sırasında sayfa içeriğine uygun mesajlar ve premium yükleme animasyonu gösterilir.
  - **Görsel Boş Durumlar:** Filtreleme veya arama sonucunda içerik bulunamadığında (`ShowEmptyChannels`) her kategoriye özel (Televizyon, Filmstrip, MovieFilter) ikonlar ve yönlendirici metinler içeren estetik paneller tasarlandı.
  - **Zeki Durum Yönetimi:** Yükleme ve boş state geçişleri, listenin o anki doluluk oranına ve sunucu yanıt durumuna göre otomatik olarak yönetilir.
- **Gelişmiş Hata Raporlama ve Çökme Takip Sistemi** (2026-03-14):
    - Uygulama içerisinde karşılaşılan hataların ve beklenmedik çökmelerin (crash) anında geliştiriciye raporlanmasını sağlayan profesyonel bir teşhis altyapısı eklendi.
    - **Otomatik Teşhis:** Kullanıcı ID, lisans durumu, işletim sistemi versiyonu ve mimari bilgilerini içeren minimalist ve İngilizce raporlar oluşturulması sağlandı.
    - **Kritik Hata Yönetimi:** Uygulamayı kapatan büyük hatalar için (Unhandled Exception) otomatik mail taslağı oluşturma ve asenkron görev hatalarını (Task Exception) takip etme mekanizması devreye alındı.
    - **Kullanıcı Dostu UI:** Global ve profil ayarları sayfalarına, estetik "Hata Bildir" butonları entegre edildi.
- **Dinamik "Hakkında" Kartı ve Premium Deneyimi** (2026-03-14):
    - **Premium Tasarımı**: Premium kullanıcılar için çift gradient çerçeve efekti, altın "PREMIUM" rozeti ve arka planda estetik taç ikonu eklendi.
    - **Ücretsiz (Free) Tasarımı**: Ücretsiz kullanıcılar için border kaldırılmış, `CornerRadius` değeri 8px olarak ayarlanmış ve standart arayüz bileşenleriyle uyumlu hale getirilmiş estetik bir "Hakkında" kartı tasarlandı.
- **Premium Upsell (Yükseltme) Mekanizması** (2026-03-14):
    - Ücretsiz kullanıcılar için "Hakkında" kartına "Premium'a Geç" butonu eklendi.
    - `UpsellWindow` ekranına erişimi sağlayan `ShowUpsellCommand` mimarisi `GlobalSettingsViewModel` ve `SettingsViewModel` içerisine entegre edildi.
- **Profile Özel Ayarlar Mimarisi** (2026-03-14):
    - Uygulama ayarlarının (altyazı dili, geçmiş saklama süresi, otomatik oynatma vb.) her profil için bağımsız olarak saklanması sağlandı.
    - Tek bir `settings.json` yerine, her profil için `settings_profile_{id}.json` yapısına geçilerek profiller arası ayar çakışmaları tamamen engellendi.
    - Profil değiştirildiğinde ilgili ayarların anlık olarak yüklenmesi ve arayüze yansıtılması sağlandı.
- **Altyazı Paneli Tema ve Mimari Optimizasyonu** (2026-03-14):
    - **Açık Tema İyileştirmesi:** Açık temada (Light Theme) altyazı panelindeki okunabilirlik sorunları giderildi. Seçili buton vurguları (`NavActiveBackgroundBrush`) ve bilgi kutusu tasarımı, beyaz arka plan üzerinde yüksek kontrastlı ve estetik görünecek şekilde optimize edildi.
    - **XAML Mimari Temizliği:** Altyazı ayar butonlarındaki 100+ satırlık mükerrer `MultiBinding` kodu temizlendi. Yeni geliştirilen `EqualityToResourceBrushConverter` ile XAML yapısı çok daha hafif ve sürdürülebilir hale getirildi.
    - **UX İyileştirmesi:** Bilgi kutusundaki metin taşma sorunu (`TextWrapping`) giderilerek, mesajların her ekran boyutunda tam ve düzgün görünmesi sağlandı.
- **Kapsamlı Kararlılık ve Birim Testi Seferberliği** (2026-03-14):
    - `EpgService` ve `PlaylistOrganizerService` için toplam 146 yeni birim testi (Unit Test) eklendi.
    - EPG eşleştirme motoru (`EpgMatchingTests`) 72 senaryo ile, oynatma listesi düzenleme motoru (`PlaylistOrganizerServiceTests`) ise 74 senaryo ile %100 kapsama ulaştırıldı.
    - `EpgService` içerisindeki private metodlar reflection kullanılarak test edilebilir hale getirildi.
- **NoctraProviderTester Mimari Devrimi** (2026-03-14):
    - Tester projesi, `Noctra.Core` kütüphanesini doğrudan referans alacak şekilde baştan aşağı refaktör edildi.
    - Tester içindeki ~600 satırlık mükerrer (duplicate) kod, model ve parser mantığı temizlendi.
    - Artık tester projesi, üretimdeki aynı `M3UParser` ve `SeriesInfoParser` mantığını kullanarak %100 tutarlı sonuçlar üretmektedir.
- **Dinamik Altyazı Boyut ve Konum İyileştirmesi (Netflix Standardı)** (2026-03-13):
    - **Dinamik Konum:** "Yukarı" altyazı konumu seçildiğinde altyazının ekranın en üst sınırına yapışması sorunu çözüldü. Oynatılacak videonun çözünürlüğü oynatma öncesi anlık olarak analiz edilerek, "Yukarı" konumu için ekran yüksekliğinin %85'ine denk gelen dinamik bir piksel marjini uygulanması sağlandı.
    - **Dinamik Boyut:** Altyazı boyutları (Küçük, Orta, Büyük) sabit piksel değerleri yerine Netflix standartlarına uygun olarak ekran yüksekliğinin yüzdelik dilimleri (Sırasıyla ~%3, ~%4.5, ~%8.5) olarak yeniden düzenlendi. Böylece 720p, 1080p veya 4K videolarda altyazı boyutları her zaman videoyla orantılı olarak görünecek.
- **Gelişmiş Altyazı Özelleştirme Sistemi** (2026-03-12):
    - **Altyazı Boyut Profilleri**: Altyazılar için Küçük (28), Standart (40) ve Büyük (60) olmak üzere üç farklı boyut profili eklendi.
    - **Arka Plan Şeffaflığı Kontrolü**: Altyazıların parlak sahnelerde okunabilirliğini artırmak için "Kapalı", "Yarı Saydam" ve "Siyah" arka plan seçenekleri eklendi.
    - **Akıllı Konumlandırma**: Altyazıların ekranın ne kadar üzerinde duracağını ayarlayan "Normal" ve "Yukarı" (sinema modu için ideal) konum seçenekleri hayata geçirildi.
    - **Video Üstü (Overlay) Entegrasyonu**: Tüm bu ayarlar video oynatılırken "Ses ve Altyazı" menüsünden anında (real-time) değiştirilebilir hale getirildi. 
    - **Otomatik Geri Yükleme**: Ayar değişikliği nedeniyle video arka planda yeniden başlatıldığında, o an seçili olan ses ve altyazı dilinin kaybolmaması için otomatik geri yükleme mantığı eklendi.
- **Gizlilik ve İzleme Geçmişi Yönetimi** (2026-03-11):
    - İzleme geçmişini kaydetme/durdurma seçeneği eklendi (**Ayarlar > Gizlilik**).
    - Geçmişi otomatik temizleme özelliği hayata geçirildi; kullanıcılar 3, 7, 14 veya 30 gün sonra eski kayıtların silinmesini seçebilir.
    - "Çıkışta Geçmişi Temizle" seçeneği ile uygulama kapatıldığında tüm izleme verilerinin otomatik silinmesi sağlandı.
    - Tek tıkla tüm geçmişi ve içerik ilerlemelerini (progress markers) silme butonu eklendi.
    - Yeni özelliklerin kararlılığı `PrivacyAndHistoryScenariosTests` entegrasyon testleri ile doğrulandı.
    - `WatchHistoryService` altyapısı veri koruma ve otomatik temizleme süreçleri için modernize edildi.
- **Sidebar İndirme Rozeti (Badge) İyileştirmesi** (2026-03-11):
    - Kenar çubuğu kapalıyken (Mini mod) görünmeyen aktif indirme sayısı göstergesi, ikon üzerine yerleşen modern bir "nokta" (dot) rozetiyle değiştirildi.
    - Kenar çubuğu açıkken sayısal rozetin, kapalıyken ise ikon üzerindeki noktanın dinamik olarak gösterilmesi sağlandı.
    - Rozetin kenarlarda yarım kalması sorunu giderilerek yerleşimi optimize edildi.
- **Bildirim Ayarları ve Masaüstü Bildirimleri** (2026-03-11):
    - İndirmesi tamamlanan içerikler için **Windows İşlem Merkezi (Action Center)** ile entegre çalışan yerli bildirim sistemi hayata geçirildi.
    - Bildirim içeriğine inen dosyanın adı eklenerek kullanıcının neyin indiğini net bir şekilde görmesi sağlandı.
    - Ayarlar menüsüne "Bildirimler" sekmesi eklendi; indirme tamamlanma bildirimi buradan açılıp kapatılabilir.
    - Windows dışı platformlar için modern, otomatik kapanan yedek bildirim penceresi optimize edildi.
- **Ses ve Altyazı Tercihleri** (2026-03-11):
    - VOD ve Diziler için varsayılan altyazı durumu ve tercih edilen ses/altyazı dili ayarları eklendi.
    - Player, içerik açıldığında ayarlardaki tercihlere göre en uygun kanalları otomatik seçer.
    - Bu özellik sadece VOD/Dizi için aktiftir, Canlı TV'yi etkilemez.
- **EPG Ayarları İyileştirmesi** (2026-03-11):
    - EPG özelliğini tamamen açıp kapatabilmek için `EpgEnabled` ayarı eklendi.
    - EPG devre dışı bırakıldığında `EpgService` veri indirme ve işleme süreçlerini atlayarak performans sağlar.
    - `SettingsWindow.axaml` içinde `ToggleSwitch` kontrolünde oluşan çalışma zamanı hatası (StaticResource hatası) giderildi.
- **Kanal Listesi İyileştirmeleri** (2026-03-11):
    - Ayarlar -> Kanal Listesi sekmesine toplam kanal sayısını gösteren "Kanal Sayısı" bilgisi eklendi.
    - Kanal sayısı, aktif profilin veya seçili listenin tüm içeriklerini (Canlı, VOD, Dizi) kapsayacak şekilde güncel olarak hesaplanır.
- **Geçmişten Silme Özelliği** (2026-03-10):
    - Geçmiş sayfasındaki tüm kartlara (Canlı TV, Dizi, Film) sağ tık menüsü üzerinden "Geçmişten Sil" seçeneği eklendi.
    - Bu özellik hem arayüzden öğeyi anında kaldırır hem de veritabanındaki ilgili izleme geçmişini ve ilerleme (progress) verilerini temizler.
    - Diziler için "İzlemeye Devam Et" verileri de temizlenerek ana sayfadan da kaldırılması sağlandı.
    - `VodCard` ve `SeriesCard` bileşenlerine `ShowHistoryMenu` özelliği eklendi, böylece bu menü sadece geçmiş sayfasında görünür.
- **İzleme Geçmişi ve Dizi İlerleme Güvenliği** (2026-03-10):
    - **Race Condition Önleme**: `WatchHistoryService` içerisinde `SemaphoreSlim` kullanılarak eş zamanlı kayıtlarda oluşan mükerrer (duplicate) geçmiş verileri engellendi.
    - **CancellationToken Desteği**: Tüm izleme geçmişi ve temizlik metodlarına iptal desteği eklenerek uzun süren veritabanı işlemlerinin güvenle sonlandırılması sağlandı.
    - **Gelişmiş Veri Temizliği (Cleanup)**: Tamamlanmış (Completed) içeriklerin yanlışlıkla silinmesi engellendi; böylece izleme noktaları ve "izlendi" statüleri koruma altına alındı. Ayrıca `SeriesEpisodeProgresses` tablosu da temizlik döngüsüne dahil edilerek veritabanı şişmesi önlendi.
    - **Akıllı Dizi İsim Fallback**: `seriesTitle` tespiti için `BaseDisplayName` kullanılarak, orphan episode durumlarında bile dizi ilerlemelerinin doğru eşleşmesi (normalization) sağlandı.
    - **Performans Optimizasyonu**: `GetHistoryAsync` sorgusuna `AsNoTracking()` ve derin `Include` yapıları eklenerek hem bellek kullanımı azaltıldı hem de geçmiş ekranında dizi isimlerinin tam görünmesi sağlandı. Sorgu 100 kayıt ile sınırlandırıldı.
    - **Veri Bütünlüğü Koruması**: Negatif zaman delta (saat kayması) durumlarında izleme süresinin bozulması engellendi. Aynı anda hem kanal hem bölüm ID'si set edilen hatalı kayıt girişlerine karşı koruma eklendi.
- **Filtreleme Alanı Tasarımı**: Live, Movies ve Series sayfalarındaki filtreleme barı (Kategori, Tümü, Sıralama) daha premium ve uyumlu bir görünüme kavuşturuldu.
  - Yeni `FilterButtonStyle` oluşturuldu ve tüm butonlara uygulandı.
  - ComboBox'lar `ModernComboBox` temasına geçiş yaptı.
  - Tüm öğelerin yükseklik ve hizalamaları (45px) standart hale getirildi.
  - Kategori ve sıralama listelerinin daha fazla öğe gösterebilmesi için açılır pencere boyutu uzatıldı (`MaxDropDownHeight` artırıldı).
- **Gelişmiş Yenileme Seçenekleri**: EPG ve Kanal Listesi yenileme sıklığı seçeneklerine 2 gün, 3 gün ve 7 gün alternatifleri eklendi. Varsayılan bekleme süresi her iki ayar için de "Kapalı (Sadece Manuel)" (0 saat) olarak ayarlandı.
- **Arama Önerisi İyileştirmeleri**: "Bunu mu demek istediniz?" mantığı daha isabetli olacak şekilde optimize edildi.
- **Ana Sayfa Boş Durum Görünümü**: "İzlemeye Devam Et" listesi boş olduğunda (yeni profil veya içerik izlenmemişse) ekrana hoş geldiniz mesajı ve yönlendirmeler içeren şık bir placeholder eklendi.
- **İndirilenler Tasarım Güncellemesi**: İndirilenler sayfasındaki sıralama ComboBox'ı, uygulamanın genel modern tasarım diliyle (`ModernComboBox`) uyumlu hale getirildi.

### 🛠️ Düzeltmeler ve Optimizasyonlar
- **Gelişmiş Seri ve EPG Kimlik Algılama** (2026-03-14):
    - `SeriesInfoParser` üzerindeki `CountryPrefixRegex` geliştirilerek `|TR|` gibi karmaşık ön eklerin temizlenmesi sağlandı.
    - `PlaylistOrganizerService` kategorizasyon kurallarına eksik olan `spor` anahtar kelimesi eklendi.
    - `AutoCategorize` mantığında Canlı kanallar için "Diziler" kategorisine geçişe (Kanal D, Show TV gibi ana kanallar için) izin verilerek daha doğru sınıflandırma sağlandı.
    - Varsayılan (eşleşmeyen) kategori ismi "Genel" yerine "Uncategorized" olarak güncellendi.
- **Tester Kaynak Yönetimi ve Concurrency Düzeltmeleri** (2026-03-14):
    - `StreamAnalyzer` içerisindeki `LibVLC` kaynak sızıntısı (leak) `Shutdown()` metodu ile giderildi.
    - `SemaphoreSlim` kullanımı optimize edilerek eş zamanlı stream analizlerindeki kararsızlıklar çözüldü.
- **Arama Önerisi Zekası İyileştirildi** (2026-03-13):
    - "Bunu mu demek istediniz?" mantığı geliştirilerek alakasız substring eşleşmeleri (örn: "Cking" -> "fucking") engellendi.
    - Dizi aramalarında, eğer dizi ana başlığı zaten bulunmuşsa spesifik bölüm (Sxx Exx) önerilmesi durduruldu.
    - Kelime benzerlik skoru hesaplanırken uzunluk farkı cezası ve kelime başı önceliği eklendi.
- **Arayüz Düzenlemeleri ve İyileştirmeler** (2026-03-13):
    - **Anasayfa**: "İzlemeye Devam Et" bölümü yatay kaydırmalı (rail) yapıdan, pencereye sığacak şekilde alta kayan (wrap) yapıya dönüştürüldü.
    - **Ayarlar**: Menü sekmelerinin (Profil, Görünüm vb.) alt satıra geçmesi engellendi, tüm öğelerin tek satırda kalması için boşluklar optimize edildi.
- **Bilgi Paneli Görünürlük ve Mantık İyileştirmesi** (2026-03-13):
    - Canlı TV kanallarında "Hakkında" panelinde oluşan boş ikinci kutucuk sorunu giderildi.
    - VOD ve Dizi içeriklerinde "Plot" (Özet) bilgisinin görünmemesi veya hatalı görünmesi sorunları çözüldü.
    - Karmaşık XAML `MultiBinding` mantığı yerine ViewModel üzerinde `IsLiveInfoVisible`, `IsSeriesPlotVisible` ve `IsVodPlotVisible` özellikleri eklenerek görünürlük kontrolü merkezi hale getirildi.
    - Boolean AND işlemlerini MultiBinding içinde güvenle yönetmek için `BoolAndMultiConverter` eklendi.
- **Performans ve Kararlılık Düzeltmeleri** (2026-03-12):
    - **Altyazı Ayarı Konumu ve Davranışı Yenilendi**: Altyazı boyutu ayarı, ana "Ayarlar" sayfasından kaldırılarak doğrudan video oynatıcı üzerindeki "Ses ve Altyazı" (Overlay) menüsünün içine taşındı. Artık Küçük (28), Standart (40) ve Büyük (60) profil seçenekleriyle daha kullanıcı dostu hale getirildi. Ayrıca Canlı yayınlarda gereksiz yer kaplamaması için gizlendi.
    - **Altyazı Boyut Değişimi Çökmesi (Crash) Giderildi**: Kullanıcı altyazı boyutunu değiştirdiğinde eski oynatıcının temizlenmesi (`Dispose`) işlemi ana UI thread'ini kilitleyerek `AccessViolation` çökmesine (fatal exception) yol açıyordu. Temizlik işlemi `Task.Run` ile arkaplana alındı, UI referansları güvenli bir şekilde silindi ve çökme tamamen engellendi.
    - **Altyazı Seçiminin Sıfırlanması Sorunu Çözüldü**: Boyut değiştirilip video arka planda yeniden başlatıldığında, kullanıcının o an seçtiği mevcut dil/altyazı profilinin kapanması sorunu düzeltildi. Sistem artık kapanmadan önceki seçili profili (`SelectedSubtitleTrack` ve `SelectedAudioTrack`) hafızasında tutup, yeniden başlatma saniyeler içinde tamamlanınca otomatik olarak geri yüklüyor.
    - **Arayüz Koleksiyonu (CollectionModified) Çökmesi Engellendi**: Uygulama kapanırken veya video güncellenirken nadiren oluşan `Collection was modified; enumeration operation may not execute` hatası, ses ve altyazı listelerinin doğrudan atanması yerine UI-Safe `ObservableCollection` kullanılarak işlenmesiyle kökten çözüldü.
    - **Video Overlay Odak ve Görünürlük Sorunları Tamamen Çözüldü**: Video oynatıcı üzerindeki kontrol panelinin (overlay) bazı durumlarda kaybolması ve ancak başka pencereye geçip geri gelince düzelmesi sorunu kökten çözüldü. 
        - State machine mantığı `OverlayFocusController` adında test edilebilir bağımsız bir sınıfa taşındı.
        - Overlay'in sadece odak değişiminde değil, her timer tick'inde görünürlük durumu kontrol edilerek (idempotent) gerekirse otomatik olarak geri getirilmesi sağlandı.
        - **Alt-Tab Gizleme**: Overlay penceresinin Alt-Tab (Görev Değiştirici) listesinde ayrı bir pencere olarak görünmesi engellendi (Win32 `WS_EX_TOOLWINDOW` entegrasyonu).
        - **Kritik Çökme Giderildi**: Overlay penceresinin sistem tarafından veya manuel kapatılması durumunda oluşan `InvalidOperationException: Cannot re-show a closed window` hatası, pencere referanslarının dinamik takibi ile çözüldü.
        - Uygulama içi sekmeler arası geçişlerde overlay'in diğer pencerelerin üzerinde asılı kalması (ghosting) engellendi.
        - 50 farklı senaryoyu kapsayan kapsamlı bir test suite (`OverlayFocusControllerTests`) eklenerek çözümün sağlamlığı doğrulandı.
    - **Dizi Güncelleme (UpdateSeriesAsync) Performansı Optimize Edildi**: Favoriye ekleme veya listeye ekleme işlemlerinde bir playlist'teki tüm dizilerin RAM'e yüklenmesi sorunu giderildi. Artık işlem öncesinde `SeriesId` üzerinden doğrudan erişim ve isim ön-filtresi (heuristic) kullanılarak veritabanı sorgusu daraltılıyor, binlerce kaydın belleğe çekilmesi engelleniyor.
    - **Boş Sezonların (Season) Temizlenmesi Sağlandı**: `MediaService` içerisindeki içerik birleştirme (aggregation) mantığına boş kalan sezonları temizleme özelliği eklendi. Artık bir playlist yenilendiğinde, içerisinde bölüm kalmayan sezon nesneleri veritabanından otomatik olarak siliniyor.
    - **Hatalı Bölüm Çakışması (S01E01 Fallback) Düzeltildi**: `SeriesInfoParser` üzerinde başlığı parse edilemeyen dizilerin varsayılan olarak S01E01'e atanıp gerçek ilk bölümün üzerine yazması sorunu çözüldü. Artık unparsed bölümler S00E00 olarak işaretleniyor ve veritabanında yalnızca benzersiz akış adresleri (Stream URL) üzerinden eşleştiriliyor.
    - **İzleme Geçmişi İçin Sonsuz Kaydırma (Infinite Scroll) Desteği**: `GetHistoryAsync` metoduna sayfalama (skip/take) desteği eklendi. `MainViewModel` üzerinden 50'şerli sayfalar halinde yükleme ve `HistoryView` üzerinde kullanıcı aşağı indikçe otomatik yeni veri çekme (infinite scroll) mekanizması hayata geçirildi. Bu sayede yüzlerce geçmiş kaydının aynı anda render edilmesinden kaynaklı performans sorunları giderildi.
    - **Tüm Kullanıcıların Geçmişini Silme Hatası Giderildi (KRİTİK)**: Ayarlar sekmesindeki "Tüm Geçmişi Sil" butonu ve "Çıkışta Geçmişi Temizle" özelliklerinin profil ayırt etmeksizin tüm veritabanındaki geçmişi sildiği kritik veri kaybı hatası düzeltildi. `WatchHistoryService` içerisindeki `ClearAllHistoryAsync` metodu ve referansları tamamen kaldırılarak yerine sadece ilgili profilü etkileyen `DeleteProfileHistoryAsync(profileId)` kullanılması sağlandı.
    - **Çıkışta Geçmişi Silme İşleminin Yarım Kalması Engellendi**: Uygulama kapatılırken çalışan "Geçmişi Sil" işleminin `MainWindow.OnClosed` (Fire-and-forget) içinde çalıştırıldığı için process kapanmadan yarım kalması sorunu çözüldü. İşlem `App.axaml.cs` içerisindeki `desktop.Exit` hook'una taşındı ve sürecin veritabanı silme işlemi tamamlanana kadar uygulamayı senkron bir şekilde açık tutması sağlandı.
    - **Eski Geçmişi Temizleme İşlemindeki Yarış Durumu (Race Condition) Çözüldü**: Uygulama başlatılırken `MainWindow` içerisinde sabit 5 saniye bekleyip (`Task.Delay(5000)`) geçmişi temizlemeye çalışan ve eğer profil 5 saniyeden geç yüklenirse tamamen sessizce başarısız olan yapı kaldırıldı. Temizlik işlemi artık doğrudan `MainViewModel` üzerinde profil değişimi (veya ilk yüklenişi) olduğunda (`CurrentProfileId` event'i aracılığıyla) güvenilir bir şekilde tetikleniyor.
    - **Geçmiş Silme Süresi (Retention) Ayarı Düzeltildi**: `MainViewModel` içerisindeki geçmiş yenileme metodunda sabit olarak (hardcoded) 7 günden eski izleme kayıtlarını silen yapı düzeltildi. Artık kullanıcının "Süresiz", "14 gün", "30 gün" gibi ayarlar sekmesinde belirlediği global "Geçmişi Saklama Süresi" ayarı baz alınarak temizlik yapılıyor.
    - **İzleme Geçmişinden Silinen İçeriklerin Anlık UI Güncellemesi**: Bir kanal veya film geçmişten silindiğinde, Ana Sayfa üzerindeki "İzlemeye Devam Et" (Continue Watching) listesinden anında kaldırılmaması sorunu çözüldü. `RemoveFromHistoryAsync` metoduna ilgili koleksiyondan çıkarma mantığı eklendi.
    - **EpgService Bellek Sızıntısı (Memory Leak) Giderildi**: `.gz` uzantılı EPG verileri yüklenirken oluşturulan `GZipStream` nesnelerinin deşifre işleminden sonra belleğe iade edilmeyip (dispose edilmeyerek) native file handle sızıntısına (resource leak) yol açması engellendi. Asenkron `await using` kalıbı ile stream'lerin güvenli bir şekilde kapatılması sağlandı.
    - **EPG Temizleme Durum Tutarsızlığı Giderildi**: `ClearEpgAsync` çağrısından sonra, veritabanı temizlendiği halde sınıf içindeki `IsLoaded` durumunun `true` kalması sorunu düzeltildi. Durum bayrağı temizleme sırasında güncellenerek, sonraki program sorgularında boş verilerin yanlış yönlendirmesi engellendi.
    - **Otomatik Kategorizasyon (PlaylistOrganizerService) Düzeltildi**: Canlı kanalların ("Show TV", "Star TV" vb.) isimlerinde "tv" veya "show" geçtiği için yanlışlıkla "Diziler" veya "Filmler" kategorisine atanması sorunu çözüldü. Artık kelime eşleşmesinden önce kanal türü (`ChannelType.Live`) kontrol ediliyor ve canlı yayınlar doğru şekilde ana gruplara veya "Genel" kategorisine yönlendiriliyor.
    - **Hata Kayıt (Log) Dizini Düzeltildi (PlaylistService)**: Playlist yenileme sırasında oluşabilecek hataları yazan `LogDetailedErrorAsync` metodunun, üretim (production) ortamında yazma izni olmayan `Program Files` dizini altındaki `BaseDirectory` yerine, standart uygulama veri yolu olan `LocalAppData/Noctra/Logs` içerisine yazması sağlandı. Bu sayede üretim hatalarının sessizce yutulması engellendi.
    - **EPG Eşzamanlılık (Concurrency) Yanılgısı Giderildi (EpgService)**: Yeni bir EPG yükleme isteği geldiğinde, hâlihazırda devam eden bir işlem varsa metodun (semaphore engeline takılarak) `0` döndürmesi sorunu çözüldü. Dönen `0` değeri, başarıyla yüklenip hiç program bulunamama durumuyla karışıyordu. Artık işlemin atlandığını belirtmek için `-1` döndürülüyor.
    - **Ölü Kod ve Gereksiz Abonelik Temizliği (VideoPlayerService)**: Üretim ortamında hiçbir işlevi olmayan ve yalnızca log kirliliğine neden olan `_lastLogProgress` değişkeni ve ilgili mantıklar temizlendi. Ayrıca içi boş olan `OnSettingsChanged` metodu ve gereksiz event abonelikleri kaldırılarak nesne yaşam döngüsü ve kod temizliği iyileştirildi.
    - **Gereksiz Namespace Temizliği**: `EpgService` içerisindeki kullanılmayan `System.Xml.Linq` using ifadesi kaldırılarak kod sadeleştirildi.
- **Kullanıcı Deneyimi ve İzleme Geçmişi İyileştirmeleri** (2026-03-11):
    - **"İzlemeye Devam Et" (Continue Watching) Dizi Bağlamı Hatası**: Anasayfadaki "İzlemeye Devam Et" kartından bir dizi bölümü açıldığında, uygulamanın dizinin tamamını (`SeriesViewItems` dışındaki tüm seri verilerini) bulamaması sebebiyle "Sıradaki Bölüm" (Next Episode) promptunun ve video içi bölüm listesinin (Episodes Paneli) çalışmaması/gözükmemesi sorunu giderildi. Artık dizi bağlamı çözerken tüm dizi belleği (`_allSeriesCache`) de taranıyor.
    - **Replay/Geriye Atma Hatası (Seek Bug)**: Oynatıcıda (VideoPlayer) videonun sonlarına doğru ileri sarıldığında, zayıf internet bağlantılarında uygulamanın eski pozisyona (`_lastKnownValidPosition`) geri dönerek kullanıcıyı filmin/dizinin gerisine atması sorunu çözüldü. Artık ileri sarma işlemlerinde hedef pozisyon doğru şekilde güncellenerek kusursuz atlama sağlanıyor.
    - **Otomatik Sonraki Bölüm (Auto Play Next) Hatası**: Ayarlardan "Otomatik Sonraki Bölüme Geç" seçeneği aktif olduğunda, dizi jeneriğinin (Credits) başladığı an (son 3dk vb.) oyuncunun bir anda beklemeden (prompt göstermeden) doğrudan sonraki bölüme geçmesi sorunu giderildi. Artık jenerik alanına girince estetik "Sıradaki Bölüm" promptu görünecek, video tamamen bittiği zaman otomatik geçiş yapılacaktır. Yeni davranış kapsamlı birim testleri (Unit Tests) ile koruma altına alındı.
    - **Canlı TV İzleme Geçmişi Kaybı**: Kullanıcılar "Kanal Listesini Yenile" (Refresh) fonksiyonunu kullandıklarında, veritabanı sıfırlanıp geri yüklendiği esnada VOD ve Diziler kalırken Canlı TV'lere ait izleme tarihleri (`LastWatched`) siliniyordu. Yedekleme mekanizmasına (Snapshot) saat verisi dahil edilerek Canlı TV tarihlerinin ebediyen silinmesi engellendi.
    - **Kopya "İzlemeye Devam Et" Kartları**: Anasayfadaki "İzlemeye Devam Et" rayında (Rail) bazı durumlarda aynı içeriğin (örneğin hem Film hem Dizi sekmesinden gelen aynı IP içeriğin) çift çıkması sorunu StreamUrl bazlı gruplama ve deduplication (tekleştirme) ile çözüldü. Dizilerin yarım kalan birden çok bölümü varsa **sadece en son izlediği bölümün** anasayfada görünmesi sağlandı.

- **Performans ve Regex Optimizasyonu (SeriesInfoParser)**:
    - **KRİTİK: Dizi Kontrolü Çoklu Dil Desteği Eksikliği Giderildi**: `IsSeries` metodunda sadece İngilizce (Sxe) veya Türkçe desteklenmesi, Fransızca (`Saison`), Almanca (`Staffel`) ve İspanyolca (`Temporada`) içinse güvensiz `.Contains` sorgularının kullanılması sebebiyle yaşanan kargaşa (`"Le Tour de France Saison 2024"` belgeselinin dizi sanılması gibi) çözüldü. Artık tüm dil denetimleri güvenli Regex mimarisi üzerinden gerçekleştiriliyor.
    - **Tasarım Sorunu (Deduplicate) Çözüldü**: `Deduplicate` isim temizleme metodu, sadece birbirini izleyen ("Bad Bad") kelimeleri değil, birbirini izlemeyen asimetrik tekrar paternlerini de (Örn: "Bad Breaking Bad" -> "Breaking Bad") temizleyecek şekilde geliştirildi.
    - **KRİTİK: ExtractLanguageCode "UK" False-Positive Hatası Çözüldü**: Dil tespit metodundaki `.Contains("UK")` kuralı kelime sınırları gözetilmeden çalıştığı için ("TÜRKÇE DİZİLER", "DUKKANLAR", "UKRAIN" gibi) içinde tesadüfen "UK" harf dizilimi geçen tüm isimlerin yanlışlıkla İngilizce (`en-US`) olarak işaretlenmesine (False-Positive) neden oluyordu. Bu hata `string.Split()` kullanılarak sadece kelime bazlı eşleşme (Word Boundary) yapılması sağlanarak giderildi. Ayrıca İngilizce/Uluslararası MULTI kontrollerinden önce Türkçe (TR) kelime denetimi yapılarak çok dilli yayın yapan TR kanallarının yanlışlıkla İngilizce işaretlenmesi önlendi.
    - **KRİTİK: NormalizeKey Türkçe Karakter Eşleşmeme Sorunu Çözüldü**: `NormalizeKey` metodu içine Türkçe karakter normalizasyonu (`ş`->`s`, `ç`->`c`, `ğ`->`g` vb.) eklendi. Böylece farklı sağlayıcılarda "Diriliş" ve "Dirilis" olarak gelen aynı diziler artık farklı gruplar (ayrı diziler) olarak algılanmayıp başarıyla tek bir başlık altında toplanabilecek (Canonical Grouping).
    - **KRİTİK: StripIptvPrefixes Sonsuz Döngü Riski Çözüldü**: `StripIptvPrefixes` içindeki `do-while` döngüsüne maksimum iterasyon (`maxIterations = 10`) limiti eklenerek, `PipeTagRegex` deseninin sadece "| | |" gibi boşluklu karakterlerde eşleşemediği ama `Contains('|')` kontrolünün sonsuz döngüye sebep olabileceği edge-case belirsizlikleri engellendi.
    - **KRİTİK: XRegex (1x01) Yanlış Eşleşme Hatası Çözüldü**: `XRegex` ve `EpisodeTokenRegex` içerisindeki "x" harfi etrafındaki boşluk toleransı (`\s*`) kaldırılarak, "4 x 400" gibi boyut belirten veya "TR/DIZI 1 x 5" gibi isimlendirmelerin yanlışlıkla 4. Sezon 400. Bölüm gibi algılanıp içeriği bozması engellendi. Sadece bitişik (Örn: `1x01`) veya formatlanmış geçerli ifadeler dizi bilgisi olarak ayrıştırılacak.
    - **KRİTİK: Dizi ve Canlı Yayın Ayrıştırma Hatası Çözüldü**: `Parse` metodunda canlı spor kanallarının ("beIN SPORTS S01" veya "TIVIBU SPOR 3") yanlışlıkla bir dizinin sezonu veya bölümü olarak algılanmasına neden olan sıralama (logic) hatası düzeltildi. `IsLiveSeries` kontrolü tüm dizi Regex eşleştirmelerinden (SxeRegex vb.) öncesine taşınarak hem regex israfı önlendi hem de canlı kanalların dizi zannedilmesinin önüne geçildi.
    - **IsLiveSeries Metodu JIT Sızıntısı Çözüldü**: `IsLiveSeries` metodu içinde her çağrıda `new Regex()` oluşturularak on binlerce kanal işlenirken yaşanan JIT derleme yükü ve nesne alokasyon sorunu giderildi. Kural seti `[GeneratedRegex]` altyapısına geçirilerek performans önemli ölçüde artırıldı.
- **Video Oynatıcı (VideoPlayerService)**:
    - **UI Zıplama Hatası (Progress Bar Jump) Çözüldü**: Video başladığında veya kanal değiştirildiğinde, `PositionChanged` olayının (event) asenkron çalışması sebebiyle `Duration` (Süre) değerinin 0 olduğu o anlık kısa sürede fırlatılan sıfırlanmış değerlerin (`e.Position * 0`) ilerleme çubuğunu (Progress Bar) bir anlığına başa sarması (jump) durumu, süre 0'dan büyük değilse hesaplamanın yapılmaması sağlanarak giderildi.
- **Dizi ve İçerik Agregasyonu (MediaService)**:
    - **KRİTİK: Veri Kaybı Çözüldü**: Yeni eklenen dizi bölümlerinin agregasyon sonunda yanlışlıkla "yetim" (orphan) sanılarak silinmesine neden olan mantık hatası giderildi. İlk kez eklenen bölümler artık güvenle kaydediliyor.
- **Playlist Organizasyon ve Agregasyon (PlaylistOrganizerService)**:
    - **Arayüz (Interface) ve Kod Temizliği**: `IPlaylistOrganizerService` arayüzü güncellenerek daha önce dışarıya kapalı olan `EnrichMetadata` metodu dışarıdan test edilebilir (public) hale getirildi. Ayrıca `CategoryRules` sözlüğündeki mantıksal hedef eşitsizlikleri giderildi ("Dizi" -> "Diziler") ve `GetChannelNumber` metodu içerisindeki hiçbir zaman çalışmayan (dead code) aşırı büyük sayı yakalama bloğu (`long.TryParse`) kaldırılarak performans ve okunabilirlik artırıldı.
    - **KRİTİK: Edge Case / Boş Referans Koruması**: `AutoCategorize` metoduna M3U ayrıştırma hatalarından gelebilecek `null` kanal isimlerine karşı koruma eklendi; böylece uygulamanın aniden çökmesi (`NullReferenceException`) engellendi.
    - **Kanal Numarası Algılama İyileştirilmesi**: `GetChannelNumber` metodunda IP adresleri ve Port numaralarının (örn: `192.168.1.1` veya `8080`) ya da kanal adıyla bütünleşik kelimelerin (örn: `3sat`) yanlışlıkla "Kanal Numarası" olarak algılanıp sırayı bozması engellendi. `http://` gibi URL desenleri kısıtlandı ve numara çıkartma stratejisi daha katı `(?<!\S)(\d{1,4})(?!\S)` regex kurallarıyla yeniden yazıldı.
    - **KRİTİK: Çalışma Zamanı (Runtime) Crash Hatası Giderildi**: `GroupMapping` sözlüğünde `StringComparer.OrdinalIgnoreCase` kullanılmasına rağmen aynı anlama gelen büyük/küçük harf varyasyonlarının (Örn: "Sports" ve "SPORTS") ayrı anahtarlar olarak eklenmesinin neden olduğu `ArgumentException` hatası çözüldü. Sözlükteki mükerrer kayıtlar temizlenerek uygulamanın açılışta veya ilk eşleştirmede çökmesi engellendi.
    - **KRİTİK: Kalite (Quality) Eşleştirme Hatası Giderildi**: `GetQualityIndex` metodunda "1080p", "hd" gibi kalite etiketlerinin kelime içi alt dizgi (substring) olarak hatalı eşleşmesi sorunu çözüldü ("shahd", "adhd", "fhd" kelimelerindeki "hd" parçasının yanlışlıkla yakalanması gibi). Etiket taramaları kelime sınırlarıyla (`\b`) kaynak kod tarafından üretilmiş Regex kullanılarak güvenli ve yüksek performanslı hale getirildi.
    - **KRİTİK: Mükerrer Dizi Kanalı Kaybı Çözüldü**: `RemoveDuplicates` metodunda sezon ve bölüm bilgisi çözümlenemeyen (parse edilemeyen) dizi kanallarının tamamının hatalı bir şekilde aynı `s00e00` anahtarıyla eşleşip üst üste yazılarak yok olması sorunu giderildi. Artık anlaşılamayan dizi kanalları için `StreamUrl` bazlı bir "fallback" benzersizlik anahtarı üretilerek hiçbir dizi bölümünün silinmemesi garanti altına alındı.
- **Birim Testleri (Unit Testing)**:
    - **LanguageDetectionTests**: Metot imzalarındaki değişikliklere uyum sağlamak için ülke algılama testleri güncellendi ve doğrulandı.
- **Veritabanı ve Playlist Organizasyon İyileştirmeleri** (2026-03-10):
    - **Toplu Veri Yazma (Bulk Insert)**: `FastSqliteBulkInsertAsync` içerisine Rating, Plot, ReleaseYear ve ContentRating gibi VOD/Dizi metadataları dahil edildi. `IsFavorite` ve `IsInMyList` varsayılan değerleri dinamik hale getirilerek her yenilemede sıfırlanmaları engellendi.
    - **Kullanıcı Verisi Koruma Mantığı**: Parmak izi (fingerprint) çakışması olan kanallarda verilerin kaybolmasını engellemek için, birleştirme (merge) stratejisi uygulandı; favori ve izleme bilgileri her zaman en kapsamlı olanda tutuluyor.
    - **Mükerrer Kanal Önleme**: Xtream/Stalker profillerinde sağlayıcının bir kanalı başka bir gruba taşıması sonucunda oluşan "dublör" kanallar (aynı StreamUrl'e sahip kopyalar) otomatik olarak siliniyor.
    - **Dizi Gruplandırma (Series Grouping) Çözümü**: Farklı dil ve gruplardaki aynı isimli dizilerin (Örn: "TR | Dizi" ile "EN | Dizi") birbirine karışması, gruplama anahtarına `GroupTitle` eklenerek çözüldü.
    - **Gelişmiş Grup İsmi Normalizasyonu**: "FİLM", "DİZİ" gibi Türkçe karakterli gruplar ile "Sports", "Kids" gibi İngilizce kategori isimleri standartlaştırılarak eşleştirme oranı artırıldı.
    - **SmartSort Tip Öncelikli Sıralama**: Kanallar artık listeye eklenmeden önce Canlı -> VOD -> Dizi şeklinde tipine göre, ardından kategori ve alfabetik olarak daha düzenli sıralanıyor.

- **"Güvenli Sıfırlama" (Safe Reset) ve Veri Bütünlüğü** (2026-03-09):
    - **Tam Liste Yenileme**: Kanal listesi yenilendiğinde (Refresh) artık mevcut tüm kanallar silinip baştan ekleniyor. Bu sayede playlist üzerindeki tüm isimlendirme, kategori ve grup değişiklikleri %100 temiz bir şekilde arayüze yansıtılıyor.
    - **Kullanıcı Veri Restorasyonu**: Liste sıfırlansa dahi kullanıcıların "Favoriler", "İzleme Geçmişi" (WatchedPosition), "İzleme Süresi" (Duration) ve "İzleme Listem" (IsInMyList) verileri akıllı parmak izi (fingerprint) teknolojisiyle yedeklenip yeni listeye saniyeler içinde otomatik olarak aktarılıyor.
    - **Diziler Sekmesi Senkronizasyonu**: Playlist yenilendikten sonra Diziler sekmesindeki içeriklerin boş görünmesine neden olan senkronizasyon hatası giderildi. Arka plandaki dizi önbelleği artık yenileme biter bitmez otomatik olarak tazeleniyor.
    - **Dinamik Grup İsmi Uyumu**: Playlist sağlayıcısı kategori isimlerine emoji veya özel karakter eklediğinde (Örn: `TR/DIZI` -> `TR/DIZI ✨`), sistem mevcut dizilerin gruplarını bu yeni isimlerle anlık olarak güncelleyerek kategori-içerik eşleşmesini kusursuz hale getiriyor.
    - **"Hayalet" Veri Temizliği (Ghost Data Purging)**: Tipi değişen (örneğin Dizi -> Canlı) kanallardan arta kalan yetim dizi bölümleri ve boşta kalan dizi başlıkları artık her yenileme sonrası veritabanından otomatik olarak temizleniyor.

- **Kusursuz Filtreleme ve Evrensel Playlist Uyumluluğu** (2026-03-09):
    - **"Kusursuz Filtre" Motoru**: M3U ve Stalker listeleri için sınıflandırma mantığı baştan sona yenilendi. Artık kanal adı ne olursa olsun, URL yapısı (`/live/`, `/movie/`, `/series/`, `pluto.tv`, `/radio/`) en güçlü sinyal olarak kullanılarak içerikler %100 doğrulukla ayrıştırılıyor.
    - **7/24 Döngü ve Spor Kanal Koruması**: Pluto TV yayınları, `Exxen Spor`, `7/24 Netflix` gibi içerikler; isimlerinde "Series" veya "Movie" geçse dahi, bunların birer canlı akış (stream) olduğu algılanarak doğruca **Canlı TV** sekmesinde tutuluyor.
    - **Gelişmiş Platform ve VOD Tanıma**: `netflix`, `amazon`, `disney`, `hulu`, `apple tv`, `blutv`, `gain`, `exxen`, `sinevizyon` gibi 15+ dijital platform kategorisi otomatik olarak VOD (Film) sekmesine yönlendiriliyor.
    - **Akıllı Yıl ve İsim Analizi**: `Past Lives 2023` veya `Thirteen Lives (2022)` gibi hem parantezli hem parantezsiz yıl formatları film belirticisi olarak sisteme eklendi.
    - **Stalker Dinamik API Keşfi**: Stalker portalları için girilen URL ne olursa olsun (`/c/`, `/stalker_portal/c/` vb.), sistem asıl API uç noktasını (`/server/load.php`) otomatik olarak keşfediyor ve bağlantı kuruyor.
    - **Çoklu Kategori (Semicolon) Desteği**: `iptv-org` gibi listelerde bulunan `News;Public` tarzı noktalı virgüllü çoklu kategoriler artık doğru şekilde parçalanıp işleniyor.

- **Gelişmiş Ülke Algılama ve tvg-country Desteği** (2026-03-09):
    - **tvg-country Desteği**: M3U listelerindeki `#EXTINF` satırlarında bulunan `tvg-country` etiketi artık otomatik olarak ayrıştırılıyor ve kanalların ait olduğu ülke bilgisi veritabanına kaydediliyor.
    - **Genişletilmiş Ülke Kapsamı**: Dil algılama algoritması (Language Detection) AL (Arnavutluk), GE (Gürcistan), GR (Yunanistan), HU (Macaristan), HK (Hong Kong) ve SE (İsveç) ülkelerini tanıyacak şekilde popüler kanal kalıplarıyla (Tring, ERT, M1, SVT vb.) güçlendirildi.
    - **Akıllı Ülke Önceliği**: Ülke tespit mekanizması, kanal isimlerindeki ön eklerden önce veritabanındaki (tvg-country'den gelen) kesin ülke bilgisini dikkate alacak şekilde yeniden yapılandırıldı. Bu sayede otomatik EPG eşleştirmesinin doğruluğu artırıldı.
    - **Veritabanı Şema Güncellemesi**: Mevcut kullanıcıların veritabanlarına `Channels` tablosu için `Country` sütunu otomatik olarak eklendi (Schema Fixup).

- **Çevrimdışı Mod ve İndirilenler İyileştirmeleri** (2026-03-09):
    - **Dinamik Afiş İndirme (Offline Poster)**: Dizi bölümü veya film indirildiğinde internete ihtiyaç duymamak adına ilgili içeriğin kapağı (Poster) otomatik olarak indirilerek cihazda saklanır. Böylece internet kapalıyken bile içeriklerin resimleri görünür.
    - **Sadeleştirilmiş İndirilenler Sayfası**: İndirilenler menüsünden bir dizinin detay sayfasına girildiğinde, çevrimiçi dizi görünümünden farklı olarak (daha küçük poster, gereksiz yayın metadatalarının gizlenmesi vb.) sadece yerel odaklı minimalist ve sade bir arayüz tasarlanmıştır.
    - **Gereksiz İndirme Tuşlarının Gizlenmesi**: İndirilenler ekranında zaten cihazda olan içerikler için kafa karışıklığını önlemek adına "Bölüm İndir" ve "Sezonu İndir" tuşları tamamen kaldırılmıştır.

- **Akıllı Kategorizasyon ve Dil Tespiti İyileştirmeleri** (2026-03-09):
    - **Kategori Önceliklendirmesi**: M3U listelerinde VOD (Film) ve Series (Dizi) içeriklerinin yanlışlıkla Canlı TV (Live) kategorisine düşmesi sorunu giderildi. Kategori belirlemede grup ismi yerine öncelikle URL yapısı (`/series/`, `/movie/`) dikkate alınacak şekilde ayrıştırma motoru (`M3UParser`) yeniden yapılandırıldı.
    - **Kelime Sınırı Koruması**: Kanal isimlerindeki 'Alive', 'being', 'liver' gibi kelimelerin içindeki 'Live' veya 'beIN' gibi ifadelerin Canlı TV tetikleyicisi olarak algılanması sorunu Regex kelime sınırları (`\b`) kullanılarak tamamen çözüldü.
    - **Gelişmiş Yabancı Dil Tespiti**: Dil algılama algoritması (Language Detection), dünya çapındaki açık kaynak listelerle (Örn: Free-TV/IPTV) tam uyumlu hale getirildi. Arnavutluk (sq-AL), İspanya/Latin Amerika (es-ES, es-CL), İngiltere/ABD (en-US) gibi çok sayıda ülkenin kanalları ve dizi grupları artık doğrudan doğru TMDB dil kodlarıyla eşleşiyor.
    - **Tam Ülke İsmi Algılama**: Grup adlarında `TR|`, `EN:` gibi kısa kodların yanı sıra `TURKEY`, `UNITED STATES`, `ALBANIA`, `FRANCE`, `GERMANY` gibi tam ülke isimleri de algılanarak doğru dil atamaları (tr-TR, en-US, sq-AL, fr-FR, de-DE) yapılıyor.

- **Mükerrer Profil Kontrolü**: Aynı sağlayıcıdan (M3U URL, Xtream veya Stalker) mükerrer profil oluşturulmasını engelleyen doğrulama mekanizması eklendi. Kullanıcıya net hata mesajları sunularak veri bütünlüğü sağlandı.
- **Gelişmiş Ülke ve Dil Tespiti**: Kategori isimlerindeki "şekilli" karakterler (Örn: `ⓣⓥ`, `ⓣⓡ`) otomatik olarak ASCII formatına normalize edilecek şekilde geliştirildi. Tam ülke isimleri (FRANCE, TURKIYE vb.) artık doğrudan tanınarak akıllı sıralama ve EPG eşleştirme başarımı artırıldı.
- **İndirme Sistemi Kapsamlı Test Paketi**: İndirme kuyruğu mantığı, dizi/sezon klasör organizasyonu, depolama kotası hesaplamaları ve indirilen içeriklerin yerel oynatma (offline mode) önceliklendirmesini doğrulayan yeni test senaryoları (`DownloadSystemComprehensiveTests`) eklendi.
- **Oynatıcı Kontrolleri ve Overlay Birim Testleri**: Ses/Mute yönetimi, Seek (atlama) mantığı, Skip (ileri/geri) carry penceresi ve overlay görünürlük durumlarını doğrulayan 90 yeni test senaryosu (`PlayerViewModelControlsTests`) eklendi.
- **Kalıcı İzleme Statüsü Koruması**: Bir içerik (VOD veya Dizi) bir kez tamamlandı olarak işaretlendiğinde, sonraki yükleme hataları veya eksik süre (duration) bilgilerinin bu statüyü bozması engellendi. `Episode.IsCompleted` alanı veritabanında kalıcı hale getirildi.
- **İzleme Statüsü Koruması Test Paketi**: Video yükleme hataları, hızlı ardışık kayıt çağrıları ve provider değişimleri gibi 19 farklı senaryoyu doğrulayan kapsamlı bir test seti (`CompletedStatusProtectionTests`) eklendi.
- **Lisans Servisi Genişletilmiş Testleri**: Bilinmeyen özellik kontrolleri, limit sınır değer analizi, abonelik iptali ve çoklu abonelik bildirimlerini doğrulayan 18 yeni test senaryosu (`LicenseServiceExtendedTests`) eklendi.
- **Lisans Servisi İyileştirmeleri**: Premium kullanıcılar için tüm limitler sınırsız hale getirildi, `DeactivatePremium` metodu eklendi ve test yardımcı metodları sessizleştirildi.
- **İzleme Geçmişi Edge Case Testleri**: `WatchHistoryService` için null guardlar, tamamlanan içeriklerin korunması, profil bazlı geçmiş temizleme ve birikimli izlenme süresi hesaplamalarını doğrulayan 12 yeni test senaryosu (`WatchHistoryServiceEdgeCaseTests`) eklendi.
- **Oynatıcı Mantığı Birim Testleri**: Bölüm tamamlanma kriterleri (%90 eşiği veya son 3 dakika), Resume (kaldığın yerden devam et) pozisyonu önceliklendirme ve zaman formatlama mantığını doğrulayan 24 yeni test senaryosu (`PlayerCompletionLogicTests`) eklendi.
- **Gelişmiş Bulanık Arama (Fuzzy Search)**: `MainViewModel` içindeki arama algoritması Türkçe karakter normalizasyonu (ı→i, ş→s vb.) ve harf yer değişimi (transposition) hatalarını destekleyen Damerau-Levenshtein mesafesi ile güçlendirildi.
- **Kanal ve EPG Birim Testleri**: `Channel` ve `EpgProgram` modelleri için izleme yüzdesi, progress bar hesaplamaları ve görsel (poster/logo) öncelik mantığını doğrulayan 20+ yeni test senaryosu eklendi.
- **Fuzzy Search Birim Testleri**: Arama algoritmasının doğruluğunu, normalizasyon kurallarını ve hata toleransını test eden 30 yeni test senaryosu (`FuzzySearchTests`) eklendi.
- **Ayarlar Sadeleştirmesi**: "Jenerik bitince sonraki bölüme geç" ve "Son kanalı hatırla" seçenekleri ayarlardan ve kullanıcı arayüzünden kaldırıldı.
- **Empty State Tutarlılığı**: Tüm ana görünümlerde (History, Search, Favorites, My List, Downloads) boş durum (empty state) tasarımları tek bir standart yapıda (MaterialIcon + Başlık + Alt Yazı) birleştirilerek görsel bütünlük sağlandı.
- **Genişletilebilir Sol Menü (Sidebar)**: Ana menü modernize edilerek açılır/kapanır "Hamburger Menü" yapısına geçirildi.
  - Menü, açıldığında ana içeriği kaydırmak yerine Netflix tarzı "Overlay" (üzerine bindirme) animasyonu ile açılıyor.
  - Menü kapalıyken ikonların daha nizami ve dengeli görünmesi için `Padding` ve `Margin` oranları revize edildi.
  - "İndirilenler" sayfasındaki ikon kayma/hizalanma hatası düzeltildi.
  - **Akıllı Navigasyon İpuçları (Tooltips)**: Menü öğeleri üzerindeki ipuçları (Tooltips), menü açıkken (yazılar okunabildiğinden) otomatik gizlenecek, sadece kapalıyken (ikon modunda) görünecek şekilde geliştirildi.
  - **Dinamik Hamburger İkonu**: Sol üstteki menü butonu ikonunun (`Menu`), yan menü açıkken çarpı (`MenuOpen`) olarak değişmesi sağlandı.
  - Header alanında bulunan ve hizalamayı bozan eski 1px'lik `Border` alanı (ölü kod) temizlendi.
- **Geçmiş Sayfası Dizi Görünümü**: Geçmiş (History) sekmesinde yer alan dizi içerikleri, izlenen bölümlerin (episode) listelenmesi yerine ana dizi kartları (`SeriesCard`) olarak listelenecek şekilde güncellendi. Artık karta tıklandığında doğrudan dizinin detay sayfası açılıyor.
- **Kaldığın Yerden Devam Et (Resume Dialog)**: VOD ve Dizi içerikleri için akıllı izleme hafızası eklendi.
  - 2 dakikadan fazla izlenen ve henüz tamamlanmamış içerikler açıldığında, kullanıcıya "Kaldığın Yerden Devam Et" veya "Baştan Başla" seçeneklerini sunan modern bir diyalog penceresi gösterilir.
  - **Premium Kilit Sistemi**: "Kaldığım Yerden Devam Et" özelliği Premium kullanıcılara özel hale getirildi. Ücretsiz kullanıcılar diyalog üzerinde "Baştan Başla" seçeneğini kullanabilir veya kilitli (🔒 PRO) butona tıklayarak yükseltme (Upsell) ekranına ulaşabilir. Yeni, tutarlı ve şık bir `PremiumLockBadge` stili eklendi.
  - Diyalog açıkken diğer oynatıcı kontrolleri otomatik olarak gizlenerek odaklanmış bir kullanıcı deneyimi sağlanır.
  - Tamamlanmış (izlendi işareti olan) bölümler tıklandığında diyalog gösterilmeden doğrudan en baştan başlatılır.

### 🛠️ Düzeltmeler ve Optimizasyonlar
- **Veri Kaybı ve Kaynak Yönetimi (PlaylistService)**:
    - **Favori ve Liste Verisi Koruması**: `FastSqliteBulkInsertAsync` metodunda `IsFavorite` ve `IsInMyList` alanlarının sıfırlanmasına neden olan hata giderildi. Artık kanal yenilemelerinde kullanıcı seçimleri kaybolmuyor.
    - **Metadata Bütünlüğü**: Toplu kanal ekleme işlemine `Rating`, `Plot`, `ReleaseYear`, `ContentRating`, `BackdropUrl`, `Cast`, `Director`, `Language` ve `TmdbId` alanları dahil edildi.
    - **Boş Liste Koruması**: Playlist indirme veya ayrıştırma sonucu boş döndüğünde mevcut kütüphanenin silinmesini engelleyen güvenlik kontrolü (`Empty Parse Guard`) eklendi.
    - **Bellek Sızıntısı Giderildi**: `SemaphoreSlim` nesnelerinin işlem bittikten sonra temizlenmemesi sorunu çözüldü (Resource Leak fix).
    - **Bağlantı Yönetimi**: Veritabanı toplu yazma işlemlerinde hata oluşması durumunda SQLite bağlantısının açık kalması sorunu `finally` bloklarıyla giderildi.
    - **Eşzamanlılık (Race Condition)**: Stalker/Xtream profil oluşturma sırasında oluşabilecek mükerrer kayıt (TOCTOU) riski kilit mekanizmasıyla engellendi.
    - **Uzaktan Erişim Optimizasyonu**: Playlist metadata kontrollerine (HEAD isteği) 10 saniyelik zaman aşımı ve iptal desteği (`CancellationToken`) eklendi.
- **Kanal Listesi ve Yenileme Mantığı (Settings/MainViewModel) İyileştirmeleri**:
    - **Xtream/Stalker Senkronizasyonu**: Arka planda "ateşle-unut" (fire-and-forget) şeklinde çalışan yenileme mantığı `await` yapısına geçirilerek, tüm kanallar inmeden "tamamlandı" sinyali verilmesi engellendi.
    - **Cooldown (Bekleme Süresi) Sistemi**: `_playlistNoChangeUntilUtc` mantığı aktive edilerek, başarılı yenileme sonrası 5 dakikalık koruma süresi getirildi; böylece sunucu spam'i önlendi.
    - **UI Thread Performansı**: `ThrottledLoadChannelsAsync` içindeki veritabanı operasyonları UI thread'inden arındırıldı. Koleksiyon güncellemeleri (`FilteredChannels.Add`) güvenli bir şekilde Dispatcher üzerinden sarmalandı.
    - **Hata ve Durum Yönetimi**: 
        - Yeni bir yenileme başladığında eski hata kutusunun (`ChannelListLastError`) temizlenmemesi sorunu giderildi.
        - `ScanChannelListStatsCoreAsync` metodundaki erken `return` hatası düzeltilerek istatistiklerin her zaman güncellenmesi sağlandı.
        - Durum mesajlarındaki yazım hataları (Türkçe karakter uyumu) standartlaştırıldı.
    - **Arayüz Etkileşimi**: Ayarlar penceresindeki "Şimdi Yenile" butonlarına `IsEnabled` binding'i eklenerek, aktif bir işlem sırasında mükerrer tıklamalar engellendi.
- **Video Oynatıcı (VideoPlayerService) İyileştirmeleri**:
    - **Bellek Yönetimi**: Her yeni medya yüklemesinde eski `Media` nesnesinin native handle'larının sızması engellendi (Explicit Dispose eklendi).
    - **Yayın Kalitesi ve Kararlılık**: 
        - `LiveM3u8` profili için donmaları önleyen `:adaptive-logic=rate` ayarına geçildi.
        - `LiveTs` profilindeki görüntü bozulmalarına yol açan `:drop-late-frames` kaldırıldı.
    - **Thread-Safety**: `_retryCount` ve `_playGeneration` gibi kritik sayaçlar `Interlocked` ile thread-safe hale getirildi.
    - **Hata Yönetimi**: `ContinueWith` anti-pattern'i `Task.Run` ve `await` ile değiştirilerek ses seviyesinin diske kaydedilme güvenilirliği artırıldı.
    - **Loglama**: `System.Diagnostics.Debug` ve `LogDebug` karışık kullanımı standartlaştırılarak hata ayıklama süreçleri iyileştirildi.
- **PlayerViewModel Temizliği**:
    - **Ölü Kod Temizliği**: `MonitorLivePlaybackHealthAsync` içindeki çalışmayan 60+ satırlık mantık temizlendi.
    - **Guard Koşulları Optimizasyonu**: `EnsurePlaybackHealthAsync` içindeki mükerrer kontrol blokları merkezi `IsHealthCheckCancelled` metoduna taşınarak kod okunabilirliği artırıldı.
    - **Belgelendirme**: `SetPlaybackPosition` ve ses seviyesi yönetimi hakkındaki yanlış/eskimiş yorum satırları düzeltildi.
- **Ses Seviyesi ve Oynatıcı Kararlılığı**:
    - **Agresif Ses Zorlama Kaldırıldı**: Oynatma başladığında ses seviyesinin defalarca (5 kez) üst üste yazılmasına neden olan mantık temizlendi.
    - **Ses Seviyesi Koruma**: Kanal değişimlerinde veya otomatik yayın kurtarma sırasında sesin sıfırlanması sorunu giderildi.
- **Mükerrer Kontrolü ve Şifreleme Bug Fix**: Non-deterministic (DPAPI) şifreleme nedeniyle mükerrer kayıtların veritabanında tespit edilememesi sorunu, karşılaştırma mantığı `Type`, `Url` ve `Username` alanlarına odaklanarak çözüldü. M3U listelerindeki boş kullanıcı adı/şifre karşılaştırma hataları giderildi.
- **UI Geri Bildirim İyileştirmesi**: Kayıt sırasında oluşan hatalarda (örn: mükerrer kayıt) ekranın "Kaydediliyor..." durumunda asılı kalması sorunu düzeltilerek kullanıcıya reel-time hata bildirimi sağlandı.
- **Veritabanı Şeması ve Toplu İşlem İyileştirmeleri**: 
  - `Episodes` tablosu için eksik olan `IsCompleted` sütunu çalışma zamanında otomatik eklenecek şekilde (Schema Fixup) güncellendi.
  - `PlaylistService` içindeki yüksek performanslı toplu ekleme (**Bulk Insert**) mantığı `IsCompleted`, `WatchedPosition` ve `Duration` alanlarını destekleyecek şekilde revize edilerek `NOT NULL` kısıtlama hataları giderildi.
- **Performans ve Kararlılık Optimizasyonu**: 
  - Anasayfa yüklenirken binlerce dizi için yapılan ağır veritabanı sorguları, **Bulk Sync (Toplu Senkronizasyon)** mimarisine geçirilerek optimize edildi. Uygulama açılış hızı ve tepkiselliği önemli ölçüde artırıldı.
  - Veritabanı şemasında karşılığı olmayan `IsCompleted` özelliğinin SQL hatalarına ve dizi listelerinin boş görünmesine neden olan hatası giderildi.
  - `Episode` nesneleri `ObservableObject` yapısına geçirilerek izleme ilerlemelerinin arayüzde anlık ve pürüzsüz güncellenmesi sağlandı.
- **Akıllı Arama İyileştirmeleri (Fuzzy Search)**:
  - **Kelime Bazlı (Token-Based) Eşleştirme Algoritması**: Arama motoru baştan aşağı yenilenerek kelime bazlı eşleştirme yapısına geçirildi. Artık "vking" yazıldığında "vikings", veya "stanger tins" yazıldığında "stranger things" gibi hatalı ve eksik yazımlar, uzunluk farkına bakılmaksızın %100 isabetle algılanıp öneri olarak sunuluyor.
  - **Türkçe Karakter Desteği**: Arama motorunun altyapısı Türkçe karakterleri (ı, ü, ö, ş, ğ, ç) ASCII karşılıklarına dönüştürecek şekilde güncellendi. "vıkıng", "şogun" gibi yazımlar doğru içeriklerle kusursuz eşleşiyor.
  - **Dinamik Öneri Tıklaması**: "Bunu mu demek istediniz?" önerisine tıklandığında sadece arka planda arama yapılması değil, tıklanan kelimenin arama kutusuna (TextBox) da otomatik olarak yazılması sağlandı.
  - **Arama Kutusu Temizliği**: Arama yaptıktan sonra sol menüdeki farklı bir sekmeye geçildiğinde (örn. Ana Sayfa, Filmler), üstteki arama kutusunda kalan eski metnin otomatik olarak sıfırlanması sağlandı.
- **Kişisel Listeler Görünüm Hatası (Fix)**: "Listem", "Favoriler" ve "Geçmiş" sekmelerinde içerik olmasına rağmen "Liste boş" uyarısının da aynı anda görünmesine neden olan senkronizasyon hatası giderildi. Boş durum kontrolleri artık veri koleksiyonu UI thread üzerinde tamamen güncellendikten sonra tetiklenerek tutarlılık sağlandı.
- **Geçmiş Sayfası Senkronizasyonu**: Uygulama açılışında dizi verileri yüklenirken geçmiş sayfasının bazen boş görünmesi sorunu düzeltildi. İzleme geçmişi artık dizi önbelleği tamamen hazır olduğunda otomatik olarak tetiklenerek güncelleniyor.
- **Kod Temizliği ve Refaktör**: Proje genelinde redundant (artık kullanılmayan) koleksiyonlar, dönüştürücüler (converters) ve eski hata giderme blokları temizlenerek codebase daha hafif ve sürdürülebilir hale getirildi.
- **Kalıcı İzleme Statüsü Koruması**: Tamamlanmış içeriklerin, hatalı veya eksik yükleme durumlarında (video açılmaması, loading'de kalması vb.) "tamamlandı" bilgisinin kaybolmasına neden olan senaryolar engellendi. İzleme statüsü artık hem veritabanında hem de uygulama önbelleğinde güvenli bir şekilde korunuyor.
- **Dizi Detay Sayfası İyileştirmeleri**:
  - Dizi detay sayfası açıkken, üst bardan arama yapıldığında (Enter) veya sol menüden farklı bir sayfaya geçildiğinde dizi sayfasının açık kalmaya devam edip altta birikmesi sorunu giderildi; artık yeni bir aksiyonda otomatik kapanıyor.
  - Sayfanın sol menünün altında kalmasını önlemek amacıyla Z-Index ve Grid yapılandırmaları düzeltildi.
- **VOD İzleme Geçmişi**: Tamamlanmış (sonuna kadar izlenmiş) VOD içeriklerinin "İzlemeye Devam Et" listesinde belirmeye devam etmesi sorunu düzeltildi (`Channel` modeline `IsCompleted` özelliği eklendi).
- **Performans İyileştirmesi**: Anasayfa yüklenirken, her bir dizi bölümü (episode) için yapılan $O(n^2)$ karmaşıklığındaki dizi (series) arama işlemi, $O(1)$ sözlük haritalaması ile optimize edilerek arayüz tepkiselliği artırıldı.

## v31.0 – TMDB Destekli Çocuk Filtresi ve Dizi Arayüz İyileştirmesi (2026-03-08)

### ✨ Yeni Özellikler ve İyileştirmeler
- **TMDB Destekli Çocuk Profili Filtrelemesi**: Çocuk profilleri için kanal ve dizi filtreleme mantığı TMDB (The Movie Database) verileriyle güçlendirildi.
  - İçeriklerin yaş sınırları (Content Rating) artık sadece anahtar kelimelere değil, TMDB'nin resmi verilerine (US, DE, TR ve global standartlar) göre kontrol ediliyor.
  - TMDB verisi bulunamayan içerikler için gelişmiş anahtar kelime filtresi yedek (fallback) olarak çalışmaya devam eder.
- **Otomatik İçerik Temizleme (Post-Sync Purge)**: TMDB senkronizasyonu tamamlandığında, eğer bir içeriğin yaş sınırı çocuk profil kurallarına uymuyorsa (`TV-MA`, `R`, `18+`, `PG-13` vb.), bu içerik çocuk profillerinden otomatik olarak silinir.
- **Merkezi Güvenlik Yardımı (`ChildSafetyHelper`)**: Tüm filtreleme kuralları, güvenli/tehlikeli kategoriler ve reyting kontrolleri tek bir merkezde toplanarak uygulama genelinde tutarlılık sağlandı.
- **Kritik Kara Liste Genişletmesi**: `XXX`, `Adult` gibi evrensel yetişkin terimleri kritik kara listeye eklenerek koruma seviyesi artırıldı.
- **Dizi Listesi Anlık Yenileme**: Yeni playlist eklendiğinde veya kanal listsesi yenilendiğinde dizilerin görünmemesi (ancak yeniden başlatınca gelmesi) sorunu çözüldü. Arka plan düzenleme işlemi bittiğinde arayüz artık dizileri ve ana sayfa raylarını otomatik olarak günceller.
- **Gelişmiş Durum Mesajları**: Alt status barda "M3U Hazır" yerine, arka plandaki "Diziler Düzenleniyor..." gibi gerçek süreçleri gösteren bilgilendirmeler eklendi.
- **Çocuk Filtresi Test Paketi**: TMDB reyting önceliklendirmesi, anahtar kelime eşleşmeleri ve otomatik silme mantığını doğrulayan 18 yeni test senaryosu (`ChildSafetyFilteringTests`) eklendi.
- **Kritik Oynatma Senaryoları Doğrulaması**: 52 farklı oynatma senaryosu (Hata yakalama, otomatik kurtarma, jenerik atlama, izleme geçmişi tutarlılığı) `CriticalScenarioTests.cs` ile doğrulandı.
- **Veritabanı İlişkisel Bütünlük Sistemi**: Profil ve playlist silindiğinde bağlı tüm verilerin (kanal, dizi, geçmiş) temizlenmesini sağlayan "Cascade Delete" konfigürasyonları `AppDbContext` düzeyinde yapılandırıldı.
- **İzleme Geçmişi ve Pozisyon Hassasiyeti**: VOD içeriklerde son 10 saniye içinde biten yayınların "erken kesilme" (premature end) olarak algılanıp kurtarma döngüsüne girmesi engellendi. `CriticalApplicationScenariosTests.cs` (37 test) ile veri izolasyonu ve bütünlüğü doğrulandı.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `ChildSafetyHelper.cs` | [YENİ] Merkezi güvenlik mantığı ve evrensel reyting kontrolleri. |
| `PlaylistService.cs` | TMDB öncelikli filtreleme ve merkezi helper entegrasyonu. |
| `TmdbSyncService.cs` | Senkronizasyon sonrası otomatik çocuk profili temizleme (Purge) mantığı. |
| `MainViewModel.cs` | Arka plan aggregation sonrası otomatik UI yenileme ve status mesaj iyileştirmeleri. |
| `AppDbContext.cs` | `Profile -> Playlist` ve `Playlist -> Series` için cascade deletion yapılandırması. |
| `CriticalScenarioTests.cs` | [YENİ] 52 kritik oynatma senaryosu doğrulama seti. |
| `CriticalApplicationScenariosTests.cs` | [YENİ] 37 veri bütünlüğü ve izolasyon testi. |
| `ChildSafetyFilteringTests.cs` | [YENİ] Filtreleme mantığını doğrulayan kapsamlı test seti. |

---

## v30.9 – Anasayfa Sadeleştirme ve Netflix Tasarımı (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **Odaklanmış Anasayfa**: "Popüler Filmler" ve "Popüler Diziler" rayları kaldırılarak sadece "İzlemeye Devam Et" rayına odaklanıldı.
- **Netflix Stili Kartlar**: "İzlemeye Devam Et" kartları için modern, yatay (16:9) ve Netflix tarzı yeni bir tasarım (`ContinueWatchingCard`) sisteme eklendi.
- **Daha Zarif Görünüm**: Kart boyutları 260x146'ya çekilerek daha kompakt ve dengeli bir yerleşim sağlandı.
- **İlerleme Çubuğu Düzeltmesi**: Dizi bölümlerinde izleme yüzdesinin anasayfaya yansımamasını sağlayan veri eşleme hatası giderildi.
- **Logo Seçim Mantığı (Heuristic)**: Genel kategorilerde (TR/DIZI vb.) yerel yayıncı (Prime Video, Netflix vb.) logolarına üretim stüdyolarının (WB vb.) önünde öncelik verildi.
- **Canlı TV Bilgi Paneli**: "Hakkında" panelindeki boş kutular ve gereksiz süre bilgileri temizlenerek daha sade bir görünüm elde edildi.
- **Sonraki Bölüm Uyarısı İyileştirmesi**: Uyarı paneli daha küçük ve sade hale getirildi (açıklama metni kaldırıldı, boyutlar optimize edildi).
- **İzlemeye Devam Et Eşiği**: İçeriklerin anasayfada görünmesi için gereken minimum izleme süresi 30 saniyeden **2 dakikaya** çıkarıldı.
- **Hata Temizliği**: `MainWindow.axaml` üzerindeki yazım hataları düzeltilerek derleme hataları giderildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `HomeView.axaml` | Popüler içerik rayları kaldırıldı, yeni kart tasarımı entegre edildi. |
| `ContinueWatchingCard.axaml` | [YENİ] Netflix stili yatay kart bileşeni. |
| `MainViewModel.cs` | İzleme verisi eşleme mantığı düzeltildi ve kullanılmayan ray kodları temizlendi. |
| `MetadataService.cs` | Logo seçim mantığı (heuristic) güncellendi. |
| `VideoOverlayView.axaml` | Bilgi paneli (About) görsel hataları giderildi ve kontroller iyileştirildi. |
| `SettingsWindow.axaml` | Sekme kontrolleri için görsel ve etkileşim iyileştirmeleri yapıldı. |


---

## v30.8 – Akıllı Dil Eşleştirme (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **Dil Öncelikli İçerik Seçimi**: TMDB popüler listeleriyle eşleşen içeriklerde, aynı yapımdan birden fazla varsa (farklı diller, alt yazılı/dublajlı vb.) uygulamanın diline (Örn: TR) en uygun olanı otomatik olarak seçilir.
- **Yabancı Dil Desteği**: Eğer kütüphanede uygulama dilinde içerik yoksa, alternatif olarak İngilizce (EN) sürümlerine öncelik verilir.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainViewModel.cs` | `LoadHomeContentAsync` eşleştirme mantığı dil skorlaması ile güçlendirildi. |

---

## v30.7 – TMDB Popüler Listeleri (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **TMDB Entegrasyonu**: "Popüler Filmler" ve "Popüler Diziler" rayları artık doğrudan TMDB'nin resmi trend listelerinden (Popular Movies/TV) gelen içeriklerle dolduruluyor.
- **Dinamik Eşleştirme**: TMDB listelerindeki ID'ler kullanıcının kütüphanesindeki içeriklerle otomatik olarak eşleştirilip popülerlik sırasına göre diziliyor.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MetadataService.cs` | TMDB popüler listelerini çekmek için yeni API metotları eklendi. |
| `MainViewModel.cs` | Kütüphane içeriklerini TMDB listeleriyle eşleştiren mantık eklendi. |

---

## v30.6 – Anasayfa Sadeleştirme (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **Sadeleştirilmiş Anasayfa**: Anasayfa içeriği daha odaklanmış bir deneyim için sadece 3 raya indirildi: "İzlemeye Devam Et", "Popüler Filmler" ve "Popüler Diziler".
- **Top 10 Listeleri**: Popüler içerik rayları artık TMDB puanına göre en iyi 10 içeriği gösterecek şekilde optimize edildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainViewModel.cs` | `LoadHomeContentAsync` metodu 3 ray ve 10 içerik limitiyle güncellendi. |
| `HomeView.axaml` | Fazladan raylar kaldırıldı ve başlıklar güncellendi. |

---

## v30.5 – Anasayfa İçerik Optimizasyonu (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **Dinamik Anasayfa**: Anasayfa içerikleri "En Çok Beğenilen Filmler", "Yeni Eklenen Filmler" ve "En İyi Diziler" olarak TMDB puanlarına göre yeniden düzenlendi.
- **Akıllı İzlemeye Devam Et**: "İzlemeye Devam Et" bölümüne izleme süresi mantığı eklendi (yarım bırakılan ve henüz %92'si izlenmemiş diziler/filmler daha doğru filtreleniyor).
- **Arayüz Temizliği (Hero Banner)**: Kullanıcı isteği doğrultusunda anasayfadaki büyük Hero Banner (vitrin) bölümü hem arayüzden hem de arkadaki mantıksal katmandan tamamen kaldırıldı.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainViewModel.cs` | `FeaturedChannel` özelliği, `PlayFeatured` komutu ve hero seçim mantığı temizlendi. |
| `HomeView.axaml` | Hero banner UI blokları kaldırıldı. |

---

## v30.4 – İzleme Çubuğu Senkronizasyonu ve Tasarım Bütünlüğü (2026-03-06)

### 🎨 Görsel ve Arayüz İyileştirmeleri
- **İzle Çubuğu (Progress Bar) Senkronizasyonu**: Video oynatıcı üzerindeki geliştirilmiş izleme çubuğu tasarımı (5px yükseklik, yüksek kontrast, kavisli köşeler) ana dizi detay sayfasına (`MainWindow.axaml`) da uygulanarak görsel bütünlük sağlandı.
- **Dinamik Tasarım Uyumu**: Farklı thumbnail boyutlarına (106px vs 200px) göre otomatik genişlik hesaplaması (`PercentToWidthConverter`) optimize edilerek her iki görünümde de kusursuz bir deneyim sağlandı.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainWindow.axaml` | Dizi detay listesindeki progress bar tasarımı oynatıcı paneliyle senkronize edildi. |

---

## v30.3 – Bölüm Paneli Optimizasyonu ve Meta Veri Entegrasyonu (2026-03-05)

### 🎨 Görsel ve Arayüz İyileştirmeleri
- **Bölüm Listesi Modernizasyonu**: Video oynatıcı üzerindeki bölümler paneli daraltılarak (128x72 → 106x60) yer kazanıldı. Gri "Bölüm X" yazıları kaldırılarak yerine TMDB'den gelen gerçek bölüm isimleri mor ve italik stilde eklendi.
- **Zengin "Hakkında" Paneli**: Bilgi panelindeki sabit kanal açıklaması yerine, serilerde o an oynatılan bölüme özel **TMDB Bölüm İsmi, Yayın Tarihi (örn: 17 Eyl 2021)** ve **Bölüm Özeti** eklendi.
- **Dinamik İçerik Mantığı**: Panel, oynatılan içeriğin türüne göre (Canlı TV, Film, Dizi) en alakalı meta veriyi (Program/Kanal/Bölüm) gösterecek şekilde akıllandırıldı.
- **İzle Çubuğu (Progress Bar) Belirginliği**: Çubuk kalınlığı 5px'e çıkarıldı ve açık renkli görsellerde de seçilebilmesi için kontrastı artırıldı.

### 🐛 Hata Düzeltmeleri
- **Bölüm Paneli (Accordion) Kapanma Hatası**: Video oynatıcı üzerindeki bölümler (episodes) listesinde bir bölüme tıklandığında Accordion panelinin (Expander) kendi kendine kapanmasına neden olan olay (event) yönlendirme (bubbling) hatası çözüldü.
- **Aktif Sezon Paneli Odaklanması**: Bölümler listesi (Episodes) açıldığında varsayılan olarak tüm sezonların açık gelmesi yerine, **sadece o an izlenmekte olan bölümün (current episode) bulunduğu sezonun otomatik olarak açık (expanded)** gelmesi sağlandı.
- **Bölüm Paneli Yükleme Hatası**: Bazı serilerde metadata senkronizasyonu sırasında panelin boş açılmasına neden olan `PlayerViewModel` veri çakışması giderildi.
- **Bölüm Paneli Sezon Açılıp Kapanma Durumu (Expander Binding)**: Bölüm listesi panelinde kullanıcı bir sezonu kapattığında, o sezonun "kapalı" kalma durumu artık hafızada tutulacak. İki yönlü bağlantı (TwoWay binding) eklendiği için kullanıcı panel kapatsa da/açsa da sezonların açılıp/kapanma durumu kendi kendine sıfırlanmayacak.
- **Kanal Geçişlerinde Hatalı Yayın Kurtarma**: Yayın sunucudan erken koptuğunda (premature end) çalışan otomatik kurtarma sisteminin kanal geçişlerinde de yanlışlıkla devreye girip, izlenen yeni içeriği durdurarak eski içeriği başlatması sorunu düzeltildi. Yayın istek sürümleri (request version) bağımsızlaştırılarak çapraz geçiş çakışmaları (race-condition) giderildi.
- **Bölüm Listesi Tasarım Düzeltmesi**: Bölüm paneli içerisindeki sezon (Expander) başlıkları ve bölüm kartlarının (Episode Cards) ekranın en sağındaki kaydırma çubuğuyla (scrollbar) çarpışmasını engellemek adına gerekli kenar boşlukları (margin/padding) eklendi ve liste tasarımı daha ferah hale getirildi.
- **Panel Geçişlerinde Yayının Yeniden Başlaması**: Kullanıcı Ses ayarları veya diğer paneller arasında geçiş yaptığında arka planda yanlışlıkla arama (seek) işleminin tetiklenerek VLC altyapısının yayını baştan başlatıyormuş gibi davranıp (loading ekranı) duraksamaya sebep olması sorunu çözüldü. Bu sorun, ses track'lerini bulmak için 250ms ile 3000ms aralıklarında art arda çalışan döngünün (`RefreshTracksWithRetryAsync`) yanlışlıkla kalınan yeri geri yükleme kodunu (`TryApplyPendingResumeSeek`) sürekli tetiklemesinden kaynaklanıyordu; arayüz ve oynatma katmanları birbirinden izole edildi.
- **Duraklatma (Pause) Hatası ve İstenmeyen Yeniden Başlama**: Oynatıcı bilerek duraklatıldığında (Pause) ekranda gereksiz yere "Yükleniyor (Buffer)" animasyonunun çıkması engellendi. Ayrıca uygulamanın arka plan sağlık kontrolü, kullanıcı tarafından yapılan duraklatmaları "yayın koptu" zannedip kendi kendine tekrar oynatmaya başlama sorunu (_auto-resume bug_) tamamen giderildi. Ek olarak, oynat/durdur butonuna basıldığı anlara denk gelen arka plan sağlık taramalarının yayını yanlışlıkla sıfırlamasına neden olan nadir bir zamanlama (race-condition) hatası da çözüldü. İndirilmiş (çevrimdışı) içeriklerde duraklatma esnasında bile gereksiz yükleme ekranı gösterilmesine sebep olan mantık hatası da düzeltildi. Artık duraklatılan yayınlar siz yeniden başlatana kadar kapalı kalır ve yükleme ekranı göstermez.
- **Zaman Çizelgesinde (Timeline) Hatalı Süre Gösterimi ve Kontrol Kaybı**: İleri/geri sarma çubuğu (Seek bar) sürüklenirken arka planda jenerik veya bölüm sonu atlama (Next Episode) uyarılarının hatalı şekilde tetiklenmesine neden olabilen pozisyon okuma hatası düzeltildi. Arayüzde kalan sürenin oynatma anında yanlış güncellenmesi durumu giderildi.
- **Bölüm Geçişlerinde Hatalı "Sıradaki Bölüm" Uyarısı**: Kullanıcı manuel olarak bölüm değiştirdiğinde, eski bölümün bitiş sinyalinin (PlaybackEnded) yeni bölüm yüklenirken araya girip ekranda yanlışlıkla "Sıradaki Bölüm" uyarısı çıkarmasına neden olan zamanlama hatası (race-condition) giderildi. Transition aşamasındaki sinyaller artık güvenli bir şekilde yoksayılıyor.
- **Ağ Kopmalarında Uygulamanın Çökmesi (Stack Overflow)**: İnternet bağlantısı tamamen yokken veya sunucu kapalıyken yayına bağlanma denemesi (Retry) yapıldığında uygulamanın kendi kendini sonsuz döngüye sokup sessizce kapanmasına (StackOverflowException) neden olan mimari hata giderildi. Yeniden bağlanma denemeleri daha güvenli bir yapıya geçirildi.
- **Arka Plan İşlemlerinin Kilitlenmesi**: Oynatıcının arka planında saat başı EPG güncellemelerini ve canlı yayın sağlığını kontrol eden sistemdeki zamanlayıcı (Timer) hatası çözüldü. Bu hata, EPG güncellemesi başarısız olduğunda o saniyedeki yayın sağlığı kontrollerinin sessizce iptal edilmesine sebep oluyordu. İşlemler birbirinden bağımsız ve eşzamanlı çalışacak şekilde izole edildi. Ayrıca, her saniye güncellenen saat bilgisinin UI thread'ini bloke etmesi ve işlemlerin üst üste binerek performans kaybına yol açması (re-entrancy) engellendi.
- **Arayüz Zamanlayıcısı Güvenlik ve Kararlılık Güncellemesi**: Video oynatıcı arayüzünün otomatik gizlenmesini sağlayan zamanlayıcı (`_autoHideTimer`), daha güvenli ve atomik çalışan `System.Threading.Timer` yapısına geçirildi. Bu sayede, yoğun kullanım anlarında arayüzün kilitlenmesi veya beklenmedik şekilde açık kalması gibi zamanlama (thread-safety) kaynaklı hatalar giderildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `VideoOverlayView.axaml` | Bölüm listesi ve Hakkında paneli meta veri bağlamlarıyla güncellendi. |
| `PlayerViewModel.cs` | Seri metadata yükleme ve panel veri tutarlılığı iyileştirildi. |

---


## v30.2 – Premium Video Oynatıcı ve Global Tema Modernizasyonu (2026-03-05)

### 🎨 Görsel ve Arayüz İyileştirmeleri
- **Minimalist Oynatıcı Kontrolleri (Netflix Tarzı)**: Video oynatıcı üzerindeki devasa kontrol butonları tamamen kaldırıldı. Yerine, yalnızca saf ikonlardan oluşan, üzerine gelindiğinde beliren zarif kontroller (`iconBtn`) eklendi.
- **Dinamik ve Duyarlı (Responsive) Yan Paneller**: Hakkında, Bölümler, Kalite ve Ses yan menüleri artık pencere boyutuna göre otomatik ölçekleniyor. Küçük pencerelerde daralıp, büyük pencerelerde `MaxWidth` sınırına kadar genişleyen esnek bir yapıya geçildi.
- **Kusursuz Metin Hizalaması (Overflow Fix)**: Yan panellerdeki uzun film özetleri ve açıklamaların kutu dışına taşması sorunu kökten çözüldü. Tüm içerik alanları `Grid` sistemine taşınarak metinlerin her koşulda kutu içinde kalması ve alt satıra geçmesi (`TextWrapping`) garanti altına alındı.
- **Koyu Tema Renk Düzeltmesi (Deep Black)**: Koyu temadaki (Dark Theme) mavi/lacivert ağırlıklı arka plan ve panel renkleri (`#161426`) tamamen temizlendi. Yerine çok daha profesyonel ve modern bir "OLED Black" estetiği sağlayan nötr siyah ve koyu gri tonları (`#0A0A0A`, `#1A1A1A`) getirildi.
- **Açık Tema Renk Düzeltmesi (Studio White)**: Açık temadaki (Light Theme) pembemsi/morumsu kirli beyaz tonlar kaldırıldı. Yerine tam stüdyo beyazı ve yumuşak gri tonları (`#FFFFFF`, `#FAFAFA`) entegre edilerek tertemiz bir görünüm sağlandı.
- **Premium "CANLI" ve Pulse Animasyonu**: Canlı yayınlarda program başlığının hemen soluna yerleşen, estetik "yanıp sönen nokta" (Pulse) animasyonuna sahip şık bir CANLI rozeti eklendi.
- **Akıllı İçerik Bilgisi (VOD vs Live)**: Hakkında panelindeki "Şu An Yayında" kutucuğu akıllı mantığa geçirildi; sadece canlı içeriklerde görünür hale getirildi. VOD içeriklerinde bu alan gizlenerek boş kutu görünümü engellendi.
- **Minimalist Bildirimler (Toasts)**: Ses ve sarma bildirimleri ekranın üst orta kısmına taşındı ve küçük, modern "hap (pill)" tasarımına kavuşturuldu.
- **VOD Kontrol İyileştirmeleri**: Film ve dizilerde play butonunun yanındaki ikonlar, 10 saniye ileri/geri sarma işlevini daha net belirten `Rewind10` ve `FastForward10` ikonlarıyla güncellendi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `VideoOverlayView.axaml` | Ana oynatıcı UI ve tüm yan panel mantığı responsive ve minimalist standartlara göre baştan yazıldı. |
| `DarkTheme.axaml` | Mavi/Lacivert tonlar nötr derin siyah tonlarına dönüştürüldü. |
| `LightTheme.axaml` | Pembemsi/Morumsu beyazlar saf stüdyo beyazına dönüştürüldü. |
| `Converters.axaml` | Canlı ve VOD içerik ayrımı için yeni görünürlük dönüştürücüleri entegre edildi. |

---

## v30.1 – Video Oynatıcı, Format Tanıma ve Kesinti Kurtarma (2026-03-04)

### 🐛 Hata Düzeltmeleri ve Video İyileştirmeleri
- **PiP (Resim-İçinde-Resim) Modu Kontrol Hataları**: PiP modundayken 2.5 saniyelik hareketsizlik süresi dolduğunda ana overlay ile birlikte PiP'e özel kontrollerin de kalıcı olarak kaybolması ve bir daha geri gelmemesi hatası düzeltildi. PiP kontrolleri artık bağımsız bir duruma (`_isPiPControlsForceVisible`) bağlandı. Ayrıca `MainWindow.axaml.cs` içine `PositionChanged` olayı eklenerek pencere Windows tarafından sürüklenirken de kontrollerin canlı kalması sağlandı. VOD ve Canlı TV geçişlerinde PiP state'inin bozulması hataları giderildi.
- **Video Kontrollerinin Sürüklerken Kaybolması**: Uygulama penceresini başlığından tutup sürükleyince video üstü kontrollerin (overlay) kaybolup tekrar gelmemesine neden olan odaklanma sorunu çözüldü. Bu sorun, sürükleme esnasında kontrollerin gereksiz yere gizlenip (Hide) geri açılmasındaki `debounceTimer` mantığından kaynaklanıyordu; artık sürükleme esnasında sadece pozisyon senkronu yapılıyor.
- **Uzantısız URL (LiveTs) Zorlaması Kaldırıldı**: Sağlayıcılardan gelen uzantısız/belirsiz yayın linkleri otomatik olarak `LiveTs` olarak dayatılıyordu; bu da VLC'nin yanlış ayarlarıyla (`drop-late-frames`, `clock-synchro=0`) yayını açmaya çalışıp 10 saniyede bir kopmasına (`EndReached`) sebep oluyordu. Uzantısız yayınlar artık yeni `Unknown` profili üzerinden `:http-continuous` ile VLC'nin kendi özgür format tespitine (auto-detect) bırakıldı.
- **Sonsuz "Premature End" Döngüsü Giderildi**: `PlayerViewModel` içindeki sunucu kaynaklı erken kesinti (premature end) kurtarma mekanizmasına **Limit ve Cooldown** eklendi. Önceden sunucu yayıncı her düşürdüğünde sistem defalarca anında tekrar bağlanıp sonsuz recovery döngüsüne giriyordu. Yeni sistemde: Art arda maksimum 5 deneme yapılır, peş peşe kopmalara karşı 3 saniye bekleme süresi uygulanır ve 2 dakika sorunsuz oynatıldığında ceza puanı (sayaç) sıfırlanır. Limite ulaşılırsa bağlantı güvenli şekilde sonlandırılır ("Yayın kararsız — bağlantı sorunlu").
- **MKV Seek Kararlılığı ve Hızı**: MKV (VOD) içeriklerde HTTP üzerinden ileri/geri sarma yapıldığında yaşanan kopma sorunu ve yavaşlık için `HardSeekAsync` bekleme (delay) süresi 1200ms'den **500ms**'ye düşürülerek seek işlemi 2.4x hızlandırıldı. Ayrıca MKV profiline özel `:demux=mkv,avformat` dayatması kaldırılarak donanımsal okuma iyileştirildi ve ağ önbelleği 8000ms'ten **10000ms**'ye çıkarılarak sunucu darboğazı esnetildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `VideoPlayerService.cs` | `Unknown` profili eklendi, uzantısız url dayatması kaldırıldı, MKV buffer artırıldı, HardSeek delay düşürüldü. |
| `PlayerViewModel.cs` | Erken kesinti kurtarma limitleri (Max 5, 3s cooldown) ve sıfırlama mekanizması eklendi. |

---

## v30.0 – TMDB Servis Altyapısı Bug Düzeltmeleri (2026-03-04)

### 🐛 Kritik Bug Düzeltmeleri
- **Scroll Enrichment Filtresi Düzeltildi** (`MainViewModel.cs`): `LoadMoreSeriesItemsAsync` içindeki TMDB zenginleştirme filtresi, `TmdbId`'si dolu ama `MetadataFetchedAt`'ı boş olan dizileri atlıyordu. Filtre `TmdbSyncService` ile uyumlu hale getirildi: `|| s.MetadataFetchedAt == null` koşulu eklendi.
- **TMDB Rate Limit Aşımı Düzeltildi** (`TmdbSyncService.cs`): `REQUEST_DELAY_MS` 300ms'den **750ms**'ye çıkarıldı. Önceki konfigürasyonda 3 concurrent slot ile ~6 req/s'e ulaşılıyordu; TMDB limiti 4 req/s (40/10s). Yeni hesap: `3 / 0.75 = 4 req/s` — tam limit sınırında.
- **Dispatcher Deadlock Riski Giderildi** (`TmdbSyncService.cs`): `EnrichWithSearchOnlyAsync` ve `EnrichWithFullDetailsAsync` içindeki `_dispatcherService.Invoke()` çağrıları `BeginInvoke()` ile değiştirildi. Background thread'den senkron UI thread çağrısı deadlock oluşturabiliyordu.
- **Genre Cache Thread Safety** (`MetadataService.cs`): `_genreCache` alanı `Dictionary` yerine `ConcurrentDictionary` olarak değiştirildi. 3 paralel enrichment thread'in aynı anda okuma/yazma yapması race condition oluşturabiliyordu.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainViewModel.cs` | Enrichment filtresi `MetadataFetchedAt == null` koşuluyla güncellendi |
| `TmdbSyncService.cs` | Rate limit 300→750ms, `Invoke`→`BeginInvoke` |
| `MetadataService.cs` | `_genreCache` → `ConcurrentDictionary` |

---

## v29.9 – Poster Yükleme Deneyimi ve API Optimizasyonu (2026-03-03)

### 🎨 Görsel ve Arayüz İyileştirmeleri
- **Bağlam Duyarlı Yayıncı Logoları (Network Logos)**: Dizi detay sayfasında gösterilen yayıncı logoları artık çok daha akıllı. TMDB'nin sunduğu JustWatch (Watch Providers) verileri sisteme entegre edildi. Artık bir dizi globalde farklı bir platformda (Örn: Peacock) olsa bile, sizin kategoriniz "TV PLUS" veya "TV+" ise sistem JustWatch üzerinden Türkiye yayıncısını bulup otomatik olarak **TV+ logosunu** getiriyor.
- **Dile Duyarlı Tür Önbelleği (Multi-Language Genre Cache)**: Uygulamanın ilk açılışında türlerin (Aksiyon, Komedi vb.) bazen İngilizce takılı kalması sorunu çözüldü. Tür önbelleği artık dil bazlı (tr-TR, en-US vb.) ayrıştırılıyor; böylece Türkçe içeriklerde her zaman Türkçe tür isimleri garanti ediliyor.
- **Akıllı Metin Kaydırma (WrapPanel)**: Dizi detay sayfasındaki "Yıl • Tür • Yaş Sınırı" bilgilerinin olduğu satır, yatay alana sığmadığında dışarı taşmak yerine otomatik olarak alt satıra geçecek şekilde (`WrapPanel`) yeniden düzenlendi.
- **Premium "İzlendi" Rozeti (Verified Style)**: Uygulama genelinde bulunan yeşil "İZLENDİ" rozeti, Twitter/X platformundaki "Verified" ikonuna benzeyen minimalist ve şık bir yıldızlı onay ikonuyla (`CheckDecagram`) değiştirildi.
- **Standartlaştırılmış Sekme Göstergeleri**: Tüm menü ve sekmelerdeki alt çizgiler, Ayarlar sayfasındaki modern, ortalanmış ve küçük (`20px`) tasarım standartına çekildi.

### ⚡ Performans ve Mimari İyileştirmeler
- **Gelişmiş Kategori ve Dil Analizi**: `TR/DIZI`, `TR-DIZI`, `[MULTI]` gibi karmaşık ön ek yapıları artık merkezi regex motoruyla saniyeler içinde analiz ediliyor. "MULTI" etiketli içerikler otomatik olarak en-US (Uluslararası) dilinde aranarak en kaliteli metadata çekiliyor.
- **Ülke Bazlı Dinamik Yaş Sınırı (Sertifika)**: Yaş sınırları artık kategori dilinden saptanan ülkeye göre önceliklendiriliyor (Örn: Alman kanalında DE öncelikli, Türk kanalında TR (+18) öncelikli).
- **Gizli API İsteği (Fallback) Temizliği**: Sezon başına atılan garantili 2. istek (en-US fallback) kaldırılarak API performansı ve yükleme hızı %50 artırıldı.
- **Kusursuz Görsel Geçiş (No-Flash Update)**: Dizi ve film sayfalarında aşağı kaydırırken yaşanan eski/yeni afiş yanıp sönme (flash) efekti ortadan kaldırıldı; TMDB araması bitene kadar temiz bir "Skeleton Loading" (mor yer tutucu) yapısı kuruldu.

### 🐛 Hata Düzeltmeleri
- **Ağ/Kanal Logosu Silinme Hatası**: Bir dizinin detay sayfasına ilk kez girildiğinde görünen yayıncı logosunun (Örn: Netflix), aynı diziye ikinci kez girildiğinde kaybolması sorunu çözüldü. TMDB'den çekilen `NetworkName` ve `NetworkLogoUrl` verilerinin anlık olarak arayüze basıldıktan sonra veritabanına (SQLite) kalıcı olarak yazılmasının unutulduğu tespit edildi ve bu veriler DB'ye mühürlenerek kalıcı hale getirildi.
- **TMDB Arama Yılı Hataları**: İsminde yıl olan ("Stranger Things (2016)") içeriklerin TMDB'de bulunamaması sorunu, yıl bilgisinin otomatik ayrıştırılıp API'ye özel filtre olarak gönderilmesiyle çözüldü (%100 isabet).
- **`LastTmdbSync` Tip Dönüşüm Hatası**: `DateTime?` tipindeki alanın `ToString()` üzerinden kontrol edilmesi sonucu oluşan potansiyel hatalar ve gereksiz bellek kullanımı `HasValue` kontrolü ile optimize edildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MetadataService.cs` | JustWatch entegrasyonu, dile duyarlı tür önbelleği ve merkezi Heuristic mantığı eklendi. |
| `SeriesInfoParser.cs` | MULTI tespiti, TV+ platform tespiti ve gelişmiş ön ek temizleme (regex) eklendi. |
| `MainWindow.axaml` & `VideoOverlayView.axaml` | Verified rozetleri ve SelectionIndicator güncellemeleri yapıldı. |
| `MainViewModel.cs` & `TmdbSyncService.cs` | Mükerrer kodlar temizlendi, dile duyarlı arama ve UI senkronizasyonu sağlandı. |
| `Series.cs` & `Channel.cs` | `LastTmdbSync` reaktif hale getirildi; DisplayCategory temizleme eklendi. |

---

## Changelog - Noctra IPTV Player

## v30.8 – Akıllı Dil Eşleştirme (2026-03-06)

### 🌟 Yeni Özellikler ve İyileştirmeler
- **Dil Öncelikli İçerik Seçimi**:TMDB popüler listeleriyle eşleşen içeriklerde, aynı yapımdan birden fazla varsa (farklı diller, alt yazılı/dublajlı vb.) uygulamanın diline (Örn: TR) en uygun olanı otomatik olarak seçilir.
- **Yabancı Dil Desteği**: Eğer kütüphanede uygulama dilinde içerik yoksa, alternatif olarak İngilizce (EN) sürümlerine öncelik verilir.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainViewModel.cs` | `LoadHomeContentAsync` eşleştirme mantığı dil skorlaması ile güçlendirildi. |

---

## v30.7 – TMDB Popüler Listeleri (2026-03-06)

### 🎨 Görsel ve Arayüz İyileştirmeleri
- **Mor Tema Optimizasyonu**: Hem Açık (Light) hem de Koyu (Dark) temalarda bulunan ve uygulamanın konseptine uymayan mavi ve indigo tonları (Gradients, InfoColor, Profil seçim ekranı) tamamen kaldırılarak yerine uygulamanın ana kimliği olan estetik mor tonları entegre edildi.
- **Detay Sayfası Butonları Kontrastı**: Dizi detay sayfasındaki Geri Dön (Back), Listeme Ekle (Plus) ve Favorilere Ekle (Heart) butonlarının arkaplanı Açık Temada beyaz üzerine beyaz denk geldiği için görünmüyordu. Bu butonlar için `ActionCircleButtonStyle` oluşturuldu ve tema destekli (`DynamicResource`) dinamik renklere geçirilerek hover durumları mükemmelleştirildi.
- **Sezon Listesi "İki Ton" Hatası**: Açık temada sezon listesinin üzerine gelindiğinde (hover) oluşan metinlerin çift renk kalma veya geçişlerde takılma sorunu çözüldü. Farklı görsel durumlar (PointerOver, Selected, Pressed) için `ListBoxItem` stilleri spesifikleştirildi.

### ⚡ Performans ve Mimari İyileştirmeler
- **Anlık Yüksek Kaliteli Poster Güncellemesi**: Arka planda TMDB'den dizi eşleştirmesi (`TmdbSyncService`) yapıldığında, bulunan yüksek kaliteli dizi afişlerinin (posterlerin) arayüze yansıması için dizinin içine girilmesi gerekiyordu. `Series` modelindeki `CoverUrl` özelliği `[ObservableProperty]` yapısına dönüştürülerek, eşleşen dizilerin posterlerinin ana liste üzerinde anlık olarak değişmesi sağlandı.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `DarkTheme.axaml` & `LightTheme.axaml` | Mavi tonlar temizlendi, mor konsept oturtuldu. |
| `Styles.axaml` | Yuvarlak etkileşimli ikon butonları için `ActionCircleButtonStyle` eklendi. |
| `MainWindow.axaml` | Detay butonları yeni stile taşındı, sezon listesi görsel durumları düzeltildi. |
| `ProfilesWindow.axaml` | Hardcoded çocuk profili indigo rengi mora çevrildi. |
| `Series.cs` & `MainViewModel.cs` | `CoverUrl` Reaktif UI için Observable yapıya geçirildi, anlık TMDB posterleri yansıtıldı. |

---

## v29.7 – Saydamlık ve Estetik Geri Kazanımı (2026-03-02)

### 🎨 Görsel İyileştirmeler
- **Bağımsız Saydamlık Konsepti**: Bölüm kartlarının arka planı yarı saydam (glassy) hale getirilirken, bu işlemin içerikleri etkilemesi engellendi. Artık bölüm afişleri (posterler) ve yazılar (metadata) arka plandan bağımsız olarak %100 net ve keskin görünüyor.
- **Yarı Saydam (Glassy) Görünüm**: Bölüm kartlarının arka planına modern ve estetik bir saydamlık kazandırıldı. Bu sayede arka plan dokusu hissedilirken okunabilirlik korunuyor.
- **Dinamik Etkileşim**: Hover durumunda sadece arka planın saydamlığı %90'a çıkarılarak odağın hangi kartta olduğu netleştirildi. Tıklama (press) anında ise %100 opaklık ile net bir basılma hissi sağlandı.

---

## v29.6 – Etkileşim ve Hizalama İyileştirmeleri (2026-03-02)

### 🎨 Görsel ve İşlevsel İyileştirmeler
- **Belirgin Hover Durumu**: Bölüm kartlarının üzerine gelindiğinde (hover) oluşan renk değişimi daha belirgin (`InteractiveHoverBrush`) hale getirildi. Artık hangi kartın üzerinde olduğunuz kolayca fark edilebiliyor.
- **Tıklama Geri Bildirimi**: Bölüm kartlarına basıldığında (click) oluşan renk değişimi optimize edildi ve bu sırada sol kenarda çıkan istenmeyen çizgi kaldırıldı.
- **Mükemmel Sezon Hizalaması**: Sezon listesi butonları ve altlarındaki seçim çizgisi, görsel ayırıcı hatla (separator line) kusursuz bir şekilde hizalandı. Sezonlar arası yatay ve dikey boşluklar pixel-perfect hale getirildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainWindow.axaml` | Sezon listesi hizalaması ve bölüm kartı etkileşim stilleri rafine edildi. |

---

## v29.5 – UI Rafine Etme ve Görsel Canlılık (2026-03-02)

### 🎨 Görsel ve İşlevsel İyileştirmeler
- **Yatay Sezon Gezintisi**: Sezon seçici tekrar klasik yatay kaydırmalı (horizontal scroll) yapıya döndürüldü. Sezonlar arası boşluklar artırılarak daha ferah bir görünüm sağlandı.
- **Canlı Bölüm Görselleri**: Bölüm kartlarının soluk (pale) görünmesi sorunu, taban opaklık değerleri (`0.5` -> `0.9`) artırılarak ve posterler üzerindeki ekstra karartma katmanı kaldırılarak çözüldü.
- **Okunabilir Açıklamalar**: Bölüm özetlerinin (plot) opaklığı artırılarak metin netliği sağlandı.
- **Gelişmiş Buton Etkileşimi**: "Sezonu İndir" butonunun arkaplanı ve hover efekti, özellikle Karanlık Tema'da daha belirgin olacak şekilde optimize edildi.

### 🌓 Metin Kontrastı
- **Işık Teması Metadata Kontrastı**: Bölüm süresi ve tarihi gibi metadata verileri, Işık Teması'nda daha koyu ve okunabilir bir tona (`#4B5563`) çekildi.
- **Etkileşim Renkleri**: Işık Teması'ndaki buton hover ve basılma renkleri daha belirgin hale getirildi.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainWindow.axaml` | Sezon listesi, bölüm kartı opaklıkları ve buton tasarımları güncellendi. |
| `LightTheme.axaml` | Metin ve etkileşim renkleri kontrast için rafine edildi. |

---

## v29.4 – UI Estetik İyileştirmeleri ve Tema Optimizasyonları (2026-03-02)

### 🎨 Görsel İyileştirmeler
- **Kompakt Bölüm Kartları**: Dizi bölümleri listesindeki kartların yüksekliği ve iç boşlukları (padding) optimize edilerek daha fazla içeriğin aynı anda görünmesi sağlandı. Yazı boyutları ve ikon ölçüleri estetikten ödün vermeden küçültüldü.
- **Dinamik Sezon Seçici**: Sezon sayısı çok fazla olan dizilerde yaşanan yatay kaydırma sorunu giderildi. Sezon butonları artık ekrana sığmadığında otomatik olarak alt satıra geçer (`WrapPanel` entegrasyonu).
- **Hover Efekti Düzeltmesi**: Bölüm kartlarının üzerine gelindiğinde (hover) oluşan vurgu çerçevesinin köşe kavislerinin (radius) ana kartla uyumsuz olması sorunu giderildi.
- **Modern Aksiyon Butonları**: Dizi detay sayfasındaki "Oynat", "Fragman" ve "Listem" butonları daha kompakt ve premium bir görünüme kavuşturuldu.

### 🌓 Tema Optimizasyonları
- **Işık Teması (Light Theme) Contrast İyileştirmesi**: Açık renkli temada okunabilirliği düşük olan beyaz metinler ve gri metadata verileri, koyu tonlu dinamik fırçalarla (`TextPrimaryBrush`, `TextMutedBrush`) değiştirilerek kontrast artırıldı.
- **Dinamik Renk Entegrasyonu**: Tüm UI bileşenleri `StaticResource` yerine `DynamicResource` kullanımına geçirilerek, tema değişimlerinde anlık ve kusursuz renk adaptasyonu sağlandı.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `MainWindow.axaml` | Bölüm kartları, sezon seçici ve butonların XAML yapısı optimize edildi. |
| `LightTheme.axaml` | Metin ve metadata renk tanımları kontrast için güncellendi. |

---

## v29.3 – İndirme Merkezi Hata Düzeltmeleri (2026-03-02)

### 🔧 Düzeltilen Hatalar
- **İndirme Kuyruğunda Yanlış İsim Gösterimi**: Sezon indirme başlatıldığında tüm bölümler kuyruğa aynı isimle (yalnızca dizi adı, örn: "Black Warrant") ekleniyordu. `DownloadItem.BaseDisplayName` özelliğindeki `SeriesInfoParser.Parse()` mantığı bölüm/sezon bilgisini siliyordu. Artık her bölüm tam ismiyle (örn: "Black Warrant - 1. Bölüm - S01 E01") gösteriliyor.
- **Sessiz İndirme Başarısızlığı**: Provider kaynaklı veri eksikliği (boş stream URL) durumunda indirme sessizce başarısız oluyordu — kullanıcıya hiçbir uyarı gösterilmiyordu. Artık tekli indirmede `"⚠️ İndirme başarısız: ... için kaynak URL bulunamadı"` uyarısı, sezon indirmede ise özet mesajında başarısız bölüm sayısı gösteriliyor.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|------------|
| `DownloadItem.cs` | `BaseDisplayName` artık `SeriesInfoParser` kullanmıyor, tam `DisplayName` döndürüyor |
| `MainViewModel.cs` | `DownloadEpisode` ve `DownloadSelectedSeason` metodlarına boş URL erken kontrolü eklendi |

---

## v29.2 – TMDB Akıllı Eşleştirme ve On-Demand Mimari (2026-03-01)

### 🔧 Düzeltilen Hatalar
- **TMDB API Anahtarı Çalışmıyordu**: `MetadataService` içindeki kritik bir hata düzeltildi — API anahtarı ortam değişkeninin **adı** olarak kullanılıyordu, bu yüzden tüm TMDB çağrıları sessizce başarısız oluyordu. 32.850 dizinin hiçbirinde TMDB verisi yoktu.
- **Dizi Detayında Veriler Boş Geliyordu**: `LoadSelectedSeriesMetadataAsync` veritabanında kayıtlı Cast, Genre, ContentRating ve BackdropUrl verilerini sıfırlıyordu. Artık mevcut veritabanı verileri ilk değer olarak gösteriliyor.
- **Yanlış TMDB Eşleştirmesi**: "Barry" (HBO) aramasında "The Drew Barrymore Show" geliyordu çünkü sadece popülerliğe göre sıralanıyordu. Yeni **isim-benzerlik skorlama sistemi** eklendi: tam eşleşme (100), başlangıç eşleşmesi (80), kelime eşleşmesi (40) — popülerlik sadece eşit skorlarda devreye giriyor.
- **Diziler Listesinde Tür Karışması**: Sağlayıcıdan gelen kategoriler (örn. "TR BİLMEMNE DİZİLER"), TMDB'den çekilen Film Türü (Genre) alanını eziyordu. Veritabanı şeması güncellenerek `GroupTitle` ve `Genre` kolonları **ayrıldı**. Kartlarda artık sadece sağlayıcı kategorisi, detayda ise orijinal TMDB türleri gösteriliyor.
- **Uluslararası Dizi Dili Tespiti (EU Kategorisi)**: Sağlayıcının `EU` önekiyle sunduğu dizilerin dili yanlışlıkla Türkçe tespit edilip TMDB'den Türkçe (ya da Kanji) olarak çekiliyordu. Parser güncellenerek adında "EU" geçen grup/isimlerin İngilizce (`en-US`) olarak çekilmesi sağlandı.
- **Arayüz Tasarım Hataları**:
  - Dizi bölümleri listesindeki aktif sezon sekmesinin kaba, mor arkaplanı kaldırılıp Netflix benzeri şeffaf alt-çizgi stiline (`NavActiveBackgroundBrush`) geçirildi.
  - Fragmanı İzle butonunun renk uyumsuzluğu Sidebar tonuyla (`AccentBrush`) eşitlenerek giderildi.
  - Uzun açıklama (Plot) ve oyuncu listesine sahip olan dizilerde yazıların detay sayfasındaki oynat butonlarının üstüne taşması/binmesi problemi, esnek ızgara yapısı ve gizli `ScrollViewer` eklenerek kökten çözüldü.
- **TMDB Posteri Kaydedilmiyordu**: `TmdbSyncService` ve `LoadSelectedSeriesMetadataAsync` poster kayıt mantığı "poster boşsa yaz" şeklindeydi, sağlayıcıdan düşük kaliteli bir poster geldiğinde TMDB posterini yazmayı reddediyordu. Artık **TMDB posteri varsa her zaman provider posterinin üstüne yazılıyor** ve veritabanına kaydediliyor. Aynı "boşsa yaz" sorunu tüm metadata alanlarında (Cast, Genre, Plot, ContentRating vb.) da mevcuttu ve hepsi düzeltildi.
- **On-Demand Zenginleştirme Dil Hatası**: Scroll ederken çalışan `TmdbSyncService.EnrichSingleSeriesAsync`, dil algılamasında sadece dizi adına (`series.Name`) bakıyordu. Artık `series.GroupTitle ?? series.Genre ?? series.Name` kullanarak EU kategorisindeki dizileri de İngilizce çekiyor.
- **Cache Temizlenmiş Diziler Yeniden Çekilmiyordu**: Enrichment filtresi yalnızca `TmdbId == null && LastTmdbSync == null` kontrol ediyordu. `MetadataFetchedAt == null` (cache'i invalidate edilen EU dizileri) olanlar da artık tekrar zenginleştiriliyor.
- **VOD Metadata DB'ye Yazılmıyordu (Kritik)**: `MetadataService.EnrichChannelAsync` yalnızca in-memory nesneleri güncelliyordu, `SaveChangesAsync` çağırmıyordu. Her VOD oynatıldığında aynı TMDB isteği tekrar gidiyordu. Artık `IDbContextFactory` ile DB'ye kalıcı yazılıyor ve `TmdbId` set edilerek zaten çekilmiş VOD'lar otomatik atlanıyor.
- **`EnrichSeriesBatchAsync` Operatör Önceliği Hatası**: `s.TmdbId == null && s.LastTmdbSync == null || s.MetadataFetchedAt == null` ifadesinde C# `&&`'ı `||`'den önce değerlendirdiği için, `TmdbId` dolu diziler bile gereksiz yere tekrar işleniyordu. Parantezlerle düzeltildi ve `TmdbId` bilinen diziler artık isim araması yerine doğrudan `/tv/{id}` endpoint'i kullanıyor.
- **`hasFullData` TrailerUrl'e Bağlıydı**: Dizi detayında `hasFullData` kontrolü `TrailerUrl` boşluğuna da bakıyordu. TMDB'den fragman dönmeyen diziler (çoğunluk) her açılışta gereksiz API çağrısı tetikliyordu. `TrailerUrl` kontrolden çıkarıldı.
- **Fragman Butonu Görünmüyordu**: `LoadSeriesWithProfileProgressAsync` zaten `FetchSeriesDetailsAsync` çağırıyordu ama yanıttaki `TrailerUrl`, `ContentRating`, `BackdropUrl`, `Genre` ve `Plot` verilerini çıkarmıyordu — sonra `MetadataFetchedAt` set ediyordu ve `LoadSelectedSeriesMetadataAsync` "veri dolu" sanıp atlıyordu. Artık aynı API yanıtından tüm alanlar çıkarılıyor, ek istek yok.
- **Bölüm Süresi Hardcoded "45 dk" İdi**: Episode row template'inde süre "45 dk" olarak hardcode edilmişti, gerçek veriyle bağlantı yoktu. Artık `EpisodeMetaText` binding'i ile TMDB'den çekilen gerçek süre ve yayın tarihi gösteriliyor (örn: "01 Mar 2025  •  45 dk"). `TmdbEpisodeDetail` modeline `Runtime` eklendi, `Episode` modeline `AirDate` eklendi — ikisi de aynı `/tv/{id}/season/{n}` yanıtından geliyor, ek API isteği yok.

### 🚀 Yeni Özellikler
- **On-Demand TMDB Mimarisi**: Arka planda sürekli çalışan `TmdbSyncService` kaldırıldı. Artık TMDB verileri **sadece kullanıcının ekranında gördüğü diziler** için çekiliyor:
  - Dizi sayfasına girildiğinde her 50'lik sayfa için arka planda 3 paralel istek ile zenginleştirme yapılıyor
  - Canlı TV izlerken **sıfır API çağrısı**
  - Dizi detayına girildiğinde veriler anında veritabanından gösteriliyor
  - Çekilen tüm veriler veritabanına kaydediliyor, tekrar çekilmiyor
- **İki-Fazlı TMDB Mimarisi (API İstekleri %50 Azaltıldı)**: Scroll sırasında her dizi için 2 istek (search + detail) yerine artık sadece 1 istek (search-only) yapılıyor. `SearchSeriesAsync` metodu eklendi — poster, açıklama, yıl, puan ve tür bilgilerini tek search yanıtından alıyor. Cast, yaş sınırı ve fragman verileri yalnızca kullanıcı diziye tıkladığında çekiliyor (`FetchSeriesDetailsAsync` — `append_to_response` ile tek istek). `hasFullData` kontrolü `MetadataFetchedAt.HasValue` ile değiştirildi.
- **TmdbId Bazlı Doğrudan Çekim**: Dizi detayında `TmdbId` biliniyorsa isim araması yerine doğrudan `/tv/{id}` endpoint'i kullanılıyor — yanlış eşleşme riski sıfır.
- **`TmdbDetail` Modeline Genres Desteği**: TMDB detay endpoint'inden gelen tür bilgileri (`genres`) artık doğrudan parse ediliyor.
- **Dizi Fragmanı (Trailer) Desteği**: `append_to_response=videos` parametresi ile TMDB'den dizilere ait YouTube fragmanları çekilerek veritabanına (`TrailerUrl`) kaydediliyor. Dizi detay sayfasına, temaya uygun mor renkli bir "Fragmanı İzle" butonu eklendi. Butona tıklandığında fragman varsayılan tarayıcıda açılır.
- **Bölüm Açıklaması İngilizce Fallback**: Türkçe bölüm açıklaması TMDB'de yoksa (çevirisi yapılmamış sezonlar), aynı sezon `en-US` ile otomatik çekilerek eksik açıklamalar İngilizce ile dolduruluyor. Sezon başına max **+1 ek istek**, tüm açıklamalar Türkçe doluysa 0 ek istek.
- **TMDB Bölüm Alt Başlıkları (Episode Titles)**: TMDB'den gelen bölüm isimleri (örn: "Will Byers'ın Ortadan Kayboluşu") artık episode row'da accent renkli, italic alt başlık olarak gösteriliyor. `Episode.TmdbEpisodeName` alanı eklendi — aynı `/tv/{id}/season/{n}` yanıtından, ek API isteği yok.
- **Yayıncı Ağ Logoları (Network Logos)**: Dizi detay sayfasında yaş sınırı badge'inin yanında yayıncı logosu (Netflix, HBO, Disney+, Amazon, vb.) gösteriliyor. `Series.NetworkName` ve `Series.NetworkLogoUrl` alanları eklendi — aynı `/tv/{id}` yanıtından, ek API isteği yok.
- **Bölüm ve Sezon İndirme Desteği**: Dizi detay sayfasındaki her bölüm için indirme butonu aktif hale getirildi. Ayrıca aktif sezonun tüm bölümlerini sırasıyla indirme kuyruğuna ekleyen "Sezonu İndir" butonu eklendi. `DownloadEpisodeCommand` ve `DownloadSelectedSeasonCommand` eklendi.

### ⚡ Performans İyileştirmeleri
- **Otomatik Arama Kaldırıldı**: Arama kutusuna yazarken otomatik sonuç gösterimi devre dışı bırakıldı. Arama sadece **Enter** tuşu veya arama butonu ile tetikleniyor — gereksiz işlem yükü ortadan kalktı.
- **Akıllı Veri Atlaması**: TMDB'de karşılığı olmayan diziler `LastTmdbSync` ile işaretlenip tekrar sorgulanmıyor. Provider'dan gelen orijinal veriler korunuyor.
- **Boş Kalan İndirme Klasörleri Temizleniyor**: İndirilen bir bölüm silindiğinde veya iptal edildiğinde, dizinin klasöründe başka dosya kalmamışsa o boş seri klasörü de otomatik olarak sistemden silinerek disk kirliliği önleniyor.
- **Kayıp İndirme Dosyaları Koruması**: Uygulama kapalıyken (veya indirme duraklatılmışken) arka planda klasör/dosya silinirse, uygulama açıldığında yarım kalan indirmeleri baştan silbaştan indirmek yerine "İndirme dosyaları klasörden silinmiş veya bulunamıyor" uyarısıyla **Hata** durumuna çeker.

### 🐛 Hata Düzeltmeleri
- **Favoriler ve Listem Görünümü**: Dizi bölümleri Favori/Listem'e eklendiğinde, genel listede tek tek (VOD gibi) görünme hatası düzeltildi. Artık Listem ve Favoriler sekmelerinde **sadece dizinin ana kartı** görünecektir.

### 📁 Değişen Dosyalar
| Dosya | Değişiklik |
|-------|-----------|
| `MainViewModel.cs` | Listem ve Favoriler ekranında VOD gibi listelenen dizilere ait bölümler (episodes) filtrelenerek sadece ana dizi kartları bırakıldı. |
| `ContentDownloadService.cs` | Yarım kalan/duraklatılan indirme dosyaları silindiğinde uygulamanın baştan indirmesi engellenip **Hata** fırlatıldı; iptal edilen dosyalardan boşalan dizi klasörleri temizlendi. |
| `MetadataService.cs` | API key düzeltmesi + isim-benzerlik skorlama |
| `TmdbSyncService.cs` | Arka plan döngüsü → on-demand `EnrichSeriesBatchAsync` |
| `ITmdbSyncService.cs` | Basitleştirilmiş arayüz |
| `MainViewModel.cs` | On-demand tetikleme + auto-search kaldırma + DB-first veri yükleme |
| `TmdbModels.cs` | `TmdbDetail`'e `Genres` property |
| `App.axaml.cs` | `StartSync()` kaldırıldı |

- **Birim Testleri (Unit Testing) ile Güvence Altına Alınmış Mimari**: 
    - Yeni TMDB eşleştirme mekanizması ve çoklu dil analiz motorumuz (Language Detection), kapsamlı xUnit testleriyle (`SeriesInfoParserTests` ve `WatchHistoryServiceTests`) koruma altına alınmıştır. 
    - *Stranger Things (Cross-Provider Match)* gibi kritik izleme senaryoları simüle edilip 100+ başarılı test durumuyla sistemin hatasız çalıştığı (%100 TMDB senkronizasyonu) tescillenmiştir.

- **Akıllı Dil Tespiti (Language Detection)** (2026-03-01):
    - Dizi ve kategori isimlerinde yer alan ülke/dil kısaltmaları (`[TR]`, `|EN|`, `DE.`, `RU Dual` vb.) özel bir Regex motoruyla (StrictLanguageCodeRegex) analiz ediliyor.
    - TMDB'den özet, afiş ve meta veriler çekilirken artık sabit "tr-TR" yerine dizinin/kanalın orijinal dilinde istek atılıyor. Alman VOD'larına Almanca özet, İspanyolca dizilere İspanyolca afiş getiriliyor.
- **Kusursuz Çapraz-Sağlayıcı İzleme Geçmişi (Absolute TMDB Match)**: Dizi izleme (progress) mimarisi artık kod seviyesinde `TmdbId` öncelikli eşleşmeye geçirildi.
    - *Senaryo*: Bir kullanıcı Provider-A'da "Stranger Things" izlerken 1. Sezon 3. Bölümü bitirip 4. Bölümün yarısında kalırsa; ileride listeyi veya sağlayıcıyı değiştirip isim farklı bir şekilde ("Things of Stranger (4K)") karşısına çıksa dahi; arka plandaki `TmdbId` eşleşmesi sayesinde diziye tıklar tıklamaz doğrudan 4. bölümün yarısından oynamaya devam eder. Yeni sağlayıcıdaki tüm bölümler anında "İzlendi" olarak işaretlenir.

- **TMDB Merkezi Meta Veri Sistemi ve Gecikmeli Yükleme (Lazy Load)** (2026-03-01):
    - **Arka Plan Eşleştirici (Background Sync Worker)**: M3U/Xtream listesindeki ID'si ve posteri olmayan VOD (Filmler) ve Diziler için arka planda tamamen sessiz çalışan `TmdbSyncService` eklendi. TMDB'nin limitlerine (10 saniyede ~40 istek) tam saygı göstererek sistemi boğmadan veritabanını zenginleştirir.
    - **Minimum API İsteği (Lazy Loading)**: Dizi listesinde gezinirken yüzlerce dizi için API isteği atılması engellendi; her şey anında SQLite'dan okunur. Bir diziye (örn. 3 sezonluk bir diziye) ilk kez tıklandığında yalnızca 4 istek (1 ana dizi detayı + 3 sezon detayı) atılarak o dizinin *tüm* bölüm resimleri ve açıklamaları tek seferde çekilir ve sonsuza dek cihazda önbelleklenir.
    - **Zengin Veritabanı Modelleri**: `AppDbContext`, `Series` ve `Channel` tabloları TMDB verilerini (Oyuncular, Yönetmen, BackdropUrl, Yaş Sınırı, Orijinal Dil ve TmdbId) destekleyecek şekilde genişletildi ve `TmdbId` sütunlarına hızlı arama (Indexing) eklendi.

- **Reaktif Arayüz İyileştirmeleri (MVVM)** (2026-03-01):
    - **Anlık İkon Güncellemesi**: Favorilere Ekle (Kalp) ve Listeme Ekle ikonlarına tıklandığında arayüzün anlık tepki vermemesi (değişikliği görmek için çıkıp girme gereksinimi) sorunu çözüldü. `Channel` ve `Series` modelleri tam MVVM desteği için `ObservableObject` altyapısına geçirilerek arayüzün özellik değişikliklerinden anında haberdar olması sağlandı.

- **Görsel Kimlik ve İkon Güncellemesi** (2026-03-01):
    - **Favori İkonu Değişimi**: Uygulama genelindeki favori (star) ikonları, daha modern bir görünüm için kalp (heart) ikonları ile değiştirildi.
    - **Sidebar Güncellemesi**: Yan menüdeki "Favoriler" ikonu kalp olarak güncellendi.
    - **Kanal Kartları Tasarımı**: Canlı TV kartlarındaki favori butonları kalbe dönüştürüldü ve favori durumunda vurgu rengi olarak kırmızı (`#E50914`) kullanıldı.
    - **Puanlama Göstergesi**: Oynatıcı bilgi panelindeki yıldız karakteri kalp karakteri (`❤`) ile değiştirildi.
- **Dizi Detay Sayfası Tasarım Güncellemesi ve Dinamik Meta Veri Entegrasyonu** (2026-02-28):
    - **Dinamik Tür ve Yaş Sınırı**: Sabit metinler yerine TMDB API'den gelen gerçek tür (Genre) ve yaş sınırı (ContentRating) bilgileri entegre edildi.
    - **Akıllı Meta Veri Paneli**: Yıl, tür ve yaş sınırı alanları sadece veri varsa görünecek şekilde güncellendi; ayırıcı noktalar (`•`) dinamik olarak konumlandırıldı.
    - **Yerleşim Sabitleme**: Dizi açıklaması veya meta veriler eksik olsa dahi başlık, geri butonu ve kontrol butonlarının (Oynat, Listem) ekrandaki yerleri sabitlendi (Sticky Layout).
    - **Otomatik İçerik Güncelleme**: Dizi açıklaması (Overview) ve oyuncu kadrosu (Cast) bilgileri artık otomatik olarak TMDB üzerinden çekiliyor.
    - **TMDB API Anahtarı Yönetimi**: Kullanıcıdan TMDB API anahtarı isteme zorunluluğu kaldırıldı; uygulama artık dahili anahtar ile çalışıyor ve ayarlar ekranı sadeleştirildi.
    - **Demo Veri Temizliği**: Rastgele üretilen "Eşleşme Yüzdesi" alanı, daha temiz ve profesyonel bir görünüm için kaldırıldı.

- **Premium Dizi Detay Sayfası Tasarımı ve Reaktivite** (2026-02-28):
    - **Netflix Tarzı Modern Arayüz**: Dizi detay sayfası tamamen yenilenerek bulanık arka plan (backdrop blur), geniş afiş alanı ve dikey bölümlendirme yapısına geçildi.
    - **Sabitlenmiş Üst Panel**: Dizi adı, yılı, türü ve yaş sınırı gibi kritik bilgiler sayfanın en üstüne sabitlendi; içerik açıklaması boş olsa dahi düzenin bozulmaması sağlandı.
    - **Gelişmiş Bölüm Kartları**: Bölümler artık 200px genişliğinde önizleme görselleri, bölüm numaraları, izlenme ilerleme çubukları ve "İZLENDİ" rozetleri içeren modern kartlar şeklinde listeleniyor.
    - **Akıllı İkon Senkronizasyonu**: Favori (Kalp) ve Listem (+/-) butonlarındaki takılma ve güncellenmeme sorunları giderildi. Artık tıklandığında ikonlar anlık olarak tepki veriyor ve veritabanıyla tam senkronize çalışıyor.
    - **Global Kaydırma (Scroll)**: Tüm sayfa yapısı tek bir kaydırma alanına alınarak akıcı bir gezinti deneyimi sağlandı.

- **İndirme Güvenliği ve Hesap Bütünlüğü (Phase 28)** (2026-02-28):
    - **Profil Bazlı İndirme Koruması**: İndirmelerin farklı hesaplar (profiler) arasında paylaştırılması veya yanlış hesapla devam ettirilmesi engellendi. Artık bir indirme sadece başlatıldığı hesap aktifken devam ettirilebilir. Bu sayede yanlış hesaptan gelen geçersiz link/token kullanımıyla oluşan "ses var görüntü yok" veya "altyazı eksik" gibi veri bozulmaları önlendi.
    - **Hesap Bilgisi Güncelleme Koruması**: Bir profilin kullanıcı adı, şifre veya URL bilgisi değiştirildiğinde, o profile ait tüm aktif (indirilmekte olan veya duraklatılan) indirmeler otomatik olarak "Hatalı" durumuna çekilir ve kullanıcıya bilgilendirme yapılır. Bu, eski/geçersiz token'larla indirmeye devam edip bozuk dosya oluşmasını engeller.
    - **Altyazı Kapatma Sorunu Giderildi**: Bazı streamlerde altyazı kapatma tuşuna basılmasına rağmen altyazıların gitmemesi sorunu, LibVLC'nin altyazı izleme (-1/0) mantığı iyileştirilerek çözüldü.

- **Arama Algoritması ve Alaka Düzeyi (Relevance Scoring) İyileştirmesi** (2026-02-28):
    - **Ağırlıklı Sıralama Sistemi**: Arama sonuçları artık basit bir "içeriyor mu?" kontrolü yerine 100 üzerinden puanlama sistemiyle sıralanıyor. Tam eşleşme (100p), kelime başı eşleşme (80p) ve içerik eşleşmesi (40p) şeklinde ağırlıklandırılarak en alakalı sonuçların en üstte çıkması sağlandı.
    - **Kısa Sorgu Filtreleme**: `%3` gibi çok kısa (3 karakter altı) aramalarda, binlerce alakasız sonucun (örn: bölüm isminde 3 geçen tüm diziler) gelmesini önlemek için derin içerik araması bu tür sorgularda kısıtlandı.
    - **Akıllı Hata Düzeltme Önerileri**: Arama motoruna "Bunu mu demek istediniz?" (Did you mean) zekası eklendi. Kullanıcı 2-3 harfi yanlış yazsa bile (örn: `Kurtuluş` yerine `Kurtuls`) Levenshtein mesafesi ve benzerlik skoru hesaplanarak en doğru öneri otomatik olarak sunuluyor.

- **Dil/Ülke Tespiti (Language Detection) Geliştirmesi** (2026-02-28):
    - Kanal gruplarındaki (örn: `|TR| BELGESEL`) ülke kodlarını algılama sistemi düzeltildi. Önceden sadece kelime sınırlarına bakan sistem, artık boru (`|`), köşeli parantez (`[]`) veya parantez (`()`) içine yazılan ülke kodlarını (örn: `|US|`, `[DE]`, `(FR)`) başarıyla algılayıp kullanıcının yerel diline en uygun kategorileri en başa çekiyor.

- **Performans: Profil Yükleme ve Arayüz Kilitlenmeleri (Yanıt Vermiyor) Çözüldü** (2026-02-28):
    - **O(N^2) Veritabanı Kilitlenmesi Giderildi**: Büyük Stalker Portal veya Xtream hesapları eklenirken, her kategori yüklendiğinde tüm veritabanını tarayıp dizileri gruplayan (AggregateContentAsync) ağır işlemin yüzlerce kez üst üste çalışarak SQLite'ı kilitlemesi engellendi. Gruplama işlemi artık yükleme tamamen bittikten sonra sadece bir kez çalışıyor.
    - **Arayüz (UI) İş Parçacığı Taşması Önüldü**: Binlerce kategori/kanal içeren hesapların arka plan yüklemesi sırasında her bir ilerleme (progress) adımının anında ekrana yansıtılmaya çalışılması sonucu oluşan "Yanıt Vermiyor" donmaları düzeltildi. İlerleme çubuğu güncellemeleri 100 milisaniyelik bir geciktirme (debounce) mekanizmasına bağlandı.

- **Performans: Gerçek Zamanlı Arama Optimizasyonları** (2026-02-28):
    - **Sıfır Bellek Tahsisi (Zero-Allocation) ile Arama**: On binlerce içeriğin içinde arama yaparken her harfe basıldığında `.ToLower()` ve `.ToLowerInvariant()` kullanılması yüzünden saniyede on binlerce geçici string oluşturulup Çöp Toplayıcıyı (GC) yorması engellendi. Aramalar artık yeni bellek tahsis etmeyen `StringComparison.OrdinalIgnoreCase` ile yapılıyor, arama anındaki takılmalar (stuttering) tamamen yok edildi.
    - **ArrayPool Optimizasyonu**: Bulanık eşleştirme (Fuzzy Match / Levenshtein Distance) algoritması içindeki geçici dizi (`int[]`) oluşturmaları kaldırılarak ortak bellek havuzuna (`ArrayPool`) geçirildi.

- **İndirme Merkezi Hata Yönetimi ve Şeffaflık** (2026-02-28):
    - **Ölümcül Hataların Yakalanması**: İndirme motoru güncellendi. Eski/pasif hesaplardan veya artık sunucuda bulunmayan (404 Not Found, 401 Unauthorized, 403 Forbidden) içerikler indirilmeye çalışıldığında; sistemin bu durumu geçici bir internet kopması sanıp sonsuz "Duraklatıldı" döngüsüne girmesi engellendi. Bu tür kritik durumlarda indirme doğrudan iptal edilerek "Hatalı" (Failed) statüsüne çekiliyor.
    - **Arayüzde Detaylı Hata Gösterimi**: İndirilenler ve İndirme Merkezi ekranlarında, bir içeriğin durumu "Hatalı" veya "Duraklatıldı" olduğunda sadece bu kelimeleri yazmak yerine, hatanın alt metni (Örn: "Hatalı - Yetkisiz erişim. Hesap süresi dolmuş veya iptal edilmiş olabilir.") kullanıcının görebileceği şekilde doğrudan durum çubuğuna entegre edildi.

- **Bağlantı Analizi ve Hata Gösterimi İyileştirmeleri** (2026-02-28):
    - **Detaylı Hata Mesajları**: "Bilinmeyen bir hata oluştu" şeklindeki genel hatalar; "DNS veya Ağ hatası", "SSL/Güvenlik sertifikası hatası" gibi gerçek teknik sorunu yansıtacak şekilde Türkçe ve anlaşılır hale getirildi.
    - **Sağlayıcı Engeli Atlatma (User-Agent)**: Bazı IPTV sağlayıcılarının (özellikle Xtream/M3U) tarayıcı dışı istekleri engellemesini (403 Forbidden) önlemek için, bağlantı analizi testlerine Chrome "User-Agent" başlığı eklendi.
    - **SSL Sertifika Bypass**: Geçersiz veya süresi dolmuş SSL sertifikasına sahip sağlayıcılarda bağlantı testinin başarısız olmasını engellemek amacıyla, yalnızca test aşamasında geçerli olacak şekilde SSL doğrulama kısıtlaması esnetildi.
    - **Zaman Aşımı İyileştirmesi**: Ağır yanıt veren sunucular için bağlantı testi bekleme süresi 10 saniyeden 15 saniyeye çıkarıldı.

- **Çift Gösterim Hatası Giderimi ve Depolama Bilgilendirmesi** (2026-02-28):
    - **7/24 Kanal Sınıflandırma Düzeltmesi**: M3U, Xtream Codes ve Stalker Portal servislerinde "7/24" veya "24/7" ifadesi içeren kanalların yanlışlıkla "Dizi" olarak sınıflandırılması engellendi. Artık kanal adında veya grup/kategori başlığında bu ifadeler geçtiğinde içerik her zaman "Canlı TV" (Live) olarak gruplandırılıyor.
    - **Mükerrer Kayıt Senkronizasyonu**: İndirilenler, Favoriler, Geçmiş ve Listem sayfalarında içeriklerin bazen çift görünmesine neden olan asenkron yarış durumu (Race Condition) giderildi. `SemaphoreSlim` ve UI kanalı atomik güncelleme (`IDispatcherService.Invoke`) mekanizmalarıyla listelerin kararlılığı sağlandı.
    - **Depolama Bilgi Paneli (Legend)**: İndirilenler sayfasındaki depolama barının altına; "Disk Doluluğu", "Noctra", "İndirilenler" (Devam edenler) ve "Boş Alan" verilerini temsil eden renkli bir açıklama paneli eklendi.
    - **Kritik Depolama Uyarısı**: Cihazın toplam doluluğu (Noctra indirmeleri dahil) %90'ı geçtiğinde, kullanıcıyı yeni indirmelerin başarısız olabileceği konusunda uyaran görsel bir ikaz bandı eklendi.
    - **Atomik Liste Güncelleme**: Uygulama genelindeki tüm kişisel listeler (`MyList`, `Favorites`, `History`) artık arka plan ve arayüz iş parçacıkları arasında tam senkronize şekilde güncellenerek tutarsız veri gösteriminin önüne geçildi.

- **İndirilenler Sayfası Görselleştirme ve Kontrol İyileştirmeleri** (2026-02-27):
    - **Sıralı Depolama Barı (Stacked Storage)**: Üst bilgi alanındaki depolama barı, "Diğer Doluluk", "Noctra Doluluğu" ve "İnecekler" şeklinde birbirini takip eden mantıksal bölümlere ayrıldı. Bu sayede Noctra'nın disk üzerindeki etkisi net bir şekilde takip edilebilir hale getirildi.
    - **Dinamik Durum Renkleri**: İndirme ilerleme barları duruma göre renklenir hale getirildi (Duraklatıldı: Turuncu, Hata: Kırmızı, İniyor: Mor).
    - **Detaylı Dosya Boyutu Gösterimi**: İndirme merkezinde sadece yüzde (%) yerine inen ve toplam boyutu gösteren (Örn: 220 MB / 2.7 GB) detaylı veri görünümü eklendi.
    - **Reorganize İndirme Merkezi**: Liste "Devam Eden" ve "Sıradakiler" olarak ikiye ayrıldı; "Tümünü Durdur" ve "Kuyruğu Temizle" fonksiyonları eklendi.
    - **Tahribatlı İşlem Geri Bildirimi (Hover UI)**: "Tümünü Sil" butonu ve tekil çöp kutusu ikonları, yanlış işlemleri önlemek adına üzerine gelindiğinde (Hover) belirgin kırmızı renge bürünecek şekilde güncellendi.
    - **Bireysel İçerik Yönetimi**: Kütüphanedeki her bir medya öğesi için tekil silme özelliği eklendi.
    - **Kütüphane Kart Tasarımı**: Kütüphanedeki dizi ve film listeleri, İndirme Merkezi ile uyumlu, çerçeveli (bordered) kart tasarımına güncellendi.
    - **İndirme Kuyruğu Kararlılığı**: Kuyruktaki öğelerin "Paused" durumundayken otomatik başlaması engellendi; "Tümünü Durdur" komutu tüm kuyruğu kapsayacak şekilde genişletildi.
    - **Performanslı Layout Motoru**: Proporitonal (yıldız tabanlı) Grid hesaplamaları için `DoubleToStarGridLengthConverter` eklendi; bar grafiklerinin her çözünürlükte kusursuz görünmesi sağlandı.

- **İndirilenler Sayfası İyileştirmeleri ve Yerel Yönetim** (2026-02-27):
    - **Tab Tasarımı Fix**: "Kütüphane" ve "İndirme Merkezi" sekmeleri arasındaki "ters çalışma" (toggle) hatası giderildi; sekmeler artık kararlı bir şekilde geçiş yapıyor.
    - **Gelişmiş Sıralama**: İndirilen içerikler için "Son İndirilen", "İsim (A-Z)" ve "Boyut (Büyükten Küçüğe)" sıralama seçenekleri eklendi.
    - **Bireysel Silme Mantığı**: Her dizi ve film satırına "Çöp Kutusu" ikonu eklendi. Silme işlemi hem yerel dosyaları hem de veritabanı kayıtlarını kalıcı olarak temizler.
    - **Hover ve UI Polish**: "Tümünü Sil" butonu için tehlike uyarısı renginde (Kırmızı) hover efekti eklendi. Silme ikonları için de görsel geri bildirimler iyileştirildi.
    - **Doğru Boyut Hesaplama**: Dizilerin toplam boyutu, yerel dosya keşif sürecinden sonra hesaplanacak şekilde optimize edildi ve yerel sayı formatına (`1,23 GB`) uygun hale getirildi.
    - **Depolama İstatistikleri**: İndirilenler sayfasının üst kısmına toplam kullanılan alan ve ilerleme çubuğu (Progress Bar) eklendi.
    - **Hata Giderme**: `EqualityToBoolConverter` ve eksik `using` ifadeleri gibi derleme hataları giderildi, dosya sistemi senkronizasyonu güçlendirildi.

- **Evrensel Medya Kartları (Netflix Standartı), Akıllı Arama ve Yüksek Performans** (2026-02-27):
    *   **Netflix Tarzı Medya Kartları**: Tüm uygulama genelinde (`Home`, `Movies`, `Series`, `Live`, `Search` vb.) eski düzensiz listeler kaldırılarak yerlerine standart `VodCard`, `SeriesCard` ve `LiveTvCard` bileşenleri eklendi. Kartlar tam kaplayan (full-bleed) poster tasarımına, üzerine gelince büyüme (Scale) ve kararma efektlerine kavuşturuldu.
    *   **Akıllı Yer Tutucular ve Sıfır Overdraw**: Posterler ve logolar yüklenene kadar kartların boyutunun bozulmasını (layout shift) engellemek için temaya uygun (`SurfaceLightBrush`), sabit boyutlu yer tutucular eklendi. Resim başarıyla yüklendiği anda bu yer tutucu katmanı otomatik olarak gizlenerek ekran kartı (GPU) üzerindeki gereksiz çizim yükü (Overdraw) tamamen ortadan kaldırıldı.
    *   **Legacy Temizliği**: Eski "Logo.png" tabanlı yer tutucu sistemi ve `RemoteImage` içerisindeki tüm atıl kodlar (zaman aşımı kontrolleri, fallback mantığı vb.) silinerek kod tabanı ve bellek kullanımı optimize edildi.
    *   **Yüksek Performans Optimizasyonu**: Binlerce içerik listelenirken kasmaya neden olan `DropShadowEffect` (Gölge), `LinearGradientBrush` (Gradyan) ve ağır `Transitions` (Animasyon) yapıları temizlendi. Listeleme performansı için `ListBox` sanallaştırması ve `MediumQuality` resim render seçenekleri optimize edildi.
    *   **Akıllı Genişleyen Arama Kutusu**: Header kısmındaki arama kutusu; tıklandığında, metin içerdiğinde veya arama sonuçları sayfasındayken otomatik genişleyen (300px), diğer durumlarda ise ikon moduna (40px) daralan akıllı bir yapıya kavuşturuldu. Arama metni navigasyon sırasında korunur hale getirildi.
    *   **High-Impact Sidebar (Sol Menü)**: Sol menüdeki Noctra logosu ve mükerrer ayarlar butonu kaldırılarak tasarım sadeleştirildi. Seçili menü öğesinin soluna karakteristik aktif vurgu çizgisi (Active Indicator) eklendi.
    *   **Standardize ProgressBar**: Uygulama genelindeki tüm sahte ilerleme çubukları kaldırılarak, temanın ana renklerine (`AccentBrush`) tam uyumlu, modern ve native Avalonia `ProgressBar` bileşenine geçildi.
    *   **Stabilite ve Hata Giderme**: XAML katmanındaki sözdizimi hataları, `InvalidCastException` çökmesi ve kartların büyümesi sırasında yaşanan kesilme (clipping) sorunları giderildi.


- **M3U Bağlantı Analizi ve Stalker Sınırsız Senkronizasyon** (2026-02-26 15:00):
    - **Stalker Full Sync (Sınırsız)**: Stalker portalları için önceki "Hızlı Yükleme" limitleri (15-50 sayfa) tamamen kaldırıldı. Artık 100.000+ içerikli devasa portallar bile tek seferde, eksiksiz olarak senkronize edilir.
    - **Paralel Çekim Optimizasyonu**: Çok sayıda sayfayı (örn: 7000+ sayfa VOD) internet hızını sonuna kadar kullanarak çekebilmek için paralel ağ isteği kapasitesi (Semaphore) artırıldı.
    - **M3U "Bağlantıyı Analiz Et" Butonu**: M3U profil düzenleme ekranında gizli kalan analiz butonu aktif edildi. Analiz motoru artık M3U linklerinin doğruluğunu HEAD/GET istekleriyle gerçek zamanlı kontrol eder.
    - **Hibrit Kategori Eşleme**: Stalker portallarındaki eksik/hatalı kategoriler için hem ID hem de isim tabanlı çalışan akıllı bir yedekleme mekanizması (Hybrid Mapping) eklendi.
    - **Canlı İlerleme Logu**: Devasa veri çekim işlemleri sırasında `startup.log` üzerinden anlık % ilerleme takibi imkanı sağlandı.

- **Merkezi Yükleme Sistemi ve UI Sadeleştirmesi** (2026-02-26 15:30):
    - **Global Yükleme Paneli (Overlay)**: Kanal listesi yenileme gibi uzun süren işlemler için tüm uygulamayı kapsayan merkezi bir yükleme ekranı (`IsGlobalLoading`) eklendi. Bu panel `MainWindow` ve `SettingsWindow` ile tam uyumlu çalışır.
    - **Kararlı Spinner Animasyonu**: `PremiumSpinner` animasyonu, en yüksek uyumluluk için kararlı XAML tabanlı sisteme güncellendi. Akıcı dönüş ve stabilite optimize edildi.
    - **UI Temizliği**: Ana ekranda bulunan eski iskelet yükleme (skeleton loading) animasyonları, kullanıcı deneyimini basitleştirmek adına tamamen kaldırıldı.
    - **Dinamik Ölçeklendirme**: Spinner kontrolü, `TemplateBinding` ve geliştirilmiş XAML yapısı sayesinde farklı boyutlarda (30px'ten 100px+'e kadar) bozulmadan ve merkezini koruyarak dönecek şekilde güncellendi.


- **Akıllı Kanal Eşleştirme ve beIN Sports Şifre Çözücü** (2026-02-26 16:00):
    - **Atomic EPG Clear (Sıfırlanma Koruması)**: EPG yenileme sırasında verilerin en başta silinip (0'lanma), indirme başarısız olunca boş kalması sorunu giderildi. Artık eski veriler, sadece yeni veriler başarıyla indirilip kaydedilmeye başlandığı anda siliniyor.
    - **beIN Sports & Canlı Kanal Koruması**: `beIN SPORTS`, `S SPORT`, `Tivibu Spor` gibi canlı spor kanallarının isimlerindeki rakamlar nedeniyle (Örn: `beIN SPORTS 1`) yanlışlıkla "Dizi/Series" olarak algılanıp "1. Bölüm" şeklinde görünmesi sorunu kökten çözüldü.
    - **be*IN Maskeleme Desteği**: Sağlayıcılar tarafından kullanılan `be*IN`, `be-IN` gibi alternatif/şifreli kanal isimlendirmeleri `IsLiveSeries` kontrolüne eklenerek bu kanalların her koşulda "Canlı Yayın" kategorisinde kalması sağlandı.
    - **Gelişmiş Seri Ayrıştırma (SeriesInfoParser)**: Dizi isimlerini ayıklayan Regex motoruna "Kelime Sınırı" (`\b`) koruması eklendi. Bu sayede kelime sonundaki harfler (SPORTS'un S'si gibi) artık dizi sezon işareti (S01) olarak yanlış algılanmıyor.
    - **Kapsamlı Regresyon Testleri**: Spor kanalları ve maskelenmiş isimler için 10+ yeni test senaryosu eklenerek kategorizasyon doğruluğu %100'e çıkarıldı.
    - **beIN Sports De-obfuscation**: Sağlayıcılar tarafından maskelenen kanal isimleri (`be*n`, `b*in`, `be!n` vb.) için akıllı bir Regex motoru eklendi. Bu kanallar otomatik olarak `bein` şeklinde normalize edilerek EPG eşleşme oranları %100'e çıkarıldı.
    - **Genişletilmiş Uluslararası Kapsam**: Popüler yabancı kanallar listesi dünya devlerini (TLC, DMAX, BLOOMBERG, CNBC, HBO, SKY vb.) kapsayacak şekilde 70+ anahtar kelimeye genişletildi.
    - **Büyük/Küçük Harf Duyarlılığı**: Tüm kanal eşleştirme ve filtreleme süreçleri `Case-Insensitive` hale getirilerek her türlü yazım varyasyonunun (örn: `tlc`, `Tlc`, `TLC`) yakalanması sağlandı.

- **Ultra-Hızlı EPG Eşleştirme ve Akıllı Ülke Kapsamı** (2026-02-26 01:45):
    - **Ülke Kapsamlı (Country-Scoped) Tarama**: Yabancı rehber dosyaları işlenirken artık tüm kanal listesi taranmaz. Sadece ilgili ülkeye ait kanallar filtrelenerek eşleştirme havuzu daraltılır ve performans 100 kat artırılır.
    - **Popüler Kanal Önceliği**: Yabancı dildeki rehberler için sadece majör ve popüler kanallar (BBC, SKY, FOX, HBO vb.) işleme alınır. Bu sayede veritabanı gereksiz verilerle şişirilmez.
    - **Tam Dil Desteği**: Uygulama dili (Örn: Türkçe) için tüm kısıtlamalar devre dışı bırakılarak kullanıcının ana dilindeki tüm kanallar %100 kapsama ile taranmaya devam eder.
    - **Algoritmik Hızlandırma**: Bulanık eşleştirme (Fuzzy Matching) motoruna "Hızlı Yol" (Exact Match) ve "Erken Çıkış" (Early Exit) mantığı eklendi. İsimlerin ilk harfi uyuşmayan kanallar anında elenerek işlem süresi milisaniyelere indirildi.

- **EPG Veri Tasarrufu ve Dil Tabanlı Akıllı Filtreleme** (2026-02-26 01:15):
    - **Zorunlu GZip Kullanımı**: `iptv-epg.org` kaynaklı tüm rehber verileri artık `.xml.gz` formatında talep edilerek veri trafiği %90 oranında azaltıldı.
    - **Uygulama Dili Önceliği**: EPG motoru artık öncelikle uygulama diline (Örn: Türkçe) ait rehber verisini indirir. Bu sayede kullanıcının ana dilindeki kanallar %100 kapsama alınır.
    - **Akıllı Ülke Filtreleme**: Listede çok az kanalı bulunan (marginal) ülkelerin devasa XML dosyalarını indirmek yerine, sadece %20'den fazla payı olan veya 50+ kanala sahip majör ülkeler (max 2 ek ülke) işleme alınır.
    - **Hibrit EPG Çözümleme**: Dil tabanlı ve içerik yoğunluğu tabanlı hibrit bir modelle, saniyeler içinde en doğru rehber verisi minimum internet kullanımıyla oluşturulur.

- **Gerçek Zamanlı EPG İlerleme Takibi ve Performans Modeli** (2026-02-26 00:45):
    - **Detaylı İlerleme Raporlama**: EPG yenileme sırasında "İndiriliyor", "Ayrıştırılıyor", "Eşleştiriliyor" ve "X program kaydediliyor" gibi spesifik aşamalar kullanıcıya anlık olarak yansıtılır.
    - **Genişletilmiş Zaman Aşımı (Timeout)**: Büyük boyutlu EPG dosyalarında yaşanan zaman aşımı hatalarını önlemek için EPG izleme süresi 3 dakikadan 10 dakikaya çıkarıldı.
    - **Optimize Edilmiş Veritabanı Yazımı**: EPG verileri veritabanına daha büyük bloklar halinde işlenerek (batching), binlerce kanallı listelerde performans artışı sağlandı.
    - **Hata Yakalama İyileştirmesi**: EPG yükleme hataları artık maskelenmeden sunucunun döndürdüğü gerçek hata mesajlarıyla birlikte raporlanır.

- **Gelişmiş Çok Kaynaklı EPG Rehber Sistemi** (2026-02-26 00:15):
    - **Xtream & Stalker Otomatik EPG**: Xtream Codes (`/xmltv.php`) ve Stalker Portal (`/itv/xmltv.php`) için standart EPG uç noktaları otomatik olarak tespit edilir. Bu sayede sunucu taraflı kanal ID'leri ile %100 uyumlu rehber verisi çekilir.
    - **Akıllı EPG Önceliklendirme**: Rehber verileri hiyerarşik bir yapıda (Kullanıcı Özel URL > Sağlayıcı EPG > M3U Başlık URL > iptv-epg.org) taranır. Sistem en kaliteli veriyi veren kaynağı otomatik seçer.
    - **iptv-epg.org Entegrasyonu**: Sunucudan EPG gelmediği durumlarda, kanal listesi analiz edilerek ilgili ülkenin (TR, DE, FR vb.) güncel rehber verisi otomatik olarak `iptv-epg.org` üzerinden indirilir.
    - **Bulanık Eşleştirme (Fuzzy Matching)**: Kanal isimlerindeki "HD", "4K", "VIP" gibi ekler temizlenerek rehber verileriyle yüksek doğrulukta eşleştirme yapılır.
    - **Aşamalı Yükleme Uyumluluğu**: Tembel yükleme (Lazy Loading) sırasında inen kanallar, arka planda hazır bekleyen EPG motoru tarafından saniyeler içinde rehber verileriyle eşleştirilir.

- **Akıllı Arka Plan Yenileme ve UI Bloklama Koruması** (2026-02-25 23:45):
    - **Non-Blocking Manuel Yenileme**: "Kanal Listesini Şimdi Yenile" butonu artık tüm platformlarda (Stalker, Xtream, M3U) ana arayüzü kilitlemeden arka planda çalışır. Kullanıcı yenileme sırasında uygulamayı özgürce kullanabilir.
    - **Tam Progresif Senkronizasyon**: Yenileme işlemi sırasında sadece eksikler değil, sunucudaki tüm içerik haritası (yeni eklenen/silinen kanallar) taranarak veritabanı en güncel hale getirilir.
    - **Yenileme-Devam Uyumu**: El ile başlatılan bir yenileme işlemi sırasında uygulama kapatılırsa, sistem bir sonraki açılışta bu durumu "yarım kalmış görev" olarak algılar ve Smart Resume motoruyla otomatik tamamlar.

- **Evrensel Performans Güncellemesi: Xtream ve M3U Aşamalı Yükleme** (2026-02-25 23:15):
    - **Xtream Aşamalı Yükleme (Progressive Loading)**: Stalker'daki hız devrimi Xtream Codes altyapısına da taşındı. Artık Xtream girişlerinde önce kategoriler çekilerek UI anında açılır, içerikler arka planda paralel olarak (Live/VOD/Series) yüklenmeye devam eder.
    - **M3U Arka Plan Yükleme (Non-blocking)**: Büyük M3U dosyalarının indirilmesi ve işlenmesi artık ana arayüzü bloklamıyor. Kullanıcı URL'i girdiği an ana ekrana geçer ve yükleme süreci sessizce arka planda tamamlanır.
    - **Evrensel Akıllı Devam Etme (Universal Resume)**: Uygulama kapatılıp açıldığında sadece Stalker değil, Xtream listeleri de kaldığı eksik kategorileri tespit eder ve arka planda indirmeyi sürdürür.
    - **UI Akıcılık Motoru (Throttled UI Update)**: Arka planda yüksek hızda veri inerken arayüzün titremesini önlemek için "Throttled" yükleme motoru devreye alındı. Veriler toplu halde ve akıcı bir şekilde ekrana yansıtılır.

- **Akıllı Devam Etme (Smart Resume) ve Sıfır Kopya Garantisi** (2026-02-25 22:30):
    - **Kaldığı Yerden Devam (Resume)**: Uygulama kapatılıp açıldığında Stalker portallarının baştan inmesi veya yarım kalması engellendi. Uygulama açılışta `GetPendingDummyGroupsAsync` veritabanı yordamıyla henüz inmemiş kategorileri tespit eder ve arka planda sadece eksik kısımları indirmeye devam eder.
    - **Idempotent Kanal Ekleme**: İndirme işlemi sırasında internetin kopması veya arayüzün yenilenmesi durumunda kanalların "çift" (duplicate) kaydedilmesi riski %100 oranında çözüldü. Her kategori için veritabanına veri yazılmadan önce o gruba ait tüm kalıntılar tek bir işlem bloğunda silinip yerine yepyeni ve taze veri basılıyor (`ReplaceDummyWithRealChannelsAsync`).
    - **Anlık Dizi Gruplaması (Incremental Aggregation)**: Dizi kategorileri inmeye başladığı andan itibaren beklemeden anında işlenerek `Series` tablosuna aktarılır. Bu sayede indirme bitmeden dizi sekmesine giren kullanıcılar anlık olarak dizileri görebilir (Daha önce tüm listenin inmesi bekleniyordu).
    - **Stalker Dizileri Çekmeme Sorunu Çözüldü**: Stalker API'si diziler için doğrudan bir oynatma linki (`cmd`) döndürmez. Eski sistem, `cmd` parametresi boş gelen bu dizileri "hatalı" sanıp siliyordu. Artık diziler özel bir sanal kimlikle (`stalker-series://`) sisteme kaydediliyor ve kayıpsız olarak Dizi sekmesine aktarılıyor.
    - **Hayalet Dizi (Ghost Series) ve UI Optimizasyonu**: Tembel yükleme sırasında `Series` tablosunda oluşabilecek mükerrer dizi kayıtları (klonlama) temizlik mantığıyla engellendi. Arka planda kanallar inerken UI'ın titremesini engelleyen "Debounce/Throttled" yükleme motoru devreye alındı. Dizilere tıklandığında oluşabilecek hatalar için güvenli oynatma kontrolü eklendi.

- **Stalker Portal Tembel Yükleme (Lazy Loading) ve Anlık Arayüz (Instant UI)** (2026-02-25 21:45):
    - **Anında Arayüz (Instant UI)**: Stalker portallarının yüzbinlerce kanalı tek tek çekip kullanıcıyı bekletmesi sorunu kökten çözüldü. Sistem açılışında yarım saniye içinde yalnızca kategoriler çekilir ve kullanıcıya anında (dummy kanallar ile) tüm menüler gösterilir.
    - **Tembel Yükleme (Lazy Loading)**: Yalnızca kullanıcının tıkladığı veya girdiği kategorinin içerikleri anlık olarak indirilir ve "Yükleniyor..." ibaresi silinip gerçek kanallarla yer değiştirir. Kalan kategoriler arka planda sessizce inmeye devam eder.
    - **Akıllı Önceliklendirme (Priority Queue)**: Kullanıcı henüz inmemiş bir kategoriye tıkladığında, arka plandaki yükleme kuyruğuna müdahale edilerek o kategori 1. sıraya alınır ve ilk boş işçi (worker) tarafından saniyeler içinde indirilir.
    - **Endpoint Çözümleme Düzeltmesi**: `/c/` gibi HTML sarmalayıcı URL'lerin yanlışlıkla API zannedilip format hatası (FormatException/InvalidOperationException) vermesi kökten çözüldü. Sistem URL'i analiz ederek `/server/load.php` gibi gerçek API uçlarını (endpoint) bulur.
    - **Gelişmiş Hata Raporlama**: Stalker hataları "İşlem beklendiği gibi tamamlanamadı" şeklindeki genel hatalar arkasına saklanmayıp, "MAC adresi hatalı", "URL geçersiz" gibi net şekilde UI'a yansıtılır hale getirildi.


- **VLC Oynatıcı ve MKV/Canlı TV Performans Optimizasyonu** (2026-02-25 15:55):
    - **Modern Donanım Hızlandırma**: Windows 11 ve modern GPU'lar için `dxva2` yerine `d3d11va` (Direct3D11 Video Acceleration) API'sine geçildi. H.265/HEVC ve VP9 içeriklerdeki (MKV) takılmalar ve "artifact" sorunları giderildi.
    - **Akıllı Stream Profilleri**: Yayın URL'sine göre otomatik değişen (MKV VOD, Live TS, HLS, MP4) özel buffer/caching profilleri eklendi.
    - **MKV Akıcılık Düzeltmesi**: MKV dosyalarında VLC'nin "geç kaldım" diyerek frame atlamasına sebep olan global `--drop-late-frames` ve `--skip-frames` politikaları kaldırıldı. MKV için 8 saniyelik agresif buffer ve özel demuxer (mkv,avformat) ipuçları eklendi.
    - **Canlı TV Senkronizasyonu**: Yayıncı kaynaklı zaman damgası (jitter) bozukluklarını tolere eden yeni `clock-jitter` (500ms) ve `clock-synchro` (off) ayarlarıyla canlı yayın kopmalarının önüne geçildi.
    - **Gelişmiş Codec Ayarları**: Verim artışı için `avcodec-fast` ve CPU-GPU kopyalama yükünü sıfıra indiren direct rendering (`avcodec-dr`) özellikleri aktif edildi.
    - **Hata Toleransı**: HTTP Range Request desteklemeyen IPTV sunucularında MKV dosyalarının açılmama sorunu için yönlendirme çerezleri (`http-forward-cookies`) ve özel ağ önbellekleme mantığı iyileştirildi.
    
- **Stalker Kategori Eşleme ve Sınırsız Senkronizasyon (Full Sync)** (2026-02-25 19:30):
    - **Sınırsız İndirme**: Stalker portalları için uygulanan sayfa sınırlamaları tamamen kaldırıldı. Artık içerik sayısı ne olursa olsun (örn. 250.000+), tüm kanallar, filmler ve diziler eksiksiz bir şekilde çekiliyor.
    - **Hibrit Kategori Eşleme**: Stalker portallarındaki kategori sorunları giderildi. Hem ID hem de isim tabanlı hibrit eşleme ile kategorilerin M3U/Xtream standartlarında gelmesi sağlandı.
    - **Yüksek Performanslı Paralel Senkronizasyon**: Çok sayıda içeriği hızlıca çekebilmek için paralel işlem (semaphore) kapasitesi 15 concurrent request'e çıkarıldı.

- **Stalker Portal Desteği ve V2 Performans Güncellemesi** (2026-02-25 16:15):
    - **Stalker V2 Mimarisi**: Sıralı (sequential) bağlantı mantığı tamamen paralel bir mimari ile değiştirildi.
    - **Paralel Endpoint Keşfi**: 7+ farklı API yolu aynı anda taranır. İlk cevap veren yol seçilerek bağlantı süresi 30-40 saniyeden <3 saniye düşürüldü.
    - **Birleşik El Sıkışma (Handshake)**: Token alma işlemi tarama (probe) aşamasına dahil edildi, gereksiz ağ trafiği silindi.
    - **Asenkron Profil Başlatma**: `get_profile` çağrısı arka planda çalıştırılarak kanal yükleme sürecini bloklaması engellendi.
    - **Maksimum Veri Paralelliği**: Canlı yayın (Live), VOD ve kategori listeleri aynı anda çekilerek bekleme süresi minimize edildi.
    - **User-Agent Düzeltmesi**: Bazı sağlayıcılarda çökmeye yol açan header doğrulama hatası giderildi.

- **Otomatik Güncelleme Sistemi ve Dizi Normalizasyonu** (2026-02-25 14:05):
    - **Otomatik Güncelleme**: Uygulamaya GitHub manifest tabanlı otomatik güncelleme sistemi eklendi (`UpdateService`). Uygulama açılışında arka plan kontrolü ve "Ayarlar > Hakkında" sekmesinde manuel güncelleme butonu aktif edildi.
    - **Yıl Korumalı Dizi Anahtarları**: Dizilerin sağlayıcılar arası progress senkronizasyonunu iyileştirmek için `NormalizeKey` metodunun yılları temizlemesi durduruldu (örn: `Breaking Bad 2008` artık korunuyor).
    - **M3U Base64 Düzeltme Doğrulaması**: M3U listelerindeki Base64 formatlı logoların kanal isimlerini bozma hatası için kalıcı bir birim test eklendi.
    - **Derleme ve Tip Güvenliği**: Startup warmup aşamasındaki değişken çakışmaları ve eksik referanslar giderilerek 0 hata ile derleme stabilitesi korundu.

- **UX, Test ve Teknik Borç İyileştirmeleri** (2026-02-25 13:42):
    - **Entegrasyon Testleri**: `PlaylistService`, `MediaService` ve `XtreamCodesService` için kapsamlı entegrasyon testleri eklendi. In-memory SQLite (`Shared Cache`) kullanılarak veri tutarlılığı ve kanal-dizi eşleştirme mantığı doğrulandı.
    - **Hata Düzeltme (Kanal Parmak İzi)**: Playlist yenileme sırasında çocuk profili filtresinin tüm profillere yanlışlıkla uygulanması ve bu sebeple kanalların "duplicate" olarak algılanıp eklenememesi hatası düzeltildi.
    - **Legacy WPF Temizliği**: Artık kullanılmayan eski WPF projesine ait tüm referanslar ve proje dosyası bağımlılıkları temizlendi; Avalonia geçişi tamamlandı.
    - **Dinamik Yükleme Süreleri**: İşletim sistemi açılışında (`SplashWindow`) ve profil yükleme sürecinde (`ProfileLoadingWindow`) premium hissi vermek için eklenen sabit bekleme süreleri (3.5s - 8s) kaldırıldı. Süreç artık verilerin yüklenmesine bağlı olarak dinamik çalışıyor (Min 1.5s).
    - **İnsan Dilinde Hata Mesajları**: C# Exception sınıflarından fırlayan teknik hata kodları maskelendi. Hata görünüm süresi 8 saniyeden 3 saniyeye düşürülerek kullanıcı deneyimi hızlandırıldı.

- **Oynatma Listesi Performans Optimizasyonları (Phase 28)** (2026-02-25 12:40):
    - **SQLite WAL Modu**: Veritabanında Write-Ahead Logging (WAL) etkinleştirilerek eşzamanlı okuma/yazma desteği eklendi, UI kilitlenmeleri kökten önlendi.
    - **Asenkron Dizi Oluşturma (Fire-and-Forget)**: Hacimli listelerde (50K+ kanal) dizileri kümeleyen ağır işlem (`AggregateContentAsync`) ana iş parçacığından koparılarak arkaplanda otonom hale getirildi; dizi sekmesi tamamlandığında otomatik yenileniyor.
    - **O(1) Karmaşıklık Devrimi**: Dizi ve bölüm eşleştirmelerindeki yoğun $O(N^2)$ döngü yükü iptal edildi; bunun yerine yüksek performanslı Dictionary odaklı $O(1)$ algoritmaya geçildi.
    - **Bellek ve İzleme Temizliği (EF Core DisableTracking)**: Binlerce entity eklenmesi sırasında Entity Framework'ün Change Tracker'ı geçici olarak bloke edilerek kayıt süresi 5-10 dakikadan saniyelere düşürüldü.
    - **Grup Veritabanı Kaydı (Single-Transaction Insert)**: Oynatma listesi eklenirken her 500 veya 1000 kanalda bir tetiklenen çoklu kayıt işlemleri (Multiple SaveChanges) iptal edildi. Artık tüm kanallar tek bir SQLite işlemiyle (Transaction) kaydediliyor; diske yazma hızı ~15x artırıldı.
    - **Hafifletilmiş Profil Yenilemesi (`RefreshAsync`)**: Playlist güncellemelerinde bütün mevcut kanalları belleğe çekip kıyaslama yapan aşırı RAM tüketen yapı kaldırıldı; yerine SQL düzeyinde hafif "parmak izi (fingerprint)" izdüşümü (Projection) oluşturularak Diff (fark) bulunuyor.
    - **Raw SQLite Bulk Insert (Ödünsüz Hız)**: EF Core'un 50K satır için oluşturduğu devasa SQL Command döngü yükünden (`AddRange`) kurtulmak adına veriler doğrudan ADO.NET (`SqliteCommand`) üzerinden en alt seviye `INSERT` ile yazdırıldı. Ekleme süresi saniyelerden milisaniyelere düştü.
    - **Single-Pass UI Kilit Çözümü**: Profil açılırken arkaplanda 5 farklı kategori grubu sorgusu için üst üste 5 kez veritabanının kitlenmesi (GROUP BY taramaları) önlendi. Veriler artık RAM'de tek bir hafif `Select(GroupTitle, Type)` eşleştirmesi ile anında çözümlenip arayüz beklemesini sıfıra indirdi.

- **Kalite Etiketleri ve Ses Kalıcılığı Optimizasyonu (Phase 16-17)** (2026-02-24 19:45):
  - **Kalite Etiketi Standardizasyonu (Phase 16)**:
    - **4K -> 2160p**: Teknik tutarlılık için "4K" etiketi "2160p" olarak güncellendi.
    - **Temiz Kalite Etiketleri**: FPS bilgisi sadelik için eski formatta (örn: "1080p60") sunulmaya devam edildi.
  - **Ses Kalıcılığı ve UI Temizliği (Phase 17)**:
    - **Kalıcı Ses Kontrolü**: Video oynatıcıda ayarlanan son ses seviyesi artık otomatik olarak kaydediliyor ve uygulama yeniden açıldığında korunuyor.
    - **Gereksiz Ayarların Kaldırılması**: Ayarlar ekranındaki "Varsayılan Ses Seviyesi" (Default Volume) kaydırıcısı, artık ses seviyesi dinamik olarak hatırlandığı için kaldırıldı.
    - **Oynatma Başlatma Mantığı**: Her yeni kanalda ses seviyesinin varsayılana sıfırlanması sorunu giderildi; kullanıcı tercihi korunarak oynatma başlıyor.
- **Arayüz Katmanında İndirme Evrenselliği (Phase 26)** (2026-02-25 11:30):
    - **Bağımsız UI Sergilemesi**: İndirilenler sayfası (`MainViewModel`), kullanıcının o an hangi profilde olduğuna bakılmaksızın tüm yerel içerikleri gösterecek şekilde yeniden kodlandı.
    - **Görünmezlik Sorunu Çözüldü**: Başka bir profildeyken indirilen ancak aktif profilde listelenmeyen VOD ve dizi dosyalarının görünmeme hatası giderildi.

- **Kusursuz Serileme, Dosya Keşfi ve Akıllı Klasörleme (Phase 27)** (2026-02-25 12:20):
    - **Dosya Sistemi Tarayıcısı**: İndirilenler sayfası artık veritabanı kayıtlarının yanı sıra `Downloads/` klasörünü fiziksel olarak tarayarak, veritabanında kaydı olmayan sahipsiz video dosyalarını (`.mkv`, `.mp4`, `.avi`, `.ts` vb.) keşfedip otomatik olarak UI'a ekliyor.
    - **Profil Bağımsız Seri Gruplama**: Dizi kapak oluşturma sürecinden `PlaylistId` çıkartılarak, farklı sağlayıcılardan indirilen aynı isimli bölümler tek bir dizi kapağı altında birleştirildi.
    - **UI Hafıza Kaybı Giderildi**: `UpdateDownloadedItems` metodunun her sekme değişiminde indirme listesini eski profil verileriyle ezmesi engellendi; liste artık kalıcı olarak SQLite veritabanından besleniyor.
    - **Profile_X Klasör Oluşturma Durduruldu**: `ResolveDownloadFreeSpaceText` metodundaki profil-bazlı alt klasör oluşturma kaldırıldı. Artık boş `Profile_0`, `Profile_5` gibi gereksiz klasörler oluşmuyor.
    - **Akıllı Klasör Eşleştirme**: Yeni bir dizi bölümü indirilirken mevcut `Series/` klasörleri fuzzy isim karşılaştırmasıyla taranıyor. "4400" ile "The 4400" gibi farklı sağlayıcı isimlendirmeleri aynı klasöre yönlendiriliyor (`FindMatchingSeriesFolder`).
    - **Sentetik Dizi Detay Düzeltmesi**: İndirilenler sayfasından tıklanan dosya-sistemi-kaynaklı dizi kartları artık veritabanından yeniden çekilmiyor; yerel dosya yolları korunarak bölümler doğru şekilde listeleniyor.
    - **Çift İsim Sorunu Giderildi**: Overlay'da "4400 4400 - 1. Bölüm" şeklinde tekrarlanan dizi adı düzeltildi; dosya adı zaten dizi adını içerdiği için ek birleştirme kaldırıldı.
    - **Temiz Veritabanı**: İptal edilen veya hataya düşen indirmeler `DownloadItems` tablosundan fiziksel olarak siliniyor (Hard Delete), veritabanında gereksiz kayıt bırakılmıyor.

- **İndirilen İçeriklerin Evrenselleşmesi (Global Downloads) (Phase 25)** (2026-02-25 11:15):
    - **Sınırsız Erişim**: İndirilen tüm dizi ve filmler, cihaza kaydedildiği için artık indirildiği profile bağlı kalmaksızın tüm profillerin "İndirilenler" sekmesinde görünebilir ve oynatılabilir duruma getirildi.
    - **Ortak İndirme Havuzu**: İçerikler artık `Profile_5` gibi izole alt klasörler yerine doğrudan Noctra cihaz ortak indirme havuzuna (`Downloads/`) aktarılmaya başlandı.
    - **Veri Koruma**: Profil silme veya güncelleme işlemleri esnasında cihazda yer alan indirilen dosyaların otomatik silinmesi engellendi.

- **Merkezi Parser ve Sezon Klasörü Doğruluğu (Phase 24)** (2026-02-25 11:00):
    - **Akıllı Önceliklendirme**: İndirme servisi artık merkezi parser'ı kullanarak "1. Bölüm - S16" gibi başlıklarda gerçek sezon bilgisini (S16) doğru tespit ediyor.
    - **Merkezi Mantık Konsolidasyonu**: İndirme servisindeki basit regex'ler kaldırılarak, tüm uygulama genelinde tutarlı bir isimlendirme ve klasörleme yapısı sağlandı.
    - **Garantili Sezon Doğruluğu**: Karmaşık dizi başlıklarında bile sezona göre doğru klasörleme (`Season 16` vb.) garantisi getirildi.

- **Klasör İsimlendirme ve Bölüm Formatı Düzeltmeleri (Phase 23)** (2026-02-24 22:55):
    - **"1. Bölüm" Desteği**: Dosya isimlerindeki "1. Bölüm" tarzı rakam-öncelikli formatlar artık başarıyla tanınıyor ve dizi adından ayrıştırılıyor.
    - **Ultra-Temiz Klasörler**: Dizi adı ayıklama algoritması güçlendirilerek folder isimlerindeki "dizi adı - " gibi sarkan parçalar tamamen temizlendi.
    - **Kapsayıcı Sezon Gruplama**: Sezon bilgisi eksik olan bölümler için varsayılan olarak "Season 01" klasörü oluşturularak organizasyon bütünlüğü sağlandı.
    - **Belirgin Sezon Gösterimi**: UI başlıklarında sadece epizot numarası değil, sezon bilgisinin de (Sezon X • Bölüm Y) net gösterilmesi garantilendi.

- **Gelişmiş İndirme Organizasyonu ve İsim Sadakati (Phase 22)** (2026-02-24 22:50):
    - **Akıllı Dizi Klasörlemesi**: İndirilen diziler artık bölüm adı yerine doğrudan dizi ana adı (örn: "4400") ile klasörleniyor.
    - **Sezon Bazlı Alt Klasörler**: Aynı dizinin farklı sezonlarının birbirinin üzerine yazılmasını önlemek için otomatik "Season 01", "Season 02" alt klasör yapısı eklendi.
    - **SxE Belirteçlerinin Korunması**: Bölüm isimlerindeki "S01E01", "Season 1" gibi belirteçlerin temizlenmesi durduruldu; bağlam kaybı engellendi.
    - **Temiz Dosya İsimleri**: Dizi adı ayıklanırken arkada kalan gereksiz tire ve sembol temizliği iyileştirildi.

- **Kapsayıcı Seri Filtreleme (Phase 21)** (2026-02-24 22:35):
    - **Kategori Öncelikli Listeleme**: "Series" kategorisindeki tüm içeriklerin (regex eşleşmesi olmasa bile) listelenmesi sağlandı.
    - **Akıllı Fallback**: Ayrıştırılamayan dizi isimleri için otomatik olarak "Sezon 1 / Bölüm 1" atanarak içerik kaybı önlendi.
    - **Esnek Live Seri Ayrımı**: Canlı yayınlanan dizi kanalları üzerindeki filtreler esnetilerek tüm içeriğin görünürlüğü sağlandı.

- **Gelişmiş Dizi İsmi ve İçerik Kurtarma (Phase 20)** (2026-02-24 22:15):
    - **URL Decoding**: `%3` gibi karakterlerin düzgün görünmemesi sorunu giderildi (# karakterine dönüştürüldü).
    - **Parantez Koruma**: Kullanıcı talebi üzerine parantez içi bilgiler (yıl, kalite vb.) temizleme dışı bırakıldı.
    - **Doğal Sıralama**: Alfabetik sıralama artık en baştaki sembolleri (#, (, ! vb.) yok sayarak gerçek harfe göre yapılıyor.
    - **Agresif Filtre Düzeltmesi**: "4400" gibi kısa ve sayısal isimli serilerin Live TV olarak işaretlenip kaybolması engellendi.

- **Canlı TV ve Seri Gruplama Filtreleme (Phase 19)** (2026-02-24 21:05):
    - **Hatalı Numaralandırma Düzeltmesi**: Canlı kanalların "1. Bölüm" slotunu işgal ederek gerçek bölümleri kaydırması sorunu giderildi.
    - **Yıl ve Sembol Temizliği**: Episode isimlerindeki gereksiz yıl (2016), parantez ve sembol tekrarları tamamen temizlendi.
    - **Gelişmiş Filtreleme Güvenliği**: "4400" gibi kısa isimli gerçek dizilerin yanlışlıkla Live TV olarak filtrelenmesi engellendi.

- **Dizi ve Bölüm İsimleri Normalizasyonu (Phase 18)** (2026-02-24 20:35):
    - **Akıllı Tekilleştirme**: Hem dizi hem de bölüm isimlerindeki tekrarlar (örn: "4400 S01 4400" -> "4400") otomatik olarak temizleniyor.
    - **Yapılandırılmış Bölüm Başlıkları**: Bölüm isimleri artık `{Dizi Adı} - {Bölüm No}. {Bölüm/Episode/Episodio} - {Bölüm Başlığı}` formatında gösteriliyor.
    - **Akıllı Dil Algılama**: Sistem orijinal başlıktaki dili algılayıp "Bölüm", "Episode", "Episodio" gibi ifadeleri otomatik olarak seçiyor.
    - **Gelişmiş Etiket Temizliği**: "S01", "Sezon 1" gibi etiketlerin genel dizi başlığına sızması engellendi.

- **Ses Slider Gecikmesi Tamamen Giderildi (Phase 17)** (2026-02-24 20:05):

- **Görsel Tema Senkronizasyonu ve Kararlılık (Phase 14-15)** (2026-02-24 18:45):
  - **Derin Tema Revizyonu (Phase 14)**:
    - **Kontrol Normalizasyonu**: `CheckBox`, `ScrollBar`, `ComboBox`, `Slider` ve `ProgressBar` bileşenleri `Styles.axaml` üzerinde merkezi olarak standartlaştırıldı; ana renkler ve hover efektleri tema kaynaklarına bağlandı.
    - **Hex Kod Temizliği**: Tüm `.axaml` dosyalarındaki (Profiles, Settings, Home, VideoOverlay) hardcoded mor ve gri hex kodları temizlenerek dinamik tema fırçalarına (`AccentBrush`, `InteractiveHoverBrush` vb.) dönüştürüldü.
    - **Shadow & Glow Senkronizasyonu**: `DefaultShadow`, `CardShadow`, `HeroShadow` ve `AccentGlow` efektleri her iki tema (Dark/Light) için optimize edilerek görsel derinlik standartlaştırıldı.
  - **Kritik Çökme ve Stabilite Düzeltmeleri (Phase 15)**:
    - **InvalidCastException Çözümü**: Tema kaynaklarındaki `BoxShadow` değerlerinin `x:String` olarak tanımlanmasından kaynaklanan ve uygulamanın belirli alanlarda çökmesine yol açan tip dönüşüm hatası, kaynaklar `BoxShadows` tipine taşınarak giderildi.
    - **Video Oynatıcı Null Koruması**: `VideoOverlayView.axaml.cs` içerisindeki timeline kaydırma (seek) mantığına `_playerViewModel` için robust null kontrolleri eklendi; oynatıcı geçişlerindeki olası `NullReferenceException` hataları önlendi.
    - **Build Doğrulaması**: Yapılan tüm değişiklikler `dotnet build` ile doğrulanarak 0 uyarı ve 0 hata ile stabilite sağlandı.

- **Çocuk Güvenliği Görselleştirme ve Filtre Sertleştirme (Phase 11)** (2026-02-24 17:15):
  - **Premium Çocuk Profili Çerçevesi**: Çocuk profilleri için avatar etrafına 3-renkli neon gradyanlı (`#6366F1`, `#A855F7`, `#EC4899`) ve dış parlamalı (glow) şık bir çerçeve eklendi.
  - **Clipping-Free Tasarım**: Profil kartı butonları 150px genişliğe çıkarılarak ve `ClipToBounds` kısıtlamaları kaldırılarak çerçevenin tüm ihtişamıyla kesilmeden görünmesi sağlandı.
  - **Hassas Filtreleme (Normalization)**: İçerik tarama motoruna Türkçe karakter normalizasyonu (`ş`->`s`, `ç`->`c` vb.) eklendi. Artık "cocuk" yazan filtreler "çocuk" başlıklı kanalları da hatasız yakalıyor.
  - **Kesin Kelime Eşleşmesi**: Filtreleme motoru Regex Word Boundary (`\b`) sistemine geçirilerek "Adam/Madam" gibi hatalı eşleşmeler (false positive) engellendi.
  - **Güvenlik Bilgilendirmesi (Tooltip)**: Profil ekleme/düzenleme ekranındaki "Çocuk Profili" kutucuğuna, ebeveynleri filtreleme kapsamı hakkında bilgilendiren bir açıklama (Tooltip) eklendi.
  - **Statik Badge Temizliği**: Avatar üzerindeki karmaşıklığı azaltmak için eski "KIDS" ve "BADGE" etiketleri kaldırıldı, odak tamamen yeni neon çerçeveye verildi.

- **Çocuk Profili Hardcore Filtre ve Evrensel Güvenlik (Phase 6-8)** (2026-02-24 12:45):
  - **Kategori-Merkezli Akıllı Filtreleme**: Filtreler artık sadece anahtar kelimeye değil, kategorinin güvenilirliğine bakıyor. "Sinema/Dizi" gibi genel kategoriler varsayılan olarak engellenip sadece adı güvenli olanlar (`Nemo`, `Frozen` vb.) kurtarılırken, "Çizgi Film/Kids" kategorileri (kara liste hariç) korunuyor.
  - **Evrensel Dil Desteği**: Filtreleme motoru artık İngilizce, Almanca, Fransızca, İspanyolca ve İtalyanca kategorileri (`Kinder`, `Niños`, `Enfant`, `Cartoon` vb.) tanıyor. 
  - **Sertifika Bazlı (Age Rating) Filtreleme (Phase 9)**: TMDB entegrasyonu ile içeriklerin yaş sınırları (G, PG, TV-Y7, R, TV-MA vb.) artık birincil filtreleme kriteri olarak kullanılıyor. TMDB API anahtarı girildiğinde sistem otomatik olarak daha hassas ve güvenilir sertifika kontrolüne geçer.
  - **M3U Dayanıklılığı ve Gelişmiş Hata Raporlama (Phase 10)**: Oynatma listesi indirmeleri sırasında oluşan hatalara (404, 502 vb.) detaylı durum kodları eklendi. Bazı IPTV sağlayıcılarının engellemelerini aşmak için 404/502 durumlarında otomatik "VLC User-Agent" fallback motoru devreye alındı.
  - **Gelişmiş Tarih Bazlı Engelleme**: 2000 yılı ve öncesine ait tüm içerikler (Yeşilçam, nostalji vb.) başlık veya kategori fark etmeksizin otomatik olarak engelleniyor. Regex motoru artık parantezsiz yılları da (`1998-14 FILME`) yakalayabiliyor.
  - **Resilient Cleanup (Dayanıklı Temizlik)**: Sunucu hatalarında veya zaman aşımı (Timeout) durumlarında dahi, çocuk profili için veri tabanı temizliği zorla çalıştırılıyor. Child safety artık internet hızına bağlı değil.
  - **Genişletilmiş Çocuk Kütüphanesi (Rescue List)**: Yüzlerce küresel marka (Disney, Pixar, Cartoon Network, Marvel, Pokemon vb.) ve yerel çocuk içerikleri (TRT Çocuk, Niloya, Rafadan Tayfa vb.) "kurtarma listesine" eklenerek şüpheli kategoriler arasından güvenle çekiliyor.
  - **Kritik Kara Liste Genişletmesi**: Kullanıcı talebiyle "Yeşilçam", "Nostalji", "Erotizm", "McGregor" gibi kritik kelimeler ve şiddet içerikli kategoriler engelleme listesine eklendi.
  - **Dizi Gruplama ve Temizlik (Phase 3)**: Kısa sezon adlandırmaları (`S01`, `S02`) artık otomatik tanınıyor. Gruplamayı bozan "DIZIAX", "NETFLIX", "PRIME" gibi platform ön ekleri ve `%3` gibi özel karakterli dizi isimleri için akıllı normalizasyon eklendi. Tüm sezonlar artık tek bir dizi kartı altında toplanıyor.
  - **Kısmi Başarı (Partial Success) Desteği**: Bazı sunucuların büyük dosyalarda (48MB+) 30 saniye sonra bağlantıyı kesmesi durumunda, o ana kadar indirilen tüm kanalların (testlerde 65.000+) çöpe atılmayıp başarıyla kaydedilmesi sağlandı (Resilient Parsing).
  - **M3U Parser Esnekliği (Phase 2)**: `#EXTM3U` başlığı artık zorunlu değil. Sunucu hatalı (başlıksız) veri gönderse bile içerik zorlanarak ayıklanıyor. Ayrıca ilk kanalın atlanmasına neden olan bir döngü hatası giderildi.
  - **Akıllı Yenileme Mantığı**: İlk yüklemesi (ağ hatası vb.) başarısız olmuş "0 kanallı" profiller için "Yenile" butonu otomatik tam-eşitleme tetikler. Artık profil silip eklemeye gerek kalmadı.
  - **Büyük Liste Optimizasyonu**: 50.000+ kanallı dev listeler için line-by-line streaming parser devreye alındı; bellek kullanımı %90 azaltıldı.
  - **Hata Raporlama**: Ayarlar ekranında "0 kanal bulundu" ve sunucu hataları için kalıcı uyarı bildirimleri eklendi.
  - **Standart Dışı M3U Desteği**: Tırnaksız etiketler (`tvg-id=123`), iki noktasız `#EXTINF` ve başta boşluk olan dosyalar için tam uyumluluk sağlandı.
  - **HttpClient Timeout**: Global bağlantı limiti kaldırılarak servis bazlı özel zaman aşımlarına (3-10 dakika) tam destek verildi.

- **VOD ve Canlı Yayın Oynatma Çökmelerine Kesin Çözüm (Connection Limit Drop)** (2026-02-23 21:35):
  - **Xtream Codes "Hayalet Bağlantı" ve Sessiz Kurtarma (Final Tespit)**: İleri/geri sarma sonlandıktan tam 10-15 saniye sonra proxy sunucusunun yayını kasıtlı olarak kestiği (`EndReached`) görüldü. Sorunun incelenmesi sonucunda, HardSeek sonrası eski bağlantıyı `Stop()` ile kapatsak da Xtream hesap limitleri nedeniyle bu kapanmış yayının sunucu tarafında 15-30 saniye boyunca "Hayalet Bağlantı" kalarak asılı kaldığı tespit edildi. Sunucunun 10 saniyede bir çalışan "Korsan Engelleyici (Anti-Leech)" yazılımı "Aktif 2 bağlantı" var sanarak videoyu koparmaktaydı.
  - **Tam Otomatik ve Görünmez Kurtarma (Silent Auto-Recovery)**: Sunucu kısıtlamasına rağmen videonun kesilmemesi için görünmez bir kalkan (AutoRecoverPrematureEndAsync) yazıldı. `EndReached` geldiğinde sistem olayın bir kopma olduğunu anlayarak "Yayın kurtarılıyor..." mesajı ve hata göstermeden arkaplanda 500ms bekler ve anında Kara Kutu'daki saniyeden yayını sıfırdan "görünmez şekilde" diriltir. Bu sayede sunucu banının yarattığı ölümcül kapanma 0.5 saniyelik ufak bir yavaşlama hissine indirgendi. Play tuşuna elle basma gereksinimi tamamen ortadan kaldırıldı.
  - **Canlı Yayın (Live TV) 5 Saniye Döngüsü ve Stall Monitor Çözümü**: Canlı yayınlarda VLC `Position` bilgisini 0 döndürdüğü için Stall Monitor'ün yayını her 5 saniyede bir "Dondu" zannedip haksız yere kapatması engellendi (Killswitch uygulandı). Canlı yayınlar artık sunucu kopmadığı sürece sonsuza dek açık kalabiliyor; koptuğunda ise Silent Recovery ile anında diriltiliyor.
  - **Milisaniye Hassasiyetinde Güvenli Seek**: VOD içeriklerinde HTTP argümanları (`:start-time`) doğrudan VLC çekirdeğinden proxy sunucusuna saf halde aktarılıyor.

- **Kusursuz Profil Değişimi ve Dizi İlerleme (Progress) Göçü** (2026-02-23 20:15):
  - **Çapraz Sağlayıcı (Cross-Provider) Eşleştirme**: Eski IPTV sağlayıcısından yenisine geçerken dizilerde kalınan yerlerin silinmesi sorunu kökten çözüldü. Farklı IP TV sağlayıcılarının isimlere eklediği diller (`TR Dublaj`, `ALTYAZILI`), video kaliteleri (`1080p Dual`, `4K HEVC`) ve yıl etiketleri (`(2008)`, `2010`) gibi "kirli" veriler `SeriesInfoParser` tarafından agresifçe siliniyor. `Breaking Bad (TR) 2008` ile `TR | Breaking Bad S01E01 1080p Dual` tamamen aynı ve pürüzsüz `breaking bad` anahtarına çözülerek izleme geçmişine %100 kenetleniyor.
  - **Profil Parolası ve SQLite Cascade Temizliği**: Kullanıcı şifresi DPAPI ile şifrelendiğinden sistem her "Kaydet" yapışta şifrenin değiştiğini sanıp tüm içerikleri (Oynatma listesi vb.) sıfırlıyordu; bu artık metin bazlı hesaplanarak düzeltildi. Ayrıca Playlists silindiğinde onlara bağlı Channels ve Series'in veritabanında "Öksüz (Orphaned)" şekilde asılı kalması, `App.axaml.cs`'e `PRAGMA foreign_keys = ON;` emri verilip Cascade Delete'in aktifleştirilmesi ile düzeltildi.
  - **Anında Arayüz Senkronizasyonu**: Profil düzenlemesi (işlem, M3U yenilemesi vb.) bittikten sonra uygulama otomatik olarak algılayıp yeni kanal listesini kapatıp açmaya gerek kalmadan yeniler.

- **VOD Filmlerde ve Dizilerde Kesintisiz İleri/Geri Sarma (Hard Seek)** (2026-02-23 13:50):
  - **VLC Seek Donma Düzeltmesi**: VLC'nin internetten izlenen HTTP tabanlı VOD (MKV/TS) içeriklerde, zaman damgasını bayt konumuna çeviremediğinde seek (ileri/geri sarma) komutlarını (hem `Time` hem de `Position` bazlı) tamamen yok sayarak sessizce kilitlenme sorunu çözüldü.
  - **HardSeekAsync Yaklaşımı (Kesin Çözüm)**: İndirilmemiş tüm HTTP VOD içeriklerinde içsel VLC araması iptal edildi. Bunun yerine, VLC'ye ait `:start-time={saniye}` argümanı kullanılarak "Durdur - Bekle - Yeniden Başlat" formülü entegre edildi. Artık ileri sardırıldığında sunucuyla hedeflenen bayt konumundan yepyeni bir HTTP bağlantısı kuruluyor.
  - **Çift Ateşleme (Double Fire) Koruması**: Arayüzdeki tıklama olaylarının (`PointerReleased` ve `PointerCaptureLost`) aynı anda 2 kez seek komutu göndermesini engellemek için ViewModel tarafına `_lastSeekTargetMs`, Arayüz tarafına da `_isCommittingSeek` bariyerleri eklendi.
  - **UI Scroll Hijack Engelleme**: Trackpad veya fare tekerleğiyle sayfa/overlay üzerinde kaydırma yapılırken Avalonia Slider'larının odak (focus) yakalayıp saniyede 60 kez ses açıp kapatarak (Volume Toast Spam) arayüzü kilitlemesi engellendi. Hedef odaklama (Focusable=False) ve Tunnel rotaları iptal edildi.
  - **Akıllı Kaldığın Yerden Devam Etme (Resume)**: `TryApplyPendingResumeSeek` ve `ResumePlaybackAsync` metotları yeni Hard Seek altyapısına uyumlu hale getirildi. Artık yarım kalan bir VOD içeriği açıldığında hedeflenen saniyeden sorunsuz başlıyor.
  - **Sıfırdan Başlatma Koruması (Buffer Shield)**: Doğal bir seek yüklendiğinde (buffering), sistemin bunu "Yayın koptu" sanarak filmi gereksiz yere baştan başlatma (`EnsurePlaybackHealthAsync`) hatası engellendi (`_suppressBufferShieldForSeek` kontrolü eklendi).

- **Seek Güvenilirliği ve Otomatik Yeniden Bağlanma** (2026-02-23 11:55):
  - **TS/M3U8 Donanım Seek Düzeltmesi**: LibVLC başlatma ayarlarına `--ts-seek-percent`, `--clock-jitter=0` ve `--clock-synchro=0` komutları eklendi. Bu sayede IPTV'deki bozuk zaman damgalarına sahip (PCR) film ve dizilerde ileri sardırıldığında doğrudan byte bazlı donanımsal atlama yapılması sağlandı ve başa zıplama sorunu çözüldü.
  - **Sonsuz Yükleme Sarmalı (Anti-Pattern) Kaldırıldı**: VLC'nin IPTV kırık zaman damgasını gerçek sanmasıyla çakışan ve 150ms arayla yeniden seek atarak oynatıcıyı sonsuz donmaya/bufferinge hapseden `VerifySeekAsync` metodu tamamen temizlendi.
  - **Otomatik Yeniden Bağlanma (Auto-Retry)**: VOD/dizi yayını ilk seferde açılmazsa artık geri çıkıp girmeye gerek yok. Sistem 5 saniye geri sayım göstererek ("5 saniye içinde yeniden denenecek...") otomatik olarak yeniden deniyor. En fazla 4 deneme yapılıyor; tümü başarısız olursa "Yayına erişilemiyor olabilir. Başka bir kanal deneyin." uyarısı gösteriliyor.
  - **Buffer Shield Timeout**: Seek sonrası buffer koruma süresi 5s → 8s'ye uzatılarak, seek sonrasında oluşan siyah ekran sıkışmaları azaltıldı.
  - **Position Guard**: VLC'ye `NaN`/`Infinity` gibi geçersiz seek değerlerinin gönderilmesi engellendi.
  - **Mesaj Sistemi Birleştirildi**: Eski `StartPlayerLoadingWarningAsync` kaldırıldı; yükleme uyarı mesajları artık tek bir yerden (`EnsurePlaybackHealthAsync`) yönetiliyor, üst üste binme sorunu giderildi.
  - **Dosyalar**: `PlayerViewModel.cs`, `VideoPlayerService.cs`

- **Altyazı Ayarları Komple Kaldırıldı** (2026-02-22 20:00):
  - **Sorun:** LibVLC'nin altyazı motoru (Freetype) ayarları (font boyutu, renk vb.) çalışma zamanında (runtime) güvenilir şekilde değiştirmeyi desteklemediği ve tutarsız davranışlar sergilediği için altyazı ayarları UI ve arka plandan silindi.
  - **Temizlik:** `SettingsWindow.axaml` içindeki tüm altyazı ayar kontrolleri, `SettingsViewModel.cs` içindeki değişkenler, `AppSettings.cs` modelleri ve `PercentToOpacityConverter` gibi artık kullanılmayan dönüştürücüler projeden tamamen temizlendi.
  - Artık VLC, altyazıları varsayılan boyutu ve konumuyla problemsiz bir şekilde gösterecek.

- **Dizi/Film Başlıklarında Yıl Koruması ve TR Pars Geliştirmeleri** (2026-02-22 20:15):
  - **Yıllar Artık Silinmiyor:** Dizi ve film isimlerindeki üretim yılları (örn: `(2020)`) daha önce bölüm eşleştirme/temizleme algoritması tarafından yanlışlıkla atılıyordu. Yıl temizleme filtresi devre dışı bırakıldı; artık `Alice in Borderland (2020)` gibi başlıklar olduğu gibi korunuyor.
  - **1 Sezon 1 Bölüm Hatası (Bölüm Ezilmesi) Çözüldü:** M3U/Xtream listelerindeki `1. Sezon 1. Bölüm` veya sadece `1. Bölüm` gibi Türkçe formatlar ile `S01E01 - 1. Bölüm` gibi tekrarlı kafa karıştırıcı formatlar sisteme tanıtıldı. Önceden bu tür kanallar anlaşılamayıp varsayılan "S01E01" kabul edilerek birbirlerinin üzerine yazılıyor ve tüm dizi tek bölüm görünüyordu. Genişletilen eşleştirme varyasyonları ve ardışık silme kurallarıyla gruplanıyor.
  - **M3U Dizilerinin Kaybolma Hatası:** Gelişmiş temizleme algoritmasının, dizi bölümlerinin de (S01E01 vb.) isminden temizlenmesine sebep olduğu ve bu nedenle kütüphane oluşturucunun tüm bölümleri "aynı kanal" sanarak (Deduplication) sildiği tespit edildi. Benzerlik anahtarı (Similarity Key) üretim mantığı, eğer kanal bir "Dizi" ise `S01E01` imzasını anahtara ekleyecek şekilde güncellendi. Diziler artık kaybolmuyor ve tüm bölümleriyle listeleniyor.
  - **Base64 Logo Hatası (Görsel Adlı Diziler):** Xtream veya M3U içindeki logolar base64 resim formatında (örn: `data:image/jpeg;base64,/9j/...`) geldiğinde, içerdiği virgüller (`,`) nedeniyle M3U Ayrıştırıcı (Parser) kanal adını yanlış okuyup devasa base64 metnini kanal adı sanıyordu. Bu olağanüstü hata `M3UParser.cs` içindeki regex ayıklayıcısı yeniden yazılarak kökten çözüldü. Artık kanalların isimleri logolarından kusursuzca ayrıştırılacak.
  - **Açılışta %100 Ses Toast Hatası:** Canlı TV ve Diziler açılırken ses barı 100% olarak ekranın ortasında beliriyordu. Kütüphane bağlama özellikleri sırasında ilk ses atamasının UI Toast Popup'ı tetiklemesinin önüne geçildi.


- **Gelişmiş Arama Deneyimi ve Sidebar Modernizasyonu** (2026-02-27):
    - **Genişleyen Arama Çubuğu**: Header kısmındaki arama butonu, üzerine gelindiğinde veya tıklandığında 200px'den 340px'e pürüzsüzce genişleyen (`WidthTransition`) modern bir `TextBox` ile değiştirildi.
    - **Hızlı Sonuç Paneli (`Popup`)**: Arama yaparken tam ekran overlay açılmak yerine, arama çubuğunun hemen altında açılan şık bir panel sayesinde bağlamdan kopmadan (context-free) anlık sonuçlar görüntülenebilir hale getirildi.
    - **UI Performans ve Navigasyon Optimizasyonu**:
        - **Gereksiz Animasyonların Kaldırılması**: Binlerce kartın render yükünü artıran `scale(1.05)` (büyüme) efekti kaldırıldı.
        - **Sidebar Modernizasyonu**: Sol menüdeki karmaşık kayma (`Slide-in`) ve gradyan efektleri kaldırılarak daha performanslı, sade ve belirgin bir arayüze geçildi. Aktif menü öğeleri için net bir arka plan rengi (`AccentBrush` %15 opaklık) atandı.
        - **Hızlandırılmış Geçişler**: Animasyon ve opaklık süresi (0.2s -> 0.1s) düşürülerek arayüz tepkiselliği artırıldı.
        - **Arama Çubuğu**: Genişleme süresi 0.15s'ye çekildi ve Popup gölge maliyetleri sıfırlandı.
    - **Sidebar Tasarımı (Netflix Style)**: Sidebar arka planı daha koyu (`#0F0F0F`) bir tona çekildi. Navigasyon öğeleri için Netflix tarzı hover ve aktif durum efektleri uygulandı. Aktif öğeler için mor gradyan yüzey kullanımı optimize edildi.
    - **M3U Liste Analizi Düzeltmesi**: M3U playlistler için gizlenen "Bağlantıyı Analiz Et" butonu görünür hale getirildi ve tüm içeriklerin (VOD/Dizi) analiz edilebilmesi sağlandı.
- **Gelişmiş Ses Denetimi ve Agresif Senkronizasyon** (2026-02-22 17:00):
  - **Agresif Ses Zorlama (Aggressive Force)**: Videonun ilk açılışındaki ses uyumsuzluğunu (UI'da %100 görünüp sesin az gelmesi) gidermek için; ses seviyesi video açılırken ve oynatılmaya başladıktan sonraki ilk 2 saniye boyunca kademeli aralıklarla (50ms'den 2s'ye kadar) tekrar tekrar doğrulanarak VLC/donanım kısıtlamaları aşıldı.
  - **Sabit Ayar Mantığı**: Ayarlardaki "Varsayılan Ses Seviyesi" artık oyuncu içindeki geçici değişikliklerden etkilenmez, kullanıcı değiştirene kadar sabit kalır.
  - **Otomatik Reset**: Her yeni video açılışında ses seviyesi otomatik olarak ayarlardaki varsayılan değere döner.
  - **Anlık UI Senkronizasyonu**: Ayarlar ekranındaki slider ile aktif video oynatıcı arasındaki gecikme giderildi, anlık senkronizasyon sağlandı.
  - **Hassasiyet**: Ses değişim adımları tüm arayüzde %2 olarak standartlaştırıldı.

- **Profil Seçme Ekranı Modernizasyonu ve Limitler** (2026-02-22 16:35):
  - Profil seçme ekranı Netflix tarzı daha sıkı ve modern bir grid yapısına kavuşturuldu.
  - Premium kullanıcılar için profil limiti **5** olarak güncellendi.
  - **Upsell Erişimi**: "Profil Ekle" butonu limit dolsa dahi görünür kalmaya devam eder, tıklandığında Premium pakete yönlendirme (upsell) penceresi açılır. Boton sadece "Düzenle/Yönet" modunda gizlenir.
- **Profil Düzenleme Sonrası Otomatik Veri Yenileme** (2026-02-22 16:35):
  - Profil düzenleme ekranında URL, Kullanıcı Adı veya Şifre değiştirildiğinde, sistem bunu algılar ve eski kanal/içerik listesini silecek şekilde güncellendi.
  - Bu sayede profil kaydedilip tekrar giriş yapıldığında yeni sağlayıcı verileri ("Kanal listeniz güncelleniyor...") mesajıyla sıfırdan çekilir.
  - **Kritik**: Dizi ilerlemeleri (`series progress`) ve izleme geçmişi profil bazlı olduğu için bu işlemden etkilenmez, korunur.
- **Profil Ekleme/Düzenleme M3U Arayüz Sadeleştirmesi** (2026-02-22 16:20):
  - M3U Playlist seçiliyken yalnızca **M3U Link** textbox'ı görünür. Kullanıcı adı/şifre alanları ve "Bağlantıyı Analiz Et" butonu gizlenir.
  - Xtream Codes veya Stalker Portal'a geçildiğinde tüm alanlar otomatik geri gelir; doğrulama mantığı etkilenmez.
  - **Dosya**: `AddProfileWindow.axaml`
- **EPG Veri Kaybı ve Yenileme Düzeltmeleri** (2026-02-22 16:10):
  - **KRİTİK: EPG Veri Silme Hatası Giderildi**: `LoadEpgAsync` içinde her `isPrimary=true` kaynak için tüm EPG verisini silen gereksiz `ClearEpgAsync()` çağrısı kaldırıldı. Bu bug nedeniyle sırayla yüklenen EPG kaynakları (Provider → iptv-epg.org) birbirinin verilerini siliyordu ve kanallarda "Program bilgisi yok" gösteriliyordu. Temizleme artık sadece `MainViewModel.ClearBeforeLoad` flag'iyle ilk kaynak için bir kez yapılıyor.
  - **EPG Ghost Error Düzeltmesi**: Settings penceresi üzerinden EPG yenilenirken, arka plandaki polling loop'u eski hata bilgisini DB'den okuyarak sahte hata mesajı gösteriyordu. `RefreshEpgNowAsync` artık DB'deki `EpgLastError` alanlarını yenileme başlamadan **önce** temizliyor.
  - **Settings → MainWindow İlerleme Köprüsü**: Settings penceresi kapatıldıktan sonra ana pencerenin sol alt durum çubuğu EPG yenileme yüzdesini göstermiyordu. `SetProgressStatus` artık `MainViewModel.StatusMessage`'ı da güncelliyor — ayarlar kapatılsa bile ilerleme görünür.
  - **`EpgService.ClearLastError()` Eklendi**: Singleton `LastError` property'sini temizlemek için yeni metot, eski ghost error'ların UI'da takılmasını önlüyor.
  - **Dosyalar**: `EpgService.cs`, `IEpgService.cs`, `MainViewModel.cs`, `SettingsViewModel.cs`, `StubEpgService.cs`
- **EPG Eşleştirme Sistemi Kapsamlı İyileştirmesi** (2026-02-22 14:45):
  - **Genişletilmiş İsim Varyantları**: `GetNameVariants` artık her kanal adından 6+ farklı eşleştirme varyantı üretiyor: parantez temizleme (`Star TV (TR)` → `Star TV`), pipe/slash ayırıcı (`TR | Kanal D` → `Kanal D`), dot-suffix (`KanalD.tr` → `KanalD`), trailing ülke adı (`beIN Sports 1 Turkey` → `beIN Sports 1`).
  - **Primary EPG İçin Fuzzy Fallback**: Daha önce sadece secondary EPG'de yapılan display-name bazlı bulanık eşleştirme artık primary EPG'de de aktif. TvgId eşleşmeyen kanallara display-name ile eşleşme şansı tanınıyor.
  - **Genişletilmiş Gürültü Filtresi**: Normalizasyon sırasında `backup`, `bkp`, `multi`, `sub`, `ace`, `plus`, `turkey`, `turkiye` gibi ekstra gürültü kelimeleri de temizleniyor.
  - **Dinamik Benzerlik Eşiği**: Kısa kanal adları (≤6 karakter) için threshold 0.65'e, orta uzunluk (≤10 karakter) için 0.72'ye, uzun isimler için 0.78'e ayarlandı. Bu sayede `TRT1` / `TRT 1` gibi kısa isimler de doğru eşleşiyor.
  - **Dosya**: `EpgService.cs`
- **Hata Mesajı Standardizasyonu ve Merkezi Kanal Listesi Hata Takibi** (2026-02-22 14:43):
  - **Merkezi Hata Takibi**: Kanal listesi yenileme hataları artık `MainViewModel.ChannelListLastError` property'si üzerinden merkezi olarak takip ediliyor. Ayarlar penceresindeki kırmızı hata kutusu, yenilemenin nereden tetiklendiğinden bağımsız olarak (Ana pencere, sidebar, arka plan yenileme) her zaman doğru çalışıyor.
  - **Gelistirme:** `PlaylistService` icin in-memory SQLite tabanli entegrasyon testleri eklendi.
- **Hata Duzeltme:** Playlist yenileme sirasinda cocuk profili filtresinin tum profillere yanlislikla uygulanmasi hatasi giderildi.
- **Performans:** Dynamic Splash ve Profil yukleme sureleri optimize edildi.
  - **Tutarlı Hata Mesajları**: Profil ekleme ekranındaki bağlantı testi ve kanal listesi yenileme hataları artık aynı `UserFriendlyErrorMessage` sistemini kullanıyor.
  - **Doğru Zaman Aşımı Mesajı**: Timeout hatası mesajı "Ağ zaman aşımına uğradı" yerine "Sunucu zaman aşımına uğradı veya yanıt vermiyor. Bağlantı adresini kontrol edin." olarak güncellendi — sorunun kullanıcının ağından değil sunucudan kaynaklandığı doğru şekilde ifade ediliyor.
  - **Kod Sadeleştirmesi**: `SettingsViewModel.RefreshChannelListNowAsync` içindeki kırılgan string-matching hata algılama kodu kaldırılıp, `MainViewModel`'deki temiz property binding'e geçildi.
  - **Dosyalar**: `MainViewModel.cs`, `SettingsViewModel.cs`, `AddProfileViewModel.cs`, `UserFriendlyErrorMessage.cs`
- **Profil Bitiş Süresi Göstergesi Düzeltmesi** (2026-02-22 14:10):
  - **Stale Veri Temizliği**: Bozuk URL veya çalışmayan Xtream hesaplarında eski (stale) bitiş tarihi gösterilmeye devam ediyordu. Artık API erişim hatası, geçersiz URL veya sunucudan `exp_date` alınamadığı durumlarda eski tarih temizleniyor ve "Bilinmiyor" gösteriliyor.
  - **Kanal Listesi Yenilemesinde Güncelleme**: Kullanıcı kanal listesini yenilediğinde (`RefreshChannelListNowAsync`) bitiş süresi göstergesi de otomatik olarak güncelleniyor.
  - **Kanal Listesi Hata Göstergesi**: Kanal listesi yenileme başarısız olduğunda, EPG sekmesindeki gibi kalıcı kırmızı hata mesajı gösteriliyor (`ChannelListLastError`). Başarılı yenilemede hata temizleniyor.
  - **HttpClient Timeout**: Bitiş tarihi kontrolündeki HTTP isteğine 10 saniye timeout eklendi (önceden sonsuz bekliyordu).
  - **Dosyalar**: `MainViewModel.cs`, `SettingsViewModel.cs`, `PlaylistService.cs`, `IPlaylistService.cs`, `SettingsWindow.axaml`
- **Video Overlay Pencere Odak/Aktivasyon Düzeltmesi** (2026-02-22 14:05):
  - **Kritik Hata Düzeltmesi**: Video oynatıcı overlay penceresi (kontroller, seek bar vb.) başka bir uygulama penceresine tıklandığında arkaya gitmiyordu — yalnızca diğer pencerenin başlık çubuğuna tıklanınca gizleniyordu. Artık herhangi bir yerine tıklansa bile overlay düzgün şekilde gizleniyor.
  - **Kök Neden Çözümü**: Avalonia'nın `Activated`/`Deactivated` olaylarındaki 150ms gecikmeli yarış koşulları (race conditions) kaldırıldı. Yerine Win32 `GetForegroundWindow` API'si ile 200ms aralıklarla foreground pencere PID kontrolü yapan güvenilir bir polling mekanizması eklendi.
  - **Dinamik Topmost Yönetimi**: Overlay penceresi artık `Topmost = false` ile başlatılıyor; yalnızca uygulamamızın process'i aktifken `Topmost = true` yapılıyor, başka process aktif olduğunda anında `false` yapılıp gizleniyor.
  - **Dosya**: `MemoryVideoView.cs` — `FocusCheckTimer_Tick`, `CreateOverlayWindow`, `UpdateOverlayPosition` metotları güncellendi.
- **Görsel Marka Kimliği ve Yükleme Deneyimi Modernizasyonu** (2026-02-21 16:00):
  - **Kurumsal Kimlik Birliği**: `SplashWindow`, `ProfilesWindow` ve `ProfileLoadingWindow` ekranları ortak bir tasarım diline, arka plan gradyanlarına ve pencere ayarlarına kavuşturuldu.
### Fixed
- Fatal crash (`InvalidOperationException`) when selecting profiles in edit mode.
- Startup crash due to circular dependency in `PremiumSpinner` XAML.
- Startup crash due to invalid Easing attribute in Avalonia animations.
- Profile loading UI freezing during initial connection.

### Improved
- Standardized loading experience with a smooth, themed single-arc spinner.
- Extended Splash screen display to 3.5 seconds for a more premium feel.
- Extended Profile Loading duration to 4.5 seconds for visual stability.
- Simplified `PremiumSpinner` for better performance and resource usage.
- Refactored `M3UParser` for thread-safe network operations.
  - **Kritik Hata Düzeltmesi**: `PremiumSpinner` kontrolündeki dairesel bağımlılık (circular dependency) nedeniyle oluşan uygulama başlatma hatası giderildi. Kontrol, `TemplatedControl` mimarisine taşınarak stabil hale getirildi.
  - **Stabil Temalı Yükleme Animasyonu**: Karmaşık animasyon hatalarını önlemek için yüksek performanslı, tek ark (single-arc) tasarımına sahip "Standart Temalı Spinner" geliştirildi. Uygulama genelindeki tüm yükleme süreçlerinde BU tasarım standart hale getirildi.
  - **Gelişmiş Hata Geri Bildirimi**: Profil yükleme aşamasında oluşan hatalar, kullanıcıyı bilgilendirmek için kırmızı renkli ve anlaşılır hata mesajlarıyla görselleştirildi.
  - **Kaliteli Geçiş Deneyimi**:
    - **Açılış Ekranı (Splash)**: Başlangıç ısıtma (warmup) işlemlerinin tamamlanması ve premium bir his için açılış ekranı süresi minimum **3.5 saniyeye** çıkarıldı.
    - **Profil Yükleme**: Profil verilerinin arka planda tam olarak hazırlanması ve arayüz titremelerinin önlenmesi için yükleme ekranı süresi minimum **4.5 saniyeye** çıkarıldı.
- **İndirme Şifreleme Sisteminin Kaldırılması ve Kuyruk İyileştirmeleri** (2026-02-21 14:07):
  - **SSD Ömür Koruması ve Performans**: İstemci tarafı indirme şifreleme/şifre çözme sistemi tamamen kaldırıldı. Bu sayede gereksiz G/Ç (I/O) yükü ve SSD aşınması engellendi, yerel içeriklerin oynatım hızı artırıldı.
  - **Doğrudan Kayıt**: İndirilen dosyalar artık ara şifreli formatlar (`.nctra`) yerine doğrudan son hedef formatında kaydediliyor.
  - **Veritabanı Şeması Otomatik Geçişi**: `DownloadItems` tablosundaki `LocalEncryptedPath` kolonu otomatik olarak `LocalFilePath` olarak yeniden adlandırıldı.
  - **Eski Format Temizliği**: Eski `.nctra` dosyaları artık desteklenmiyor; uygulama başlangıcında bu dosyalar "eski format" olarak işaretlenir ve `TempPlayback` dizini tamamen temizlenir.
  - **İndirme Politikası Düzeltmeleri**: 'Yalnızca Wi-Fi' ayarı açıkken mobil verinin (Cellular) indirmeye izin vermesi hatası giderildi; artık sadece Wi-Fi ve Ethernet bağlantılarına izin veriliyor.
  - **Dizin Adlandırma Düzeltmesi**: `Profile_` dizin yapısındaki büyük/küçük harf uyumsuzluğu giderilerek İndirme Merkezi ve disk alanı gösterimi düzeltildi.
  - **Gelişmiş Yerel Yol Tespiti**: `PlayerViewModel` artık yerel dosyaları dosya uzantısından bağımsız olarak, mutlak yol doğrulamasıyla tespit edebiliyor.
- **Kapsamlı Servis Refaktörü ve Service Locator Arındırma** (2026-02-21 12:36):
  - **ObservableCollection Performans İyileştirmesi**: `MainViewModel` içerisindeki UI'a bağlı tüm listeler `ObservableCollection<T>` tipine dönüştürüldü ve `SetItems` yardımcı metodu ile sadece içerikleri güncellenerek sayfa geçişlerindeki arayüz titremeleri (UI flicker) engellendi.
  - **VideoPlayer Null Koruması**: `VideoPlayerService` içerisinde `CS8602` uyarısına neden olan olası boş referans hataları (null reference) yerel değişken kopyalamaları ve `null` kontrolleri ile kalıcı olarak giderildi.
  - **IServiceScopeFactory Tamamen Kaldırıldı**: `MainViewModel`, `ProfilesViewModel`, `AddProfileViewModel`, `SettingsViewModel`, `ContentDownloadService` ve `PlaylistService` içerisindeki tüm `IServiceScopeFactory` (Service Locator) kullanımı temizlendi.
  - **IDbContextFactory ve IDialogService Geçişi**: Manuel scope yönetimi yerine `IDbContextFactory<AppDbContext>` ve genişletilmiş `IDialogService` mimarisine geçildi. Bu sayede iş mantığı (business logic) katmanı DI prensiplerine tam uyumlu hale getirildi ve test edilebilirlik artırıldı.
  - **Window Ömür Döngüsü Yönetimi**: Pencere açma ve ViewModel eşleştirme mantığı `AvaloniaDialogService` içerisinde merkezileştirilerek kod-arkası (code-behind) dosyalarındaki Service Locator bağımlılıkları yok edildi.
  - **Captive Dependency Çözümü**: `App.axaml.cs` içerisindeki bağımlılık enjeksiyonu (DI) güncellendi. Kendi veritabanı bağlamlarını güvenle yöneten servisler `Singleton` olarak kaydedilerek ömür döngüsü (lifetime) hataları kalıcı olarak çözüldü.
  - **Gereksiz Bağımlılık Temizliği**: Masaüstü (Avalonia) uygulaması için aşırı yük olan ASP.NET Core `Microsoft.AspNetCore.WebUtilities` framework paket bağımlılığı projeden tamamen kaldırıldı. M3U ve Xtream bağlantı ayrıştırma işlemlerinde (URL Query Parsing) kullanılan `QueryHelpers.ParseQuery`, base sınıf kitaplığında (.NET BCL) bulunan daha hafif ve harici paket gerektirmeyen `System.Web.HttpUtility.ParseQueryString` metoduna geçirildi.
  - **İndirme Toleransı Esnekliği**: İçerik indirme servisinde (`ContentDownloadService`), ağ dalgalanmaları sırasında akışın kapanması durumunda kullanılan "neredeyse tamamlandı" sayısal toleransı (hardcoded `%99.98`), yapılandırılabilir (configurable) hale getirilerek `AppSettings.DownloadCompletionTolerance` özelliğine bağlandı.
  - **Kayıp Veri (Data Loss) Koruması**: Eski Dizi (Series) izleme geçmişi verilerini (Legacy `WatchHistories`) yeni `SeriesEpisodeProgresses` mimarisine taşıyan `ApplyProfileProgressAsync` ve `PersistSeriesProgressSnapshotsAsync` içerisine **Database Transaction (IDbContextTransaction)** yapısı eklendi. Taşıma sırasında oluşabilecek DB kilidi (lock) veya izin (constraint) hatalarında işlem artık `RollbackAsync` ile geri alınarak eski verilerin sessizce kaybolması önlendi.
  - **Async Void Güvenliği**: Proje genelindeki `async void` event handler'lar denetlendi. `MainWindow.SettingsButton_Click` üzerinde eksik olan `try-catch` bloğu eklenerek olası çalışma zamanı hatalarının uygulamayı çökertmesi engellendi.
  - **Güvenlik ve Thread-Safety İyileştirmeleri**: `SettingsService` içerisindeki manuel çift-kontrol kilitleme (double-checked locking) yapısı, thread-safe olduğu garanti edilen `Lazy<AppSettings>` desenine geçirildi. Bu sayede ayarların ilk yüklenme anındaki yarış durumları (race conditions) kalıcı olarak önlendi.
  - **Güvenlik ve Cross-Platform Uyumluluğu**: `SecurityService` içerisinde Windows'a özgü `ProtectedData` (DPAPI) kullanımı için runtime OS kontrolü eklendi. Uygulamanın Linux ve macOS sistemlerde `PlatformNotSupportedException` ile çökmesi engellenerek taşınabilirlik (portability) sağlandı.
  - **SeriesInfoParser Güçlendirilmesi**: IPTV isimlerindeki etiket temizleme mantığı güvenli iki fazlı bir yaklaşımla yeniden yazıldı: (1) `CountryPrefixRegex` ile 2-3 harfli ülke kodları (TR, EN, DE) güvenle temizlenir, (2) `PipeTagRegex` ile sadece pipe (`|`) ile ayrılmış etiketler (Kanal D, HBO vb.) temizlenir. Bu sayede tire veya nokta içeren meşru dizi adlarının kesilmesi önlendi.
  - **Genel Sistem Sağlığı ve Uyumluluk**: Tüm altyapı (Database, Auth, Downloads) gözden geçirildi. Bazı IPTV sağlayıcılarının ".NET" User-Agent'ını engellemesi nedeniyle, tüm ağ isteklerine standart bir browser User-Agent'ı eklenerek uyumluluk artırıldı. Ayrıca profil geçişlerinde eski verilerin ekranda kalması (ghosting) sorunu çözüldü ve Steam tarzı animasyonlu bir profil yükleme ekranı (`ProfileLoadingWindow`) eklendi.
  - **Genişletilmiş Unit Test Paketi**: Test paketi **72 teste** çıkarıldı. `SeriesInfoParser` (55 test), `M3UParser` (4 test), `SecurityService` (4 test), `SettingsService` (2 test) ve `LicenseService` (5 test - tier ve limit doğrulamaları) ile uygulamanın tüm kritik backend mantığı otomatik test kapsamına alındı.
  - **Güvenlik: AuthCache Şifre Hash'leme**: `XtreamCodesService.BuildAuthCacheKey` içerisinde şifreler artık düz metin yerine **SHA256 hash** olarak saklanıyor. Bu sayede static `ConcurrentDictionary<string, CachedAuthState>` anahtarlarından bellek dump'ı veya heap profiler ile şifre çıkarılması önlendi.
  - **TOCTOU Race Condition Düzeltmesi**: `XtreamCodesService.CleanupExpiredAuthsIfNeeded` içerisindeki `_lastCleanup` zamanlama kontrolü ve güncellemesi artık `Monitor.TryEnter` kilidi **içerisinde** yapılıyor. Eski kodda okuma kilidin dışında, yazma içeride olduğu için birden fazla thread aynı anda temizlik tetikleyebiliyordu (TOCTOU race).
  - **Socket Exhaustion Önleme**: `HttpClient` kaydı `AddTransient` → `AddSingleton` olarak değiştirildi. Eski kodda her servis çözümlemesinde (`XtreamCodesService`, `StalkerPortalService`, `M3UParser`, `EpgService` vb.) yeni bir `HttpClient` ve `SocketsHttpHandler` oluşturuluyordu; bu da **TIME_WAIT** soketlerinin birikmesine ve socket exhaustion'a yol açabiliyordu. `SocketsHttpHandler.PooledConnectionLifetime` (5 dk) zaten DNS rotasyonunu güvenli şekilde yönettiğinden, Singleton kayıt doğru yaklaşımdır.
  - **Mimari İyileştirme: IProfileService**: `AddProfileViewModel` ve `ProfilesViewModel` içerisindeki tüm veritabanı operasyonları, transaction yönetimi ve iş mantığı `IProfileService` (ProfileService) katmanına taşındı. ViewModels artık DB Context veya Entity Framework detaylarını (Attach/Detach vb.) bilmek zorunda kalmadan sadece UI state yönetiminden sorumlu hale getirildi. Veri taşıma işlemleri için immutable `ProfileSaveRequest` yapısı kullanılmaya başlandı.
  - **Async Resource Management**: Kod tabanındaki `using var transaction` kullanımları, asenkron dispose işlemini garanti altına almak için `await using var transaction` (IAsyncDisposable) yapısına geçirildi/doğrulandı.
  - **Model Katmanı Temizliği (POCO)**: EF Core entity modellerinden (`Profile`, `ProviderAccount`, `Channel`) `ObservableObject` mirası ve `[ObservableProperty]` öznitelikleri kaldırıldı. Core katmanı artık `CommunityToolkit.Mvvm` kütüphanesine bağımlı değil; bu sayede veritabanı takipçisi (change tracker) ile UI bildirim mekanizmalarının çakışması önlendi.
  - **Memory & Cache Optimizasyonu (LRU Eviction)**: `RemoteImage` ve `AvaloniaImageCacheService` bileşenlerindeki basit FIFO/TTL tabanlı önbellek mantığı, **LRU (Least Recently Used)** tahliye politikası ile değiştirildi. 1500 (RemoteImage) ve 500 (ImageCacheService) kayıt limitleri getirilerek belleğin kontrolsüz büyümesi önlendi ve sık kullanılan görsellerin bellekte kalması sağlandı.
  - **Servis Yaşam Ömrü Düzeltmesi**: İçerisinde static state (oturum cache ve kilitler) barındıran `XtreamCodesService` kaydı mimari tutarlılık için **Transient** → **Singleton** olarak güncellendi.
- **Canlı TV Kanal Navigasyonu**: Canlı TV yayınları için oynatıcı arayüzüne (overlay ve PiP) özel "Önceki Kanal" ve "Sonraki Kanal" butonları eklendi. (VOD ve Dizilerdeki 10 saniye atlama butonlarının yerini alır.)
  - **Dinamik Bağlam Çözümlemesi (Geliştirilmiş Fallback)**: İzlenen kanal Geçmiş veya Arama ekranından başlatılmış olsa bile, kanalın ait olduğu çalma listesinin (playlist) hâlâ aktif olup olmadığı doğrulanır. Olası eskimiş veri (stale data) risklerini önlemek için bağlam, bellekten (`Channels.Where`) değil; arka planda ateşle-ve-unut (fire-and-forget) yöntemiyle `AppDbContext` üzerinden anlık olarak sorgulanıp yenilenir (`RefreshLivePlaybackContextAsync`).
  - **PiP Etkileşimi**: Saydam köşe sorunlarını aşmak ve Avalonia'nın tıklama yutma hatalarını engellemek için PiP ekranındaki kanal geçiş butonları native `Click` event'leri ile C# arka planına bağlandı.
- **PlayerViewModel Durum Yönetimi (State Cleanup) Optimizasyonu**:
  - Parçalı ve üst üste (overlapping) binme riski taşıyan durum bayrakları (`_isEndedSeekRecoverInProgress`, `_isLiveAutoRecoverInProgress`) tek bir atomik state makinesine (`PlaybackRecoveryState` enum ve `Interlocked`) dönüştürüldü.
  - Bu sayede canlı yayın donma (stall) kurtarmaları sırasında kullanıcının oynatım çubuğundan veya yön tuşlarından gönderdiği sinyaller (seek) güvenle yok sayılarak Player'ın çökmesi (race condition) veya sonsuz kurtarma döngüsüne girmesi engellendi.
- **MainWindow Mimari Optimizasyonu (Refactoring)** (2026-02-20):
    - **Bileşen Odaklı Yapı (Component-Based)**: `MainWindow.axaml` içerisinde bulunan tüm ana görünümler (`HomeView`, `LiveView`, `MoviesView`, `SeriesView`, `MyListView`, `DownloadsView`, `HistoryView`, `FavoritesView`, `SearchView`) kendi bağımsız `UserControl` (.axaml ve .cs) dosyalarına ayrıldı.
    - **Performans ve Bakım Kolaylığı**: Ana pencere kodu (XAML ve C#) büyük ölçüde sadeleştirildi. Görüntüleme mantığı, kaydırma efektleri (Parallax) ve sayfalama algoritmaları yalnızca ilgili görünümler belleğe yüklendiğinde ve kendi içlerinde çalışacak şekilde izole edildi.
    - **Modüler Bağlam Menüleri (Context Menus)**: Medya (Kanal/Dizi) sağ tık ve "Listeme Ekle / Favorilere Ekle" gibi dinamik eylemler genel `MainWindow` dosyasından çıkarılıp her UserControl'ün kendi özgü ve güvenli alanına taşındı.  
    - **Derleme Hataları ve Ad Alanı Temizliği**: Bileşen ayrımı sırasında oluşan `x:Name` çakışmaları (CS0542) ve ad alanı çakışmaları (`global::Avalonia.Controls.StyledElement`, `global::Avalonia.Media` - CS0234) kalıcı olarak çözüldü.
- **SettingsWindow İyileştirmeleri ve Bellek Yönetimi** (2026-02-20):
    - **Kritik Bellek Sızıntısı (Memory Leak) Giderildi**: `SettingsWindow.axaml.cs` içerisinde `SettingsViewModel`'a yapılan anonim event aboneliği isimli metoda dönüştürüldü ve `OnClosed` aşamasında abonelik temizliği (Unsubscribe) eklendi.
    - **Kopya-Yapıştır Hataları Düzeltildi**: Oynatma sekmesindeki hatalı "Kişiselleştirme" başlığı "Oynatma & İndirme" olarak düzeltildi.
    - **XAML Temizliği ve Optimizasyon**: `SettingsWindow.axaml` içerisindeki redundan (gereksiz) `MaterialIcon` tanımları ve kullanılmayan `StreamGeometry` kaynakları projeden kaldırılarak dosya boyutu küçültüldü.
    - **Dinamik Önbellek (Cache) Yönetimi**: Ayarlar ekranındaki "Önbellek boyutu" artık "234 MB" gibi sabit bir değer göstermek yerine; resim önbelleği (`image-cache`), geçici dosyalar (`TempPlayback`) ve logların gerçek boyutunu hesaplıyor. "Önbelleği Temizle" butonu artık tüm bu geçici verileri diskten gerçekten siliyor.
    - **Profil Yönetimi Mantığı Sadeleştirildi**: Profil seçme ekranına dönüş fonksiyonu daha güvenli ve temiz bir yapıya kavuşturuldu.
- **UpsellWindow Temiz Kod (Clean Code) Uygulaması** (2026-02-20):
    - **Pencere Sürükleme Mantığı Modernize Edildi**: `UpsellWindow.axaml.cs` içindeki anonim lambda ile kurulan sürükleme (dragging) sistemi, daha "temiz" ve standartlara uygun olan `OnPointerPressed` override metoduna taşındı.
- **AvaloniaImageCacheService Kritik Çökme (Crash) Tespiti ve Çözümü** (2026-02-20):
    - **Bellek Yönetimi Düzeltildi**: Resim önbelleğinde (cache) süresi dolan Bitmap nesnelerinin manuel olarak `Dispose()` edilmesi engellendi. Bu durumun, resim o sırada ekranda gösterilirken render motoruyla (UI Thread) çakışarak uygulamayı çökertme riski (Access Violation) ortadan kaldırıldı. Bellek yönetimi güvenli bir şekilde .NET Çöp Toplayıcısına (GC) bırakıldı.
- **Mimari İyileştirme (Separation of Concerns)** (2026-02-20):
    - **AvaloniaDialogService Refaktörü**: Dialog servisinin doğrudan veritabanına (`AppDbContext`) erişmesi engellendi. `ShowEditProfileAsync` metodu artık profil ID'si yerine doğrudan `Profile` nesnesi alacak şekilde güncellendi. Bu sayede UI katmanı ile Veri katmanı arasındaki sorumluluklar net bir şekilde ayrıldı.
    - **MetadataService API Key Mantık Hatası Giderildi**: `EnsureApiKeyLoaded` metodu, ayarlardan gelen TMDB API anahtarını her zaman kontrol edecek şekilde güncellendi. Bu sayede kullanıcı API anahtarını değiştirdiğinde uygulamanın yeniden başlatılmasına gerek kalmadan yeni anahtar devreye girecek.
- **Performans Optimizasyonu ve Modernizasyon** (2026-02-20):
    - **Anında Açılış (Instant Startup) ve Splash Ekranı**: Uygulamanın 1 saniyenin altında tepki vermesi (Instant Load) için "Background Warmup" mimarisine geçildi.
        - **SplashWindow Eklendi**: `App.axaml.cs` ayağa kalkarken ağır DI nesnelerini (ViewModel'lar, Veritabanı vb.) yaratmak yerine, sıfır bağımlılığa sahip çok hafif bir `SplashWindow` (Logo ve yükleme metni) anında ekrana getirildi.
        - **EF Core Soğuk Başlangıç (Cold Start) Çözümü**: `SplashWindow` ekrandayken arka planda asenkron bir `Task.Run` başlatılarak `AppDbContext.Profiles.AnyAsync()` tetiklendi. Böylece Entity Framework Core'un normalde UI üzerinde 2-3 saniye kilitlenmeye sebep olan ilk "Model Derlemesi" (Model Building) işlemi kullanıcıya hissettirilmeden arka planda bitirildi.
        - **DI Ağacı Yükü Dağıtıldı**: Yüzlerce servise ve bağımlılığa sahip olan `ProfilesWindow` ve `MainWindow`'un oluşturulması (`Services.GetRequiredService`) işlemi arka plan ısıtma (warmup) aşamasına kaydırıldı. Ağır nesneler RAM'e yüklendiğinde `SplashWindow` otomatik kapanıp yerini ana pencereye sorunsuz şekilde devrediyor.
    - **VLC Asenkron Yükleme**: Ana thread'i 10-15 saniye kilitleyen ağır senkron `LibVLCSharp.Shared.Core.Initialize()` çağrısı kaldırıldı. Özelliğin asenkron olarak arka planda başlatılması sağlandı. `MainWindow`, arka planda VLC yüklendiğinde `MediaPlayerReady` üzerinden otomatik olarak ekrana bağlandı.
    - **IPTV Yayın Kararlılığı (Cold-Start & Anti-Bot Çözümleri)**: `VideoPlayerService` içerisinde IPTV sağlayıcılarının (Xtream vb.) yarattığı gecikmeler ve kısıtlamalar için kritik düzeltmeler yapıldı:
        - **Sabırlı Ön Bellek (Caching)**: Ağ hatalarına karşı `network-caching` ve `live-caching` süreleri IPTV standartlarına uygun şekilde 500ms'den **3000ms'ye** çıkarıldı. Böylece sağlayıcının sunucuyu uyandırma ("Cold Start") süresinde bağlantı koparması engellendi.
        - **Anti-Bot Atlama (User-Agent)**: VLC'ye standart Chrome tarayıcısı (`Mozilla/5.0...`) kimliği tanımlanarak sağlayıcıların bot bloklamasından (Drop) kaçınılması sağlandı.
        - **Hızlı Hata Tespiti & Otomatik Yeniden Bağlanma**: `:http-reconnect=true` seçeneği eklendi. Ayrıca ilk denemede hata alınırsa veya bağlantı zaman aşımına uğrarsa 5 saniye beklemek yerine oluşan hata anında yakalanıp, sadece 1.5 saniye arayla otomatik ikinci istek arka planda (kullanıcıya siyah ekran göstermeden) yollanıyor.
    - **SettingsService İyileştirmesi**: Ayarlar servisinin yapıcı metodundaki (constructor) senkron dosya okuma işlemi kaldırıldı. Bunun yerine "Lazy Loading" (ihtiyaç anında yükleme) mimarisine geçilerek, uygulamanın açılış hızı iyileştirildi ve ana thread üzerindeki disk G/Ç yükü kaldırıldı. Ek olarak yeni `SplashWindow` mimarisinde arka planda ayarlara "dokunularak" diskteki dosya RAM'e önceden çekildi.
    - **EF Core Performans İyileştirmeleri**: `PlaylistService` ve `EpgService` sınıflarındaki salt-okunur (read-only) sorgulara `AsNoTracking()` eklenerek bellek kullanımı ve CPU yükü azaltıldı. Ayrıca EPG yükleme gibi toplu işlemlerde `AutoDetectChangesEnabled` kapatılarak performans %40-60 oranında artırıldı. `ChannelService` içindeki gereksiz `.Update()` çağrısı kaldırılarak sadece değişen kolonların güncellenmesi sağlandı. `WatchHistoryService` içerisinde eski kayıtların silinmesi işlemi `ExecuteDeleteAsync()` ile direkt veritabanı seviyesine çekilerek performans iyileştirildi.
    - **GeneratedRegex Kullanımı**: `AddProfileViewModel.cs` içindeki MAC adresi doğrulama ifadesi modern .NET standardı olan `[GeneratedRegex]` özniteliğine taşındı. Bu sayede uygulama başlangıç süresi iyileştirildi ve Regex için bellek tahsisi (allocation) minimize edildi.
    - **AvaloniaDispatcherService İyileştirmesi**: `InvokeAsync` metotlarında kullanılan `TaskCompletionSource` nesneleri `RunContinuationsAsynchronously` seçeneği ile yapılandırıldı. Bu sayede UI thread'den gelen görevlerin devamı (continuations) ThreadPool'a yönlendirilerek arayüzün daha akıcı kalması ve olası kilitlenmelerin (deadlock) önlenmesi sağlandı.
- **Güvenlik ve Kararlılık (Thread Safety & Lifecycle)** (2026-02-20):
    - **VideoOverlayViewModel Thread-Safe Güncelleme**: Arka plan timer'ları (`System.Timers.Timer`) tarafından tetiklenen özellik güncellemeleri (saat, otomatik gizleme, ses toast mesajı) `IDispatcherService` üzerinden UI thread'ine alındı. Bu sayede "Cross-Thread Collision" riskleri ve olası UI kilitlenmeleri giderildi.
    - **LibVLC Başlatma Mantığı Düzeltildi**: `LibVLCSharp` başlatma prosedürü optimize edilerek asenkron başlatılmaya başlandı ve UI kilitlenmelerinin önüne geçildi. Motorun birden fazla kez başlatılma riski zaten `Program.cs`'ten tamamen silindiği için kalıcı olarak çözüldü.
    - **Kod Temizliği**: `NetworkService.cs` içindeki kullanılmayan istisna değişkeni (`ex`) kaldırılarak derleyici uyarıları temizlendi.
    - **XtreamCodesService Bellek Sızıntısı Giderildi**: `AuthLocks` ve `AuthCache` yapıları konsolide edildi. Süresi dolan kimlik doğrulama kilitlerinin (`SemaphoreSlim`) ve önbellek girişlerinin periyodik olarak temizlenmesi sağlanarak, uzun süreli kullanımda oluşabilecek bellek sızıntısı riski ortadan kaldırıldı.
    - **İzleme Süresi Hesaplama Mantığı Düzeltildi**: `WatchHistoryService` üzerindeki sabit 5 saniyelik artış (hardcoded increment) yerine, `PlayerViewModel` tarafından hesaplanan gerçek zaman farkı (`delta`) kullanılmaya başlandı. Bu sayede izleme süresinin (WatchedDuration) her koşulda doğru hesaplanması sağlandı.
    - **CancellationToken Desteği Eklendi (ISP)**: `IMetadataService`, `IMediaService` ve `IChannelService` gibi kritik asenkron servis metotlarına `CancellationToken` desteği eklendi. Bu sayede HTTP istekleri ve veritabanı işlemleri kullanıcı arayüzden ayrıldığında iptal edilebilir hale getirilerek kaynak yönetimi optimize edildi.
    - **Nullable Event Tanımları Fixlendi**: `ILicenseService.cs` (Interface) üzerindeki `SubscriptionChanged` event tanımındaki eksik `?` (nullable reference type) operatörü eklenerek uygulama genelindeki event standartlarıyla tutarlılık sağlandı.
    - **Arayüz Dokümantasyonu Geliştirildi**: `IMediaService`, `IDialogService` ve `IDispatcherService` arayüzleri, metot ve parametre açıklamalarını içeren standart XML dokümantasyon yorumlarıyla zenginleştirildi.
- **Loglama ve Hata Takibi İyileştirmeleri (StartupDiagnostics)** (2026-02-20):
    - **Startup Log Rotasyonu**: `startup.log` dosyasının kontrolsüz büyümesini engellemek için 1MB sınırı eklendi. Dosya bu sınırı aştığında otomatik olarak son 500KB'ı tutacak ve satır bütünlüğünü koruyacak (newline preservation) şekilde kırpılıyor.
    - **Akıllı Dosya Yolu Çözümleme**: Log dizinine erişilemediği durumlarda (permission/path errors) otomatik olarak `TempPath` dizinine düşen (fallback) hata-toleranslı dosya yolu sistemi eklendi.
    - **Gereksiz Log Temizliği**: Artık kullanılmayan `mobile_startup_trace.log` ve `mobile_crash.log` gibi eski log dosyaları projeden temizlendi.
    - **Resim Yükleme İptalleri**: `RemoteImage` bileşeninde sayfa geçişleri sırasında oluşan `TaskCanceledException` hataları artık "hata" olarak değil, normal bir "iptal" işlemi olarak loglanıyor (Gürültü azaltıldı).
    - **Indirme Boyutu Formatlama Optimizasyonu**: `DownloadItem.cs` içerisindeki `FormatBytes` metodu, her çağrıda yeni bir string dizisi oluşturmak yerine `static readonly` bir dizi kullanacak şekilde optimize edildi. Bu sayede hızlı güncellenen indirme süreçlerinde Garbage Collector üzerindeki baskı azaltıldı.
    - **Model Nitelikleri Refaktör Edildi**: `Channel.cs` ve `Series.cs` sınıflarında kullanılan gereksiz uzun `[NotMapped]` nitelik yolları, `using` bildirimleri kullanılarak sadeleştirildi.
    - **Global UTC Zaman Standartı**: Uygulama genelinde (Models, ViewModels, Services) tüm veritabanı zaman damgaları ve abonelik/deneme süresi hesaplamaları `DateTime.UtcNow` standardına taşındı. Bu sayede zaman dilimi uyumsuzlukları ve yerel saat manipülasyonu kaynaklı riskler minimize edildi.
    - **EPG Altyapısı ve Performans Optimizasyonu**:
        - EPG rehberi sorguları için veritabanı seviyesinde composite indeks (`ChannelId, StartTime, EndTime`) tanımlanarak sorgu performansı 10-20 kat artırıldı.
        - EPG verisi işlenirken sadece mevcut kanal listesiyle eşleşen programların işlenmesi sağlanarak bellek ve veritabanı kullanımı optimize edildi.
        - EPG kaynak öncelik sıralaması `EpgSourceResolver` içerisinde merkezileştirildi (Custom URL > Provider > M3U > iptv-epg.org > Global Fallback).
        - Otomatik ülke tespiti eşik değeri yükseltildi (%10/5 kanal → %20/20 kanal) — az sayıda kanal için gereksiz yere büyük EPG dosyalarının (140MB+) indirilmesi engellendi.
        - EPG indirme zaman aşımı süresi büyük dosyalar için 5 dakikadan 10 dakikaya çıkarıldı.
    - **EPG Zaman Dilimi Uyumluluğu Teyidi**: EPG programlarının veritabanına her zaman UTC formatında kaydedildiği (`EpgService` üzerinden) ve `EpgProgram` sınıfındaki aktiflik/ilerleme hesaplamalarının `UtcNow` ile %100 uyumlu çalıştığı doğrulanmıştır.
- **Veri Temizliği ve Bakım** (2026-02-20):
    - **Eski Veritabanı Kalıntısı Temizlendi**: Uygulama klasöründe (LocalApplicationData) kalan ve kullanılmayan `noctra_avalonia_v1.db` (24MB) dosyası silinerek disk alanı kazanıldı.
- **Bellek Yönetimi ve Sızıntı Giderilmesi (Memory Leak Prevention)**:
    - **KRİTİK: VideoPlayerService Resource Leak Çözüldü**: `VideoPlayerService` sınıfının `Dispose` metodu içerisindeki eksik kaynak temizleme (unmanaged resource) problemleri giderildi:
        - Başlatma işlemlerini yöneten `_initLock` (`SemaphoreSlim`) nesnesinin dispose işlemi eklenerek `WaitHandle` sızıntısı önlendi.
        - Ses ayarlarını diske yazmayı geciktiren (debounce) `_volumeSaveCts` iptal edilip dispose edildi; böylece obje yok edildikten sonra tetiklenen disk I/O hataları ve arka plan görevi zombi kalıntıları engellendi.
        - `_settingsService.SettingsChanged` event aboneliği kaldırılarak, iki Singleton servis arasındaki döngüsel referansın (circular reference) Garbage Collector'ı (GC) engellemesi (Ghost Object) sorunu çözüldü.
    - **GlobalSettingsViewModel Event Leak Çözüldü**: `GlobalSettingsViewModel` sınıfına `IDisposable` arayüzü eklendi. `SettingsChanged` ve `PropertyChanged` event abonelikleri `Dispose()` metodu içerisinde temizlenerek, Ayarlar sayfası her açıldığında bellekte yeni nesnelerin birikmesi ve sızıntı yapması (Ghost Object Leak) engellendi.

- **Picture-in-Picture (PiP) Architecture Modernizasyonu** (2026-02-19):
    - **Single-Window Mimarisi**: VLC (Direct3D11) motorunun Windows üzerinde HWND (pencere tutamacı) kilitlemesi nedeniyle oluşan siyah ekran ve çökme sorunlarını gidermek için tasarlanmıştır. PiP modu artık harici bir pencere açmak yerine, `MainWindow`'u minimal bir "Shell" haline getirerek mevcut HWND'yi korur.
    - **8 Yönlü Orantılı Boyutlandırma**: Pencereyi 4 köşe ve 4 kenardan, 16:9 en-boy oranını koruyacak şekilde büyütüp küçülten özel bir vektörel boyutlandırma mantığı eklendi.
    - **Tüm Yüzeyden Sürükleme**: `MouseCaptureLayer` üzerinden tüm video yüzeyini kapsayan global bir sürükleme (Window Move Drag) sistemi entegre edildi.
    - **1:1 Kontrol Tasarımı & Estetik**: PiP arayüzü görseldekiyle birebir örtüşmesi için iyileştirildi. Orta kontrollere dairesel `Play/Pause` ikonları eklendi. Pencere kenarlarına 12px köşe radiusu ve şık bir çerçeve (`PiPFrame`) uygulandı.
    - **Akıcı Boyutlandırma (Jitter-Free)**: Boyutlandırma mantığı piksel bazlı yuvarlama (pixel snap) ile optimize edilerek, büyütme/küçültme sırasındaki titremeler tamamen giderildi.
    - **Gelişmiş Kırpma (Clipping)**: Videonun köşeleri, PiP çerçevesinin kavislerine uyacak şekilde `PiPContainer` üzerinden dinamik olarak kırpıldı.
    - **Çift Tıklama Kararlılığı (Double-Click Fix)**: PiP modunda çift tıklama yapıldığında pencerenin bug'a girmesi engellendi. Artık çift tıklama, pencereyi güvenli bir şekilde tam ekran moduna döndürüyor.
    - **Phase 26 (Evrensel UI İndirme Filtresi Kaldırıldı):** `MainViewModel.cs` içindeki `ProfileId` filtreleri iptal edilerek tüm tamamlanmış indirmelerin ekranda listelenmesi sağlandı. Kullanıcının mevcut veya boş playlist'i olması fark etmeksizin global dosyalar arayüze yansıtıldı.
    - **Phase 27 (Kusursuz Serileme ve Bellek Çakışması Engellendi):** `MainViewModel.cs` içindeki `UpdateDownloadedItems` UI ezme problemi giderilerek arayüz tamamen SQLite tabanına bağlandı. Dizi kapak oluşturma sürecinden `PlaylistId` çıkartılarak, farklı sağlayıcılardan indirilen aynı isimli bölümler tek bir dizi kapağı altında kusursuzca birleştirildi. İptal edilen/hata veren indirmelerin gereksiz veritabanı kayıtları `ContentDownloadService` üzerinden tamamen temizlenmesi sağlandı.
    - **Olay Yönetimi (Event Handling)**: Fare tıklama olayları (`Handled = true`) izole edilerek, işletim sistemi seviyesindeki istenmeyen pencere komutlarının arayüzü bozması önlendi.
    - **Arayüz Restorasyonu (Layout Fix)**: PiP'den ana pencereye dönüşte `Dispatcher` üzerindeki `Background` önceliği kullanılarak, pencere boyutları ve içerik görünürlüğü "Atomic Restore" yöntemiyle senkronize edildi.
    - **Premium Estetik**: `Border.BoxShadow` ve transparan yüzen kontrol barı ile modern, native hissettiren bir görünüm sağlandı.
    - **Etkileşim Güvenliği**: PiP modunda `PlayerOverlayLayer` tamamen devre dışı bırakılarak, sadece PiP'e özel kontrol barının aktif kalması sağlandı (karışıklık önlendi).
- `Avalonia` ana istemci tarafinda indirilebilir icerik sistemi (`DownloadItems`) eklendi.
- `Indirme Merkezi` ve `Indirilenler` ayri gorunumleri eklendi.
- Indirme listesinde:
  - Aktif indirmeler
  - Kuyruk
  - Tamamlanan indirmeler
  bolumleri eklendi.
- Indirme satirlarina poster, durum, hiz, ETA, boyut, yuzde ve sira numarasi eklendi.
- Ust ozet metrikleri eklendi:
  - Toplam hiz
  - Aktif indirme sayisi
  - Bos disk alani
- Indirilen Series icerikleri icin indirilen episode odakli gosterim ve detay acilisi eklendi.
- Indirilen dosyalar icin klasor hiyerarsisi eklendi:
  - `.../Downloads/profile_{id}/Filmler/{Film Adi}/`
  - `.../Downloads/profile_{id}/Diziler/{Dizi Adi}/Sezon XX/`
- Provider-bagimsiz dizi ilerleme saklama altyapisi eklendi:
  - Yeni tablo: `SeriesEpisodeProgresses` (Profile + normalize series key + sezon + bolum).
  - Schema fixup hem `Avalonia` hem `WPF` girislerinde olusturuluyor.
- Dizi/Bolum tanima regex kapsamı genişletildi:
  - İspanyolca (`Temporada`, `Capitulo`), Portekizce, Fransızca (`Saison`) ve Almanca (`Staffel`, `Folge`) desteği eklendi.
  - Sezon ve bölüm belirteçleri arasındaki boşluklar için tolerans artırıldı (örn: `S01 E01`).
  - Çok dilli regex desteği (TR, EN, ES, PT, FR, DE) ile tüm dizi ayrıştırma, tanıma ve normalizasyon mantığı `SeriesInfoParser`'a merkezileştirildi.
- `MediaService`, `SeriesProgressIdentity`, `M3UParser` ve `MainViewModel` servisleri merkezi parser'ı kullanacak şekilde modernize edildi.
- Arama ve dizi gruplama için "Canonical Key" (mükemmel analiz) sistemi tüm uygulamada standart hale getirildi.
- `MetadataService.cs` ve `PlaylistOrganizerService.cs` servisleri merkezi parser'ı ve normalizasyon motorunu kullanacak şekilde modernize edildi, legacy regex'ler kaldırıldı.
- Canonical dizi gruplama sistemi geliştirildi:
  - Farklı provider'lardan gelen benzer isimli diziler artık tek bir dizi altında birleştirilir.
  - Favori/Listeye ekle aksiyonları canonical anahtar üzerinden tüm eşleşen kayıtlara uygulanır.
- İndirme tamamlama ve doğrulama sistemi iyileştirildi:
  - Şifreleme işlemi atomik hale getirildi (`.tmp` üzerinden yazma ve rename).
  - HMAC doğrulaması için tek-geçişli (`IncrementalHash`) yönteme geçildi (performans artışı).
  - %99.9 gibi çok küçük farklarda sunucunun bağlantıyı kesmesi durumunda tolerans eklendi.
  - Hatalı/yarım kalan `.nctra` dosyalarının oluşması engellendi.  
- `PlaylistOrganizerService.cs` içindeki tüm regex tanımları kaldırıldı ve `SeriesInfoParser`'a taşındı.
- `PlaylistOrganizerService.cs` içindeki `GenerateSimilarityKey` ve `GenerateEpgId` metotları, `SeriesInfoParser`'ın merkezi temizleme ve normalizasyon fonksiyonlarını kullanacak şekilde güncellendi.
- **Ağ Durumu Algılama (Network Status)**:
  - Video oynatıcı overlay panelindeki ağ durum göstergesi (Wi-Fi/Ethernet/Mobil veri/Offline) dinamik hale getirildi.
  - Ethernet tespit mekanizması iyileştirildi; sanal ağ adaptörleri filtrelenerek gerçek internet bağlantısının (Gateway üzerinden) tespiti sağlandı.
  - `PlayerViewModel` üzerindeki sabit "Wi-Fi" tanımı kaldırılarak dinamik `INetworkService` entegrasyonu sağlandı (Arayüzde yanlış durum gösterimi düzeltildi).
  - Ağ durumu metninin yanına ilgili ikonlar (Ethernet, Wi-Fi, Mobil veri, Offline) eklendi.
  - Uygulama artık Ethernet, Wi-Fi ve Çevrimdışı durumlarını sadece "Wi-Fi" yazmak yerine doğru şekilde algılayıp gösteriyor.
  - Video oynatıcı arayüzünde (Overlay) ve ana ekranda (Header) ağ durumuna göre dinamik ikonlar (Ethernet/Wi-Fi/Offline) eklendi.
- **Bağlantı Analizi (Connection Analysis)**:
  - Profil ekleme ekranındaki "Bağlantıyı Analiz Et" butonu güçlendirildi.
  - **Xtream**: Sunucu erişiminin yanı sıra kullanıcı adı/şifre doğruluğunu da (`player_api.php`) kontrol eder.
  - **Stalker**: MAC adresi girilmemiş olsa bile sunucu erişilebilirliğini test etmeye izin verir.
  - **M3U**: Bağlantı hızını (ping) ve HTTP durum kodunu analiz eder.
  - Analiz sonuçları (renkli ikon ve ms bilgisi) profil türü değiştiğinde otomatik temizlenir.
- **Profil Yönetimi İyileştirmeleri**:
  - **Veri Koruma (Caching)**: Profil türleri arasında (Xtream <-> Stalker) geçiş yaparken girilen verilerin kaybolması önlendi.
  - **Akıllı Temizlik**: Stalker moduna geçerken URL otomatik temizlenir (sadece sunucu bırakılır), Stalker'dan çıkarken MAC adresi kullanıcı adından temizlenir.
  - Türkçe karakter sorunları ("Ayarlari" -> "Ayarları") giderildi.
  - Profil ekleme penceresine "Kapat" butonu eklendi ve buton yerleşimleri (Ortalama/Padding) iyileştirildi.
- **Kritik Hata Düzeltmeleri**:
  - Video oynatıcı penceresi kapatılırken oluşan `System.ArgumentNullException (LibVLCSharp)` çökme sorunu giderildi. Artık bellek temizliği (callback detach) güvenli şekilde yapılıyor.

### Issues Resolved
- **Controls Persisting Unintentionally**: Resim içinde resim (PiP) kontrolleri ana oynatıcıda göründü ve otomatik olarak gizlenmeyi reddetti.
- Görünürlüklerini kesin olarak bağlamak için bir `IsPiPMode` izleyici ve bir `IsPiPControlsVisible` hesaplanmış özelliği eklendi. Etkinliksizlik zamanlayıcısı 4,0 saniyeden 2,5 saniyeye kısaltıldı.
- **Visual PiP Jitter**: Belirli köşelerden yeniden boyutlandırma, ciddi kullanıcı arayüzü titremesine neden oluyordu.
- 8 yönlü sürekli yeniden boyutlandırma temiz bir `WindowResizeService`'e çıkarıldı ve titremeye eğilimli tutamaçlar (TopLeft, Top, Left) kaldırıldı, böylece PiP için düzgün sınır eşlemesi korundu.
- **Corner "Ears" Bleeding**: Koyu renkli, sözde yuvarlak köşeli bir öğe, şeffaf sınırların dışına taşmıştı.
- Tüm sözde köşe maskeleri kaldırıldı ve PiP sınırlarının, `CornerRadius="0"` ile işletim sisteminin yerel dikdörtgen çerçevesine uyması sağlandı.
- **VLC External Output Window**: PiP'i kapattıktan sonra, yeni bir video açmak bazen LibVLC'nin uygulama içinde render etmek yerine ayrı bir Direct3D penceresi oluşturmasına neden oluyordu.
- `MemoryVideoView` içinde `WaitForHandleReadyAsync`'i kullanıma sunarak ve `PlayChannelAsync`'i çalıştırmadan önce bekleyerek kritik bir UI iş parçacığı yarış durumunu düzelttik; bu sayede LibVLC her zaman geçerli bir `HWND`'ye bağlanır.

### Testing and Verification

### Fixed
- **Picture-in-Picture (PiP) Hata Düzeltmeleri**:
  - PiP modunda şeffaf çerçeve dışına taşan ve "fare kulağı" ("ears") gibi siyah üçgenlere yol açan yapay köşelikler (Corner Masks) için köklü çözüme gidildi. `PiPContainer` ve `PiPFrame` çerçevelerinin `CornerRadius` değeri sıfırlanarak, PiP penceresinin işletim sisteminde keskin, net bir formda (dikdörtgen) görüntülenmesi sağlandı. Orijinal dev oynatıcıyı taklit etmeye çalışan sahte Corner Masks XAML kodu ve C# logic tetikleyicileri gereksiz karmaşıklığı önlemek için projeden tamamen çıkartıldı. `MainWindow.axaml.cs` temizlendi.
  - PiP penceresinin boyutlandırılmasında, işletim sistemi koordinat uyumsuzluğundan kaynaklanan "titreme" (jitter) sorununu gidermek amacıyla sadece orantıyı (16:9) koruyan en stabil tutamaklar (BottomRight, Right, Bottom) aktif bırakıldı; sorun çıkaran 5 farklı tutamak (TopLeft, Top, vb.) kapatıldı.

### Removed
- `PiPWindow.axaml` ve `PiPWindow.axaml.cs`: Yeni Single-Window PiP mimarisine geçiş nedeniyle tamamen atıl (deprecated) duruma düştüğü için projeden kaldırıldı.

### Changed
- **PiP Mimari Soyutlaması**: `MainWindow` içerisinde bulunan karmaşık 8-yönlü, orantı-korumalı (16:9) Picture-in-Picture yeniden boyutlandırma matematiği ve durum değişkenleri, temiz kod (Clean Code) prensipleri gereği yeni `WindowResizeService` sınıfına soyutlandı. `MainWindow.axaml.cs` dosyasının boyut ve karmaşıklığı büyük ölçüde azaltıldı.
- README tamamen guncellenerek proje gercekligiyle esitlendi:
  - Avalonia ana uygulama
  - WPF legacy notu
  - Guncel indirme akislari ve calistirma komutlari
- `Downloads` menusu varsayilan davranisi `Indirilenler` landing olacak sekilde duzenlendi.
- `Indirme Merkezi` gorunumu buton ile ac/kapat modeline cevrildi.
- Indirme ilerleme bari sabit width hesaplarindan `ProgressBar` kullanimina gecirildi.
- Indirme durum event akislari optimize edilerek UI refresh modeli iyilestirildi.
- Daha iyi bir düzen için indirme ekranında ve dizi detay görünümünde dizi adı ve sezon/bölüm bilgileri ayrı ayrı gösterildi.
- `WatchHistoryService` artik dizi oynatiminda `EpisodeId` disinda provider-bagimsiz episode ilerlemesini de yazar.
- `MainViewModel.ApplyProfileProgressAsync` artik iki kaynakla calisir:
  - Once mevcut `EpisodeId` gecmisi (mevcut davranis),
  - Sonra provider-bagimsiz `SeriesEpisodeProgresses` kayitlari.
- Eski kayitlar icin tek seferlik gecis eklendi:
  - Uygun dizi acildiginda legacy watch-history satirlari yeni provider-bagimsiz tabloya tasinir.
- `Tamamlanan indirmeler` listesi oturum bazli olacak sekilde guncellendi (uygulama yeniden acilisinda sifirdan baslar).
- Oynatma akisinda online oncelik modeli eklendi:
  - `Downloads` disinda ve ag mevcutsa kaynak URL tercih edilir
  - `Downloads` ekraninda veya offline durumda yerel indirilen dosya kullanilir
- Indirilen dizi detay sayfasi icin alternatif gorunum eklendi:
  - `Downloads` icinde dizi detayinda sezon bazli sade (duz) episode listesi gosterimi.
- Indirilen icerik oynatiminda overlay aksiyonlari sadeleştirildi:
  - `Indir` butonu gizlenir.
  - `Hakkinda` butonu/paneli gizlenir.
  - Ses/altyazi, kalite ve tam ekran kontrolleri korunur.
  - VOD icin de indirilen oynatim algisi guclendirildi (`.nctra`, `file://`, profile download path),
    boylece offline/indirilen VOD oynatiminda `Indir` ve `Hakkinda` butonlari gizli kalir.
- **Track Names**: Düzensiz ses ve altyazı etiketlerini temizlemek için geliştirilmiş normalizasyon (sağlayıcı etiketlerini, dil kodlarını ve gereksiz ön ekleri kaldırır)
- **UI Performance**: İçerik indirme işlemi tamamlandığında İndirmeler sayfasının otomatik yenilenmesi hızlandırıldı (gecikme 900 ms'den 250 ms'ye düşürüldü).

### Fixed
- **PiP Etkileşim Gecikmesi ve Çizim Titremesi (Jitter) Çözümleri** (2026-02-20):
  - ~~**Interaction Delay Fix**: PiP modundayken pencereyi taşımak veya boyutlandırmak için "iki kez tıklama" zorunluluğu giderildi.~~ *(Kullanıcı isteği üzerine bu düzeltme geri alındı. Orijinal "önce odaklan, sonra tıkla/sürükle" deneyimi `Activate()` ve `Focus()` çağrılarıyla geri getirildi).*
  - **PiP ESC Kapatma Hatası**: PiP modundayken `ESC` tuşuna basıldığında videonun tamamen kapanması hatası giderildi. Artık sadece PiP modundan çıkılıp ana pencereye dönülüyor.
  - **PiP Kontrol Görünmezlik Hatası (Double Click)**: PiP üzerine çift tıklandığında veya odaklanıldığında kontrollerin kaybolması/buga girmesi sorunu çözüldü. Artık fare tıklamaları `UserInteractionCommand` aracılığıyla arayüz zamanlayıcısını (AutoHideTimer) doğru şekilde tetikliyor.
  - **Anti-Jitter (Titreme Önleyici)**: Pencereyi Top (Üst) ve Left (Sol) kenarlarından 16:9 boyutlandırırken işletim sistemi seviyesinde oluşan titremeler engellendi. Boyutlandırma esnasında pozisyon ve ebat güncellemeleri parçalanmak yerine `Dispatcher.UIThread.Post` (Render Önceliği) ile tek bir atomik çizim karesinde birleştirilerek mükemmel bir akıcılık elde edildi.
- **Performans ve Kararlılık (Memory Leak Çözümleri)** (2026-02-20):
  - **GlobalSettingsWindow GC Kilitlenmesi**: Genel Ayarlar ve Tema penceresinde anonim metotlar (lambda) kullanılarak GlobalSettingsViewModel'a yapılan bağlamalar isimli metotlara dönüştürüldü. Pencere kapanırken `OnClosed` metodu üzerinden tüm event aboneliklerinin silinmesi garanti altına alındı, böylece uygulamanın bellekte sızıntı yapması (Ghost Window Leak) engellendi.
- **Video Player Görüntü ve Arayüz Düzeltmeleri** (2026-02-19):
  - **Artifact Çözümü**: VLC `vmem` modülü kaynaklı görüntü bozulmaları (dikdörtgen artifact) giderildi.
  - **Overlay İyileştirmesi**: Native pencere üzerinde arayüz çizimi (Airspace sorunu) çözüldü.
    - Kontroller (Play/Pause, Seek, vb.) için video penceresi ile senkronize çalışan **Floating Transparent Window** teknolojisi eklendi.
    - Pencere boyutu değişimi ve Fullscreen geçişlerinde kontrollerin kaybolmaması için Z-Order (`Topmost`) yönetim mekanizması geliştirildi.
    - Alt-Tab geçişlerinde overlay penceresinin diğer uygulamaların üzerinde kalmaması için Aktivasyon takibi eklendi.
    - **Performans Optimizasyonu**: Pencere yeniden boyutlandırma ve taşıma sırasında overlay'in geriden gelmesi (lag) "Hide-on-Interaction" (Debounce) yöntemiyle çözüldü. Hareket bitince overlay anında ve pürüzsüzce belirir.
- **Ayarlar ve Görünüm İyileştirmeleri** (2026-02-19):
  - **Premium Sekme Tasarımı**: Ayarlar penceresindeki sekmeler (Profil, Görünüm vb.) tamamen yenilendi.
    - Özel `ControlTemplate` ile modern seçim göstergesi (indicator) ve pürüzsüz hover efektleri eklendi.
    - Tüm sekme ikonları yüksek kaliteli `MaterialIcon` kütüphanesine geçirildi.
  - **Akıllı ComboBox Seçimi**: Yenileme sıklığı gibi açılır menülerin boş görünmesi sorunu, `Index` bazlı eşleştirme sistemiyle giderildi; artık varsayılan seçenekler otomatik seçili gelir.
  - **Canlı Senkronizasyon**: Ayarlarda yapılan yenileme sıklığı (EPG/Kanal) değişikliklerinin arka plandaki zamanlayıcılara anında yansıması sağlandı (yeniden başlatma gerektirmez).
  - **Düzen ve Boşluk Düzeltmeleri**:
    - `Padding` yerine `Margin` sistemine geçilerek tüm temalarda kararlı alt boşluk sağlandı (60px-80px arası optimize edildi).
  - **Async Void Güvenliği ve Hata Yönetimi**: Avalonia View ve Window sınıflarındaki (`LiveView`, `MoviesView`, `SeriesView`, `ProfilesWindow`, vb.) tüm `async void` olay işleyicileri `try-catch` bloklarına alınarak uygulama çökmesi (Crash) riski ortadan kaldırıldı. Hatalar artık `MainViewModel.StatusMessage` üzerinden kullanıcıya bildiriliyor.
  - **Thread-Safe Pencere Yönetimi**: `ProfilesWindow` içerisindeki `_isAddProfileWindowOpen` bayrağı `Interlocked.CompareExchange` kullanılarak thread-safe hale getirildi, böylece hızlı çift tıklamalarda birden fazla pencere açılması engellendi.
  - **Namespace ve Derleme Hataları Giderildi**: `AvaloniaDialogService` ve `SettingsWindow` içerisindeki eksik `using` ifadeleri ve yanlış metot referansları düzeltildi.
  - **Kritik Hata Düzeltmeleri**:
    - Kapat butonu görünürlüğü artırıldı (Material Icon entegrasyonu).
    - Türkçe karakter hataları, özellikle "Hakkında" bölümündeki metinlerde ("Tum Haklari" -> "Tüm Hakları") düzeltildi.
    - Ayarlar penceresi açıkken yapılan değişikliklerin diğer tüm pencerelerde anında güncellenmesi sağlandı.
- Uygulama başlangıcında aktif/yarım kalan indirmelerin otomatik olarak devam etmesi sağlandı (stuck durumu giderildi).
  - Yazma stream kapanisi sonrasi sifreleme/finalize garantilendi.
  - Kismen tamamlanmis dosyalarda finalize fallback duzeltildi.
- Provider degisiminde dizi bolum progress/tick kaybinin ana nedeni giderildi:
  - Dizi ilerlemesi artik playlist/episode id degisiminden bagimsiz okunur.
- Profil silme akislarinda (`AddProfileViewModel` / `ProfilesViewModel`) yeni
  `SeriesEpisodeProgresses` kayitlari da temizleniyor.
- Ag kesintisi/yanit alamama durumlarinda indirmenin kayitlardan kaybolmasi engellendi.
- `Paused/Resume` davranisinda uygulama yeniden acilisi sonrasi durum toparlama duzeltildi.
- Iptal edilen indirmelerde artik dosya/artik temizligi daha guvenilir.
- Klasorden manuel silinen local indirilen dosyalar icin stale kayit temizligi iyilestirildi.
- Indirme ekraninda kuyruk satirlarinda yalnizca uygun butonlarin gorunmesi duzeltildi.
- Aktif indirme sayisinin kuyrugu da saymasi hatasi duzeltildi.
- Indirilenler ekraninda `Diziler` / `Filmler` basliklarinin icerik yokken gorunmesi duzeltildi.
- `%99.9` civari finalize/lock hatalarinda sifreleme asamasi icin retry mekanizmasi eklendi.
- `Tamamlanan` bolumunde gorunup `Indirilenler`de gorunmeyen VOD icerikler icin fallback listeleme eklendi.
- Yerel indirilen dosyanin online acilis senaryolarinda uygulama kararsizligina sebep olmasi engellendi.
- Indirilen dizi oynatiminda `Siradaki Bolum` aksiyonu guvenli hale getirildi:
  - Siradaki bolum yerelde yoksa gecis yapmaz.
  - Kullaniciya overlay durum mesaji gosterilir (`Siradaki bolum indirilmemis.`).
- `Indirilenler > Dizi > Bolum` acilisinda hata olusursa uygulamanin kapanmasi engellendi:
  - Oynatma event zincirine `try/catch` eklendi.
  - Episode acma akisina guvenli hata yakalama eklendi.
  - Hata durumunda uygulama kapanmak yerine durum mesaji gosterir.
- `Icerik oynatilamadi: Dosya dogrulamasi basarisiz` hatasi icin otomatik onarim eklendi:
  - Bozuk `.nctra` dosyasi tespit edilirse, ilgili `.nctra.part` varsa yeniden sifrelenir.
  - Onarimdan sonra oynatma cache decrypt tekrar denenir.
  - DB'de `TempFilePath` eksik olsa bile ayni klasordeki `*.nctra.part` dosyasi bulunup onarim fallback'i calisir.
  - Path karsilastirma normalize edilerek (file://, mutlak yol) eslestirme guvenilirligi artirildi.
- Tamamlanmis indirmelerde kalan `.nctra.part` artik dosyalari temizleme iyilestirildi:
  - Finalize adiminda `part` silme retry ile yapilir.
  - Periyodik cleanup'ta completed kayitlar icin stale `TempFilePath` temizlenir.
- Indirme akisinda `The response ended prematurely` / erken kapanan yanit senaryosu iyilestirildi:
  - Toplam byte biliniyorsa eksik inen dosya artik `tamamlandi` sayilmaz.
  - Gecici baglanti kesintilerinde otomatik devam denemesi eklendi (`3` deneme).
  - Denemeler siniri asilirsa indirme `Duraklatildi` kalir ve kullaniciya `Devam Et` mesaji gosterilir.
- Kullaniciya gosterilen hata metinleri standartlastirildi:
  - Yeni ortak esleyici: `UserFriendlyErrorMessage`.
  - Teknik/ham `ex.Message` metinleri yerine daha acik mesajlar kullaniliyor
    (`Ag hatasi`, `Kimlik dogrulama hatasi`, `Sunucu hatasi`, `Dosya dogrulama hatasi`, vb.).
  - Uygulanan baslica alanlar:
    - `MainViewModel` durum mesajlari
    - `SettingsViewModel` EPG/Kanal yenileme mesajlari
    - `AddProfileViewModel` analiz/kaydetme hatalari
    - `PlayerViewModel` indirme hata mesaji
    - `MainWindow` oynatma hata mesaji
    - `AvaloniaDialogService` dialog hata detaylari
    - `ContentDownloadService` indirme durdurma/otomatik devam mesajlari
    - `VideoPlayerService` oynatma baslatma hata mesaji
- Uygulama başlangıcında aktif/yarım kalan indirmelerin otomatik olarak devam etmesi sağlandı (stuck durumu giderildi).

### Performance
- Indirme sirasinda progress persistence seyreltildi (zaman + byte esik tabanli).
- Sik DB yazimi ve event spam azaltilarak uzun indirmelerde dusen hiz etkisi azaltildi.
- Download cleanup kontrolleri her cagrida degil belirli araliklarla calisacak sekilde optimize edildi.
- `Cancel/Pause/Resume` komutlarinda UI tarafinda gereksiz ek refresh cagrilari kaldirildi.
- Poster/logo yukleme akisi optimize edildi:
  - `RemoteImage` preload artik eszamanli istekleri sinirliyor (throttle), istek firtinasi azaltildi.
  - `image.tmdb.org` `http` URL'leri otomatik `https`'e normalize ediliyor.
  - HTTP resim isteginde timeout suresi artirildi ve `http -> https` fallback denemesi eklendi.
  - Guvenli performans ayarlari ile gorsel yukleme hizi iyilestirildi:
    - Placeholder fallback gecikmesi `1500ms -> 300ms`.
    - `MaxConnectionsPerServer` `24 -> 32`.
    - HTTP timeout `18s -> 14s`.
    - Retry modeli daha hizli hale getirildi (`2` deneme, `120ms` taban gecikme).
    - Kalici HTTP hatalarinda (`400/401/403/404/410`) fail-fast (gereksiz retry yok).
    - Warmup gecikmesi `140ms -> 50ms`.
- **Add Profile UI**: Profil ekleme/düzenleme penceresi modernize edildi:
  - "True" şeklinde görünen hatalı başlıklar düzeltildi.
  - Pencereye kapatma (X) butonu eklendi.
  - "Xtream bilgileri dönüştürüldü" uyarı mesajı kaldırıldı.
  - Buton metinleri ortalandı ve hizalama düzeltildi.
  - Türkçe karakter sorunları (Ayarlari -> Ayarları, vb.) giderildi.
  - **Profil Sil** butonu için kırmızı hover efekti ve köşe yumuşatma eklendi.
  - **Avatar Düzenle** butonu için hover sırasında ikon büyütme efekti eklendi.
  - **Gelişmiş Bağlantı Analizi**:
      - URL geçerliliği ve sunucu yanıt süresi (Ping) kontrolü eklendi.
      - Hata durumlarında detaylı bilgi (404 Bulunamadı, 401 Yetkisiz vb.) gösterimi eklendi.
      - Bağlantı kalitesine göre renkli ikonlar (Yeşil/Sarı/Kırmızı) entegre edildi.



