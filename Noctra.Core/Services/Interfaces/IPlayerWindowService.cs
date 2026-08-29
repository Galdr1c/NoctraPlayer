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
    /// Controls whether Avalonia's Android SurfaceView must be composed above
    /// the native video surface. Shell/banner mode uses false so regular
    /// Android views can remain visible; an active player uses true so
    /// Avalonia controls stay above the native video TextureView.
    /// </summary>
    void SetPlayerOverlayActive(bool active);

    /// <summary>
    /// Applies the current application theme to Android's edge-to-edge system
    /// bar backdrop and icon appearance. Light shell surfaces use dark icons;
    /// dark shell/player surfaces use light icons.
    /// </summary>
    void SetSystemBarsTheme(bool isDarkTheme);

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
