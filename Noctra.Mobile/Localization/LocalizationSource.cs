using System.ComponentModel;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.Localization;

public sealed class LocalizationSource : INotifyPropertyChanged
{
    private const string IndexerName = "Item";
    private ILocalizationService? _localizationService;

    public static LocalizationSource Instance { get; } = new();

    private LocalizationSource()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _localizationService?.GetString(key) ?? key;

    public void Initialize(ILocalizationService localizationService)
    {
        if (ReferenceEquals(_localizationService, localizationService))
        {
            return;
        }

        if (_localizationService is not null)
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }

        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        RaiseChanged();
    }

    private void OnLanguageChanged() => RaiseChanged();

    private void RaiseChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
