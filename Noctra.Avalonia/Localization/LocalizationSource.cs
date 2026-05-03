using System.ComponentModel;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Localization;

public sealed class LocalizationSource : INotifyPropertyChanged
{
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

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }

        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
        RaiseChanged();
    }

    private void OnLanguageChanged()
    {
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        // Notifying with multiple property names ensures that different Avalonia binding versions
        // and indexer binding syntaxes ([Key], Item[Key]) react to the change.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // Notify all
    }

    private const string IndexerName = "Item";
}
