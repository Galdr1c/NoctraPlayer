using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class AvatarPickerWindow : Window
{
    private AvatarPickerViewModel? _viewModel;

    public AvatarPickerWindow()
    {
        InitializeComponent();
    }

    public AvatarPickerWindow(AvatarPickerViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        BindViewModel(viewModel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as AvatarPickerViewModel);
    }

    private void BindViewModel(AvatarPickerViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel != null)
        {
            _viewModel.AvatarSelected -= ViewModel_AvatarSelected;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.AvatarSelected += ViewModel_AvatarSelected;
        }
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
        BindViewModel(null);
        base.OnClosed(e);
    }
}
