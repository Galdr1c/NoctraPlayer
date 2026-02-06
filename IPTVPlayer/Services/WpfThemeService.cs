using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

public class WpfThemeService : IThemeService
{
    public bool IsDarkTheme { get; private set; } = true;

    public void SetTheme(bool isDark)
    {
        Helpers.ThemeHelper.SetTheme(isDark);
        IsDarkTheme = isDark;
    }
}
