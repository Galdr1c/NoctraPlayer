using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Noctra.Mobile.Views;

public partial class MobileSettingsView : UserControl
{
    public event EventHandler? BackToProfilesRequested;

    public MobileSettingsView()
    {
        InitializeComponent();
    }

    private void BackToProfiles_Click(object? sender, RoutedEventArgs e)
    {
        BackToProfilesRequested?.Invoke(this, EventArgs.Empty);
    }
}
