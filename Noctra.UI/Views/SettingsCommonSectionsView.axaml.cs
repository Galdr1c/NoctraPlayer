using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Noctra.UI.Views;

public partial class SettingsCommonSectionsView : UserControl
{
    public SettingsCommonSectionsView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? BackToProfilesRequested;

    private void BackToProfiles_Click(object? sender, RoutedEventArgs e)
        => BackToProfilesRequested?.Invoke(this, e);
}
