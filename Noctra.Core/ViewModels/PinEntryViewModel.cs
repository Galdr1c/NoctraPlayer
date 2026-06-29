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
    private const int MaxAttempts = 5;
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
    /// Lockout başladığında tetiklenir — ProfilesWindow lockout'u kalıcı hale getirir.
    /// Parametre: lockout'un biteceği UTC zaman.
    /// </summary>
    public event EventHandler<DateTime>? LockoutTriggered;

    public PinEntryViewModel(
        ISecurityService securityService,
        IDispatcherService dispatcherService,
        string pinHash,
        string profileName,
        string profileAvatar,
        string purpose, ILocalizationService localizationService)
    {
        _securityService = securityService;
        _dispatcherService = dispatcherService;
        _pinHash = pinHash;
        ProfileName = profileName;
        ProfileAvatar = profileAvatar;
        Purpose = purpose;
        _localizationService = localizationService;
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

            if (_attemptCount >= MaxAttempts)
            {
                TriggerLockout();
            }
            else
            {
                int remaining = MaxAttempts - _attemptCount;
                ErrorMessage = remaining == 1
                    ? _localizationService.GetString("PinEntry.Error.WrongPinLast")
                    : string.Format(_localizationService.GetString("PinEntry.Error.WrongPinRemainingFormat"), remaining);
            }
        }
    }

    private void TriggerLockout()
    {
        IsLocked = true;
        LockSecondsRemaining = 30;
        ErrorMessage = _localizationService.GetString("PinEntry.Error.TooManyAttempts");

        // ProfilesWindow'a lockout başladığını bildir (kalıcılık için)
        LockoutTriggered?.Invoke(this, DateTime.UtcNow.AddSeconds(30));

        int countdown = 30;

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
