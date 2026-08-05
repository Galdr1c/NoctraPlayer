using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class PinEntryViewModel : ObservableObject
{
    private readonly ISecurityService _securityService;
    private readonly IDispatcherService _dispatcherService;
    private readonly string _pinHash;
    private readonly ILocalizationService _localizationService;
    private const int MaxAttempts = Services.ProfileService.MaxPinAttempts;
    private int _attemptCount;

    [ObservableProperty]
    private string _enteredPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorMessage))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorMessage))]
    private bool _isLocked;

    [ObservableProperty]
    private int _lockSecondsRemaining;

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

    public int PinLength => EnteredPin.Length;

    /// <summary>
    /// Lockout aktifken ErrorMessage'i gizle (UI overlap'ı önler)
    /// </summary>
    public bool ShowErrorMessage => !IsLocked && !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// true = doğru PIN, false = iptal, null = "şifremi unuttum"
    /// </summary>
    public event EventHandler<bool?>? PinResult;

    /// <summary>
    /// Her başarısız denemede tetiklenir. Parametre: güncel toplam başarısız
    /// deneme sayısı. UI bu sayıyı kalıcı state'e (ProfileService) yazar.
    /// </summary>
    public event EventHandler<int>? AttemptFailed;

    public PinEntryViewModel(
        ISecurityService securityService,
        IDispatcherService dispatcherService,
        string pinHash,
        string profileName,
        string profileAvatar,
        string purpose,
        ILocalizationService localizationService,
        int failedAttempts = 0,
        DateTime? lockedUntilUtc = null)
    {
        _securityService = securityService;
        _dispatcherService = dispatcherService;
        _pinHash = pinHash;
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
    private void PressDigit(string digit)
    {
        if (IsLocked || EnteredPin.Length >= 4) return;

        EnteredPin += digit;
        OnPropertyChanged(nameof(PinLength));

        // 4 hane dolunca otomatik doğrula
        if (EnteredPin.Length == 4)
            VerifyPin();
    }

    [RelayCommand]
    private void Backspace()
    {
        if (EnteredPin.Length > 0)
            EnteredPin = EnteredPin[..^1];

        OnPropertyChanged(nameof(PinLength));
        ErrorMessage = string.Empty;
    }

    private void VerifyPin()
    {
        if (_securityService.VerifyPin(EnteredPin, _pinHash))
        {
            PinResult?.Invoke(this, true);
        }
        else
        {
            _attemptCount++;
            EnteredPin = string.Empty;
            OnPropertyChanged(nameof(PinLength));

            ShakeTrigger++;
            AttemptFailed?.Invoke(this, _attemptCount);

            int remaining = MaxAttempts - _attemptCount;
            ErrorMessage = remaining <= 0
                ? _localizationService.GetString("PinEntry.Error.TooManyAttempts")
                : remaining == 1
                    ? _localizationService.GetString("PinEntry.Error.WrongPinLast")
                    : string.Format(_localizationService.GetString("PinEntry.Error.WrongPinRemainingFormat"), remaining);
        }
    }

    /// <summary>
    /// Kalıcı lockout başladığında UI tarafından çağrılır — kilit ekranını
    /// açar ve verilen zamana kadar geri sayım başlatır.
    /// </summary>
    public void ApplyLockout(DateTime untilUtc)
    {
        IsLocked = true;
        var seconds = Math.Max(1, (int)(untilUtc - DateTime.UtcNow).TotalSeconds);
        LockSecondsRemaining = seconds;
        ErrorMessage = _localizationService.GetString("PinEntry.Error.TooManyAttempts");

        int countdown = seconds;

        _ = Task.Run(async () =>
        {
            while (countdown > 0)
            {
                await Task.Delay(1000);
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
        });
    }

    [RelayCommand]
    private void Cancel() => PinResult?.Invoke(this, false);

    [RelayCommand]
    private void ForgotPin() => PinResult?.Invoke(this, null);
}
