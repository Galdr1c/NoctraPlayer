namespace Noctra.Services.Interfaces;

/// <summary>
/// Oynatıcı için platforma özgü pencere davranışları.
/// Masaüstünde no-op olabilir; mobil (Android) tarafında ekranı uyanık tutma,
/// tam ekran (yatay yön + immersive) ve ekran parlaklığı kontrolünü sağlar.
/// </summary>
/// Android tam ekranda kullanıcı yön tercihi korunur; portre ve yatay kullanıma izin verilir.
public interface IPlayerWindowService
{
    /// <summary>
    /// Video oynarken ekranın kararıp kilitlenmesini engeller (FLAG_KEEP_SCREEN_ON).
    /// Oynatma başlayınca true, durunca/kapanınca false çağrılmalıdır.
    /// </summary>
    void SetKeepScreenOn(bool keepOn);

    /// <summary>
    /// Tam ekran video moduna girer/çıkar.
    /// Girişte: yatay (landscape) yön zorlanır ve durum/navigasyon çubukları gizlenir (immersive).
    /// Çıkışta: yön serbest bırakılır ve sistem çubukları geri gelir.
    /// </summary>
    void SetFullScreenMode(bool fullScreen);

    /// <summary>
    /// Ekran parlaklığını ayarlar. 0.0–1.0 arası geçerli değer; negatif değer
    /// (ör. -1) sistem varsayılan parlaklığına döner.
    /// </summary>
    void SetBrightness(double brightness);

    /// <summary>
    /// Pencerenin geçerli parlaklığını döndürür (0.0–1.0).
    /// Pencere değeri ayarlanmamışsa makul bir varsayılan (ör. 0.5) döner.
    /// </summary>
    double GetBrightness();
}
