using System.Windows;
using System.Windows.Controls;
using IPTVPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Views;

public partial class AvatarPickerWindow : Window
{
    public AvatarPickerWindow(AvatarPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        viewModel.AvatarSelected += ViewModel_AvatarSelected;
    }

    private void ViewModel_AvatarSelected(object? sender, string avatar)
    {
        DialogResult = true;
        Close();
    }



    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is AvatarPickerViewModel vm)
        {
            vm.AvatarSelected -= ViewModel_AvatarSelected;
        }
        base.OnClosed(e);
    }
}
