# P1-02 Search collection diff tasarımı

## Amaç

Incremental Search ranking snapshot'ının her commit'inde altı sonuç koleksiyonunun `Reset`
üretmesini ve Mobile/Desktop section feed'in bütün satır akışını yeniden kurmasını kaldırmak.

## Kapsam

- `SearchLiveChannels`, `SearchSeriesChannels`, `SearchVodChannels`
- `SearchSimilarLiveChannels`, `SearchSimilarSeriesChannels`, `SearchSimilarVodChannels`
- `MobileSectionedCardFeed` ve `DesktopSectionedCardFeed` içindeki kaynak değişim projeksiyonu

Genel `SetItems` davranışı ve Search dışındaki koleksiyonlar değiştirilmeyecek.

## Kimlik ve mutasyon kuralları

- Channel ve Series kimliği `(PlaylistId, Id)` bileşimidir.
- Aynı sıra, aynı kimlik ve aynı nesne referansı: koleksiyona dokunma, event üretme.
- Yeni kimlik: indexed `Add`/`AddRange`.
- Çıkan kimlik: indexed `Remove`/`RemoveRange`.
- Var olan kimliğin sırası değiştiyse: `Move`.
- Aynı kimlik yeni model örneğiyle geldiyse: indexed `Replace`.
- Duplicate next identity programlama hatasıdır ve commit öncesi reddedilir.
- Search hesaplama, kısa sorgu temizleme ve global reset yollarının hiçbirinde Search sonuçları
  için `ReplaceAll`/`Clear` kullanılmaz.

## Section feed projeksiyonu

Source collection event'leri yalnız sonuç koleksiyonunda Reset'i kaldırmakla bırakılmaz.
Section row projeksiyonu etkilenen section'ın bounded kaynağını yeniden gruplar ve yalnız o
section'ın değişen satırlarını replace/insert/remove eder. Diğer section header ve row
kimlikleri korunur; `FullRebuildCount` artmaz.

Sona bitişik `Add` mevcut hızlı `TryAppend` yolunu kullanır. Add uyumsuzsa veya event
Remove/Move/Replace ise tam feed rebuild yerine `TrySynchronizeSection` fallback'i çalışır.
Gerçek full rebuild yalnız section tanımı, source binding veya kolon metriği değişiminde kalır.

## Kabul

- Search sonucu commit'inde `NotifyCollectionChangedAction.Reset` yok.
- Aynı snapshot ikinci kez commit edilirse collection/row event yok.
- Bounded append yalnız indexed Add üretir.
- Remove/reorder/model replacement yalnız etkilenen item/section satırlarını değiştirir.
- Search owner/generation/cancellation atomik commit korumaları aynen kalır.
- Mobile ve Desktop aynı davranışı kullanır.
