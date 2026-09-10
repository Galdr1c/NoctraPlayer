using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Noctra.UI.Views.Player;

public partial class PlayerEpisodesSheet : UserControl
{
    public static readonly StyledProperty<IDataTemplate?> EpisodeThumbnailTemplateProperty =
        AvaloniaProperty.Register<PlayerEpisodesSheet, IDataTemplate?>(nameof(EpisodeThumbnailTemplate));

    public PlayerEpisodesSheet()
    {
        InitializeComponent();
    }

    public IDataTemplate? EpisodeThumbnailTemplate
    {
        get => GetValue(EpisodeThumbnailTemplateProperty);
        set => SetValue(EpisodeThumbnailTemplateProperty, value);
    }
}
