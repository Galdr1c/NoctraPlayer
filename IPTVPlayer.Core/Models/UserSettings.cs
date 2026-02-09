using CommunityToolkit.Mvvm.ComponentModel;

namespace IPTVPlayer.Models;

public partial class UserSettings : ObservableObject
{
    [ObservableProperty]
    private bool _autoPlayNext = true;

    [ObservableProperty]
    private bool _autoSkipIntro;

    [ObservableProperty]
    private bool _autoPlayPreviews;

    [ObservableProperty]
    private string _quality = "Auto";

    [ObservableProperty]
    private int _subtitleFontSize = 20;

    [ObservableProperty]
    private int _subtitleBackgroundOpacity = 75;

    [ObservableProperty]
    private string _defaultAudioLanguage = "tr";

    [ObservableProperty]
    private string _defaultSubtitleLanguage = "off";

    public UserSettings Clone()
    {
        return new UserSettings
        {
            AutoPlayNext = AutoPlayNext,
            AutoSkipIntro = AutoSkipIntro,
            AutoPlayPreviews = AutoPlayPreviews,
            Quality = Quality,
            SubtitleFontSize = SubtitleFontSize,
            SubtitleBackgroundOpacity = SubtitleBackgroundOpacity,
            DefaultAudioLanguage = DefaultAudioLanguage,
            DefaultSubtitleLanguage = DefaultSubtitleLanguage
        };
    }

    public bool Equals(UserSettings? other)
    {
        if (other == null) return false;
        return AutoPlayNext == other.AutoPlayNext &&
               AutoSkipIntro == other.AutoSkipIntro &&
               AutoPlayPreviews == other.AutoPlayPreviews &&
               Quality == other.Quality &&
               SubtitleFontSize == other.SubtitleFontSize &&
               SubtitleBackgroundOpacity == other.SubtitleBackgroundOpacity &&
               DefaultAudioLanguage == other.DefaultAudioLanguage &&
               DefaultSubtitleLanguage == other.DefaultSubtitleLanguage;
    }
}
