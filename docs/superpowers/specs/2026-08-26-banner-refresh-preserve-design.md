# Banner yenileme no-fill davranışı

## Karar

Bir banner daha önce `Loaded` olduysa sonraki no-fill yenilemesinde mevcut
native `AdView`/`BannerView` yok edilmeyecek. Native handle üzerindeki
`RequestRefresh()` aynı view'de yeni istek başlatacak; eski creative, yeni
creative bulunana kadar görünür kalacak. İlk yükleme no-fill ise gösterilecek
geçerli bir creative olmadığı için native kabuk temizlenecek.

## Sağlayıcılar

- AdMob `OnAdFailedToLoad` ve Huawei Petal Ads `OnAdFailed` ortak
  `BannerAdLoadState.Failed` callback'ini kullanır.
- `IBannerAdRefreshHandle` iki native provider tarafından uygulanır.
- Retry zamanlaması kontrollüdür: Release 30/60/120/240 saniye, üst sınır 5
  dakika; Debug 5 saniye tabanlıdır.

## Güvenlik ve UX

- `ClearAd()` Premium, suppression veya stale/detach recovery gibi gerçek
  geçersizleşme durumlarında creative'i yine yok eder ve retry'ı iptal eder.
- No-fill kendi başına yeni impression üretmez; yalnızca yeni istek planlar.
- Native view geçersizleşir veya detach olursa mevcut lifecycle recovery yolu
  tam recreate yapar.
