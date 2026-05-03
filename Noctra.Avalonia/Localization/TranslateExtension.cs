using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Noctra.Avalonia.Localization;

public class TranslateExtension : MarkupExtension
{
    public TranslateExtension()
    {
    }

    public TranslateExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;
    public string? StringFormat { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
            StringFormat = StringFormat
        };
    }
}
