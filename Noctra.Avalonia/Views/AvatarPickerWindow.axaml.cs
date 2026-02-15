using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class AvatarPickerWindow : Window
{
    public AvatarPickerWindow()
    {
        InitializeComponent();
    }

    public AvatarPickerWindow(AvatarPickerViewModel viewModel)
        : this()
    {
        DataContext = viewModel;

        viewModel.AvatarSelected += ViewModel_AvatarSelected;
    }

    private void ViewModel_AvatarSelected(object? sender, string avatar)
    {
        Close(true);
    }

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
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
