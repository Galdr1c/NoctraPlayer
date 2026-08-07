using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class PinEntryViewModel : ObservableObject, IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IDispatcherService _dispatcherService;
    private readonly int _profileId;
    private readonly ILocalizationService _localizationService;
    private readonly CancellationTokenSource _lockoutCts = new();
    private bool _disposed;
    private const int MaxAttempts = Services.ProfileService.MaxPinAttempts;
    private int _attemptCount;

    [ObservableProperty]
    private string _enteredPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorMessage))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorMessage))]
    [NotifyPropertyChangedFor(nameof(IsKeypadEnabled))]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockoutMessageText))]
    private int _lockSecondsRemaining;

    /// <summary>
    /// Doğrulama + kalıcı sayaç/kilit güncellemesi (VerifyAttemptAsync) devam
    /// ederken keypad kısa süreliğine devre dışı kalır. Böylece art arda girilen
    /// PIN'ler veritabanındaki deneme sayacını yarışa sokmaz; her deneme atomik
    /// servis çağrısıyla tamamlanmadan bir sonraki başlayamaz.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeypadEnabled))]
    private bool _isVerifying;

    /// <summary>
    /// Lockout sırasında gösterilen birleşik mesaj — neden kilitlenildiği ve
    /// kalan süre birlikte sunulur ("PinEntry.Error.ProfileLockedFormat").
    /// Böylece sayaç tek başına, bağlamsız görünmez.
    /// </summary>
    public string LockoutMessageText => string.Format(
        _localizationService.GetString("PinEntry.Error.ProfileLockedFormat"),
        LockSecondsRemaining);

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _profileAvatar = string.Empty;

    [ObservableProperty]
    private string _purpose = string.Empty;

    /// <summary>
    /// Her hatalı PIN girişinde 1 artar. UI tarafı bu değişikliği dinleyip
    /// sallanma (shake) animasyonunu tetikler.
    /// </summary>
    [ObservableProperty]
    private int _shakeTrigger;

    /// <summary>
    /// Keypad yalnızca kilit veya devam eden doğrulama sırasında devre dışı kalır.
    /// </summary>
    public bool IsKeypadEnabled => !IsLocked && !IsVerifying;

    public int PinLength => EnteredPin.Length;

    /// <summary>
    /// Ekran okuyucuya PIN ilerlemesini bildiren metin — nokta göstergesinin
    /// "4 haneden 2'si girildi" gibi bağlamsız görünmesini engeller
    /// ("PinEntry.ProgressA11yFormat": {0}=toplam hane, {1}=girilen hane).
    /// </summary>
    public string PinProgressA11yText => string.Format(
        _localizationService.GetString("PinEntry.ProgressA11yFormat"),
        4,
        PinLength);

    /// <summary>
    /// Lockout aktifken ErrorMessage'i gizle (UI overlap'ı önler)
    /// </summary>
    public bool ShowErrorMessage => !IsLocked && !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// true = doğru PIN, false = iptal, null = "şifremi unuttum"
    /// </summary>
    public event EventHandler<bool?>? PinResult;

    /// <summary>
    /// Her başarısız denemede tetiklenir. Parametre: veritabanındaki kalıcı
    /// toplam başarısız deneme sayısı (atomik servis yanıtından alınır).
    /// Sayaç ve kilit artık tek servis çağrısında güncellenir — UI'ın ayrıca
    /// persist etmesi gerekmez.
    /// </summary>
    public event EventHandler<int>? AttemptFailed;

    public PinEntryViewModel(
        IProfileService profileService,
        IDispatcherService dispatcherService,
        int profileId,
        string profileName,
        string profileAvatar,
        string purpose,
        ILocalizationService localizationService,
        int failedAttempts = 0,
        DateTime? lockedUntilUtc = null)
    {
        _profileService = profileService;
        _dispatcherService = dispatcherService;
        _profileId = profileId;
        ProfileName = profileName;
        ProfileAvatar = profileAvatar;
        Purpose = purpose;
        _localizationService = localizationService;
        _attemptCount = Math.Max(0, failedAttempts);

        if (lockedUntilUtc is { } until && until > DateTime.UtcNow)
        {
            ApplyLockout(until);
        }
    }

    [RelayCommand]
    private async Task PressDigit(string digit)
    {
        if (IsLocked || IsVerifying || EnteredPin.Length >= 4) return;

        EnteredPin += digit;
        OnPropertyChanged(nameof(PinLength));
        OnPropertyChanged(nameof(PinProgressA11yText));

        // 4 hane dolunca otomatik doğrula — tek atomik servis çağrısı
        // (PIN kontrolü + kalıcı sayaç/kilit güncellemesi birlikte).
        if (EnteredPin.Length == 4)
            await VerifyPinAsync();
    }

    [RelayCommand]
    private void Backspace()
    {
        if (IsLocked || IsVerifying) return;

        if (EnteredPin.Length > 0)
            EnteredPin = EnteredPin[..^1];

        OnPropertyChanged(nameof(PinLength));
        OnPropertyChanged(nameof(PinProgressA11yText));
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// PIN'i doğrular ve deneme sayacını/kilit durumunu TEK atomik servis
    /// çağrısıyla günceller. Sonuç kalıcı state'ten geldiği için UI yerel
    /// sayacına güvenmez; eşzamanlı denemeler sayaçta kayıp yaratamaz.
    /// </summary>
    private async Task VerifyPinAsync()
    {
        if (_disposed) return;

        IsVerifying = true;
        try
        {
            // Verifier servis tarafından veritabanından okunur (profile.PinHash) —
            // VM doğrulama sonucunu kendi verifier'ıyla etkileyemez.
            var result = await _profileService.VerifyAttemptAsync(
                _profileId,
                EnteredPin);

            if (_disposed) return;

            if (result.IsValid)
            {
                _attemptCount = 0;
                PinResult?.Invoke(this, true);
                return;
            }

            // Kalıcı sayaç servisten gelir — eşiğe (MaxAttempts) dayalı mesajlar
            // veritabanındaki gerçek sayaçla tutarlıdır.
            _attemptCount = result.State.FailedPinAttempts;
            EnteredPin = string.Empty;
            OnPropertyChanged(nameof(PinLength));
            OnPropertyChanged(nameof(PinProgressA11yText));

            ShakeTrigger++;
            AttemptFailed?.Invoke(this, _attemptCount);

            if (result.State.IsLocked)
            {
                // Neden + geri sayım birlikte gösterilir (LockoutMessageText);
                // ApplyLockout ayrıca ErrorMessage'i temizler (UI çakışması olmaz).
                ApplyLockout(result.State.PinLockedUntilUtc!.Value);
                return;
            }

            int remaining = MaxAttempts - _attemptCount;
            ErrorMessage = remaining <= 0
                ? _localizationService.GetString("PinEntry.Error.TooManyAttempts")
                : remaining == 1
                    ? _localizationService.GetString("PinEntry.Error.WrongPinLast")
                    : string.Format(_localizationService.GetString("PinEntry.Error.WrongPinRemainingFormat"), remaining);
        }
        catch
        {
            if (_disposed) return;

            // DB/verifier hatası kilit akışını bozmasın — giriş temizlenir,
            // genel hata gösterilir; sayaç artırılmaz (hiçbir şey kalıcılaşmadı).
            EnteredPin = string.Empty;
            OnPropertyChanged(nameof(PinLength));
            OnPropertyChanged(nameof(PinProgressA11yText));
            ErrorMessage = _localizationService.GetString("PinEntry.Error.VerificationFailed");
        }
        finally
        {
            IsVerifying = false;
        }
    }

    /// <summary>
    /// Kalıcı lockout başladığında çağrılır — kilit ekranını açar ve verilen
    /// zamana kadar geri sayım başlatır.
    /// </summary>
    public void ApplyLockout(DateTime untilUtc)
    {
        if (_disposed) return;

        IsLocked = true;
        var seconds = Math.Max(1, (int)(untilUtc - DateTime.UtcNow).TotalSeconds);
        LockSecondsRemaining = seconds;
        // Neden, birleşik LockoutMessageText içinde yer alır; ShowErrorMessage
        // kilit sırasında zaten gizlenir, ayrıca hata metni tutmaya gerek yok.
        ErrorMessage = string.Empty;

        int countdown = seconds;

        _ = Task.Run(async () =>
        {
            try
            {
                while (countdown > 0)
                {
                    await Task.Delay(1000, _lockoutCts.Token);
                    countdown--;
                    // Yerel değişken kullan — race condition'u önle
                    var captured = countdown;
                    _dispatcherService.BeginInvoke(() => LockSecondsRemaining = captured);
                }

                _dispatcherService.BeginInvoke(() =>
                {
                    IsLocked = false;
                    _attemptCount = 0;
                    ErrorMessage = string.Empty;
                });
            }
            catch (OperationCanceledException)
            {
                // VM elden çıkarıldı — geri sayım durur, UI güncellemesi gönderilmez.
            }
        }, _lockoutCts.Token);
    }

    /// <summary>
    /// PIN ekranı kapatıldığında çağrılır — devam eden lockout sayacını iptal
    /// eder. Aksi halde detached Task.Run 30 saniye daha yaşar ve ölü ViewModel'e
    /// dispatcher güncellemeleri göndermeye devam ederdi.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockoutCts.Cancel();
        _lockoutCts.Dispose();
    }

    [RelayCommand]
    private void Cancel() => PinResult?.Invoke(this, false);

    [RelayCommand]
    private void ForgotPin() => PinResult?.Invoke(this, null);
}
