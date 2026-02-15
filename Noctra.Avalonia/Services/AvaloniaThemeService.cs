using Avalonia;
using Avalonia.Styling;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public bool IsDarkTheme { get; private set; } = true;

    public void SetTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

        // Theme switching is handled via ThemeVariant to avoid runtime ResourceInclude.Source issues.
    }
}
