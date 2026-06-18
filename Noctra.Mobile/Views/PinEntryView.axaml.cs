using Avalonia.Controls;
using Avalonia.Input;

namespace Noctra.Mobile.Views;

public partial class PinEntryView : UserControl
{
    public PinEntryView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not Noctra.ViewModels.PinEntryViewModel viewModel)
        {
            return;
        }

        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            viewModel.PressDigitCommand.Execute(((int)e.Key - (int)Key.D0).ToString());
            e.Handled = true;
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            viewModel.PressDigitCommand.Execute(((int)e.Key - (int)Key.NumPad0).ToString());
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            viewModel.BackspaceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
