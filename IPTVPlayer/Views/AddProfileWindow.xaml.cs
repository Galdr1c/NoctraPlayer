using System.Windows;
using System.Windows.Input;
using IPTVPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Views;

public partial class AddProfileWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public AddProfileWindow(AddProfileViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        _serviceProvider = serviceProvider;
        
        viewModel.RequestClose += (sender, args) => 
        {
            DialogResult = true;
            Close();
        };

        viewModel.RequestAvatarPicker += ViewModel_RequestAvatarPicker;

        // Window sürükleme
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
    }

    private void ViewModel_RequestAvatarPicker(object? sender, EventArgs e)
    {
        var picker = _serviceProvider.GetRequiredService<AvatarPickerWindow>();
        picker.Owner = this;
        if (picker.ShowDialog() == true)
        {
            if (DataContext is AddProfileViewModel vm && picker.DataContext is AvatarPickerViewModel pickerVm)
            {
                vm.SetAvatar(pickerVm.SelectedAvatar ?? "default");
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
