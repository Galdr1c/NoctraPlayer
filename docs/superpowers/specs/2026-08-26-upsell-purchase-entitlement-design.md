# Upsell satın alma sheet'i ve Premium entitlement akışı

## Amaç

Google Play satın alma ekranı açıldığında Upsell sheet'inin ekranın altında
kalmasını önlemek ve satın alma tamamlandıktan sonra Premium hakkının uygulama
durumuna gecikmeden yansımasını garanti etmek.

## Davranış sözleşmesi

1. Plan butonuna basıldığında ürün/aktivite/Play Billing akışı hazırlanır.
2. `LaunchBillingFlow` başarıyla dönerse Play satın alma penceresi açılmış kabul
   edilir ve Upsell sheet hemen gizlenir.
3. Ürün bulunamazsa, aktivite yoksa, bağlantı/teknik hata oluşursa veya Billing
   akışı başlatılamazsa sheet kapanmaz; mevcut hata mesajı gösterilir.
4. Play callback'i ve Activity resume yenilemesi birincil entitlement yollarıdır.
   Callback'in kaçırıldığı veya satın alma Play tarafında birkaç saniye sonra
   görünür olduğu durumda, sınırlı süreli tamamlanma watcher'ı lisans durumunu
   tekrar sorgular.
5. Backend doğrulaması başarılı ve abonelik/lifetime aktif olduğunda tekil
   `ILicenseService` güncellenir. `SubscriptionChanged` ile Premium kontrolleri,
   reklam görünürlüğü ve Upsell UI aynı durumu görür.
6. Kullanıcı Play ekranından vazgeçerse veya bekleyen/başarısız satın alma oluşursa
   gizlenmiş sheet yeniden açılmaz; bir sonraki Upsell açılışında güncel mağaza
   durumu yenilenir. Böylece sheet, satın alma penceresiyle çakışmaz ve yanlış
   başarı mesajı vermez.
7. `AlreadyOwned` sonucu geldiğinde mağaza hakları hemen yenilenir. Hak aktifse
   sheet kapalı kalır; aktif değilse kullanıcıya aboneliği Google Play'den
   yönetmesi gerektiği bilgisi gösterilir.

## Backend doğrulama sözleşmesi

- Google Play `purchases.subscriptionsv2.get` çağrısı, `.../subscriptionsv2/tokens/{token}`
  yolunu kullanır; `tokens` segmenti eksik olursa Google 404 döndürür ve Premium
  verilemez.
- Cloud projesinde `androidpublisher.googleapis.com` API'si etkin olmalıdır.
  Etkin değilse Play doğrulaması 403/502 olur; istemci son doğrulanmış önbelleği
  korur ve yeni satın alma için yanlışlıkla Premium açmaz.
- Backend logları yalnızca ürün, durum, expiry, eşleşme ve HTTP kodu gibi tanı
  alanlarını yazar; purchase token'ı veya hesap bilgisi yazılmaz.

## Uygulama sınırları

- GMS/Google Play satın alma ve backend doğrulama akışları değişir; HMS reklam
  sağlayıcısına veya reklam kimliklerine dokunulmaz.
- Android dışındaki URI tabanlı Premium akışı korunur.
- Satın alma sonucu doğrulanmadan Premium açılmaz; istemci süre hesaplamaz.
- Watcher sınırlı retry kullanır, sonsuz ağ isteği üretmez ve uygulama kapanırken
  iptal edilir.

## Kabul kriterleri

- Play penceresi başarıyla açıldığında sheet görünmez.
- Play penceresi açılamadığında sheet görünür ve hata gösterir.
- Satın alma callback'i kaçırılsa bile aktif doğrulama sonucu kısa süre içinde
  `IsPremium == true` olur.
- Aktif Premium doğrulanınca reklam servisleri reklamları kaldırır ve yeni
  Upsell açılışı kendiliğinden kapanır.
- Mevcut lisans, reklam ve masaüstü davranış regresyon testleri geçer.
