namespace IPTVPlayer.Services.Interfaces;

public interface IThemeService
{
    void SetTheme(bool isDark);
    bool IsDarkTheme { get; }
}
