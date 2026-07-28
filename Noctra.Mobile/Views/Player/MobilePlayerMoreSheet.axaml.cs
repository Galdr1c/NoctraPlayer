using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views.Player;

public partial class MobilePlayerMoreSheet : UserControl
{
    public MobilePlayerMoreSheet()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            viewModel.CloseAllPanels();
        }
    }
}