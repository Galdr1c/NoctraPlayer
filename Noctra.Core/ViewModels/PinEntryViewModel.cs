using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class PinEntryViewModel : ObservableObject
{
    private readonly ISecurityService _securityService;
    private readonly string _pinHash;
    private readonly ILocalizationService _localizationService;
    private const int MaxAttempts = 5;
    private int _attemptCount;

    [ObservableProperty]
    private string _enteredPin = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private int _lockSecondsRemaining;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _profileAvatar = string.Empty;

    [ObservableProperty]
    private string _purpose = string.Empty;

    public int PinLength => EnteredPin.Length;

    /// <summary>
    /// true = doğru PIN, false = iptal, null = "şifremi unuttum"
    /// </summary>
    public event EventHandler<bool?>? PinResult;

    public PinEntryViewModel(
        ISecurityService securityService,
        string pinHash,
        string profileName,
        string profileAvatar,
        string purpose, ILocalizationService localizationService)
    {
        _securityService = securityService;
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

        _ = Task.Run(async () =>
        {
            while (LockSecondsRemaining > 0)
            {
                await Task.Delay(1000);
                LockSecondsRemaining--;
            }

            IsLocked = false;
            _attemptCount = 0;
            ErrorMessage = string.Empty;
        });
    }

    [RelayCommand]
    private void Cancel() => PinResult?.Invoke(this, false);

    [RelayCommand]
    private void ForgotPin() => PinResult?.Invoke(this, null);
}
