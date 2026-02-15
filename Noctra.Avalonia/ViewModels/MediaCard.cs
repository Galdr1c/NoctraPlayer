using CommunityToolkit.Mvvm.ComponentModel;

namespace Noctra.Avalonia.ViewModels;

public partial class MediaCard : ObservableObject
{
    public string Title { get; }
    public string Section { get; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isInMyList;

    public MediaCard(string title, string section)
    {
        Title = title;
        Section = section;
    }
}

