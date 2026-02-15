using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class EditChannelWindow : Window
{
    private EditChannelViewModel? _viewModel;

    public EditChannelWindow()
    {
        InitializeComponent();
    }

    public EditChannelWindow(EditChannelViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        _viewModel = viewModel;
        _viewModel.RequestClose += ViewModel_RequestClose;
    }

    private void DragBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.RequestClose -= ViewModel_RequestClose;
        }

        base.OnClosed(e);
    }

    private void ViewModel_RequestClose(object? sender, EventArgs e)
    {
        Close(true);
    }
}
