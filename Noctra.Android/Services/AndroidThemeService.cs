using Avalonia;
using Avalonia.Styling;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Mobile-safe theme service for Avalonia on Android.
/// Keeps the selected theme in Avalonia's ThemeVariant layer so DynamicResource
/// bindings update immediately without recreating the Activity.
/// </summary>
public sealed class AndroidThemeService : IThemeService
{
    public bool IsDarkTheme { get; private set; } = true;

    public void SetTheme(bool isDark)
    {
        IsDarkTheme = isDark;

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
