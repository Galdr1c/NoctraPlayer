using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class WpfThemeService : IThemeService
{
    public bool IsDarkTheme { get; private set; } = true;

    public void SetTheme(bool isDark)
    {
        Helpers.ThemeHelper.SetTheme(isDark);
        IsDarkTheme = isDark;
    }
}

