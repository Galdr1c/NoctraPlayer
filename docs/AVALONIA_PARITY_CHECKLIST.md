# Avalonia Gorsel Parite Checklist

Bu liste WPF referansi ile Avalonia ekranlarini birebir kontrol etmek icin hazirlandi.

Durum:
- `[ ]` bekliyor
- `[/]` devam ediyor
- `[x]` tamamlandi

## Ana Ekran

- [x] Header logo, baslik, arama kutusu hizalandi
- [x] Sol nav buton sirasi ve aksiyonlari calisiyor
- [x] Card grid sarimi ve context menu aksiyonlari calisiyor
- [x] Search overlay ac/kapat/uygula/temizle
- [/] Font-weight ve spacing ince ayarlari

## Oynatici

- [x] Mini player alaninda VideoView render
- [x] Play/Pause/Stop/Full komutlari bagli
- [x] Buffer overlay gorunurlugu
- [x] Zapping overlay gorunurlugu
- [x] Volume toast gorunurlugu
- [x] Side panel slide-in/slide-out (Info/Audio/Quality)
- [/] Iconografi ve button skin birebirligi

## Yardimci Pencereler

- [x] ProfilesWindow iskelet ve profile aksiyonlari
- [x] SettingsWindow iskelet ve save/reset baglari
- [x] GlobalSettingsWindow iskelet ve komut baglari
- [x] AddProfileWindow iskelet ve save/cancel
- [x] EditChannelWindow iskelet ve save/cancel
- [x] AvatarPickerWindow secim akisi
- [x] DialogWindow bilgi/hata/onay modlari
- [x] UpsellWindow temel akis
- [/] WPF template/stil paritesi (hover/pressed/anim detay)

## Tema/Resource

- [x] Dark/Light token kaynaklari tasindi
- [x] Converter kaynaklari tasindi
- [/] WPF Styles.xaml icindeki ileri seviye template paritesi
