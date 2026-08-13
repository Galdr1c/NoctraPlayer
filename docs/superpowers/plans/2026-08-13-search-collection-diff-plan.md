# P1-02 Search collection diff uygulama planı

1. `BatchObservableCollection.RemoveRange` ve event sözleşmesi için RED test yaz.
2. Generic composite-identity synchronizer için no-op/add/remove/move/replace RED testleri yaz.
3. `SectionedIncrementalRowCollection.TrySynchronizeSection` için section identity/no-reset
   RED testleri yaz.
4. MainViewModel ve mobile/desktop feed kaynak sözleşmelerini RED durumda sabitle.
5. Koleksiyon primitive'lerini ve identity synchronizer'ı uygula.
6. Section-local row senkronizasyonunu ve iki feed fallback'ini uygula.
7. Altı Search commit ve temizleme yolunu synchronizer'a geçir.
8. Odaklı test, 10 tekrar, tam regresyon ve bağımsız Critical/Important review çalıştır.
9. Android APK build, veri koruyarak kurulum, Search query/scroll/menu/resume kabulü yap.
10. Ana performans raporunu kanıtlarla güncelle.
