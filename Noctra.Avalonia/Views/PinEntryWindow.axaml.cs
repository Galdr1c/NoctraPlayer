using Avalonia.Controls;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class PinEntryWindow : Window
{
    public PinEntryWindow()
    {
        InitializeComponent();
    }

    public PinEntryWindow(PinEntryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not PinEntryViewModel vm) return;

        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            var digit = ((int)e.Key - (int)Key.D0).ToString();
            vm.PressDigitCommand.Execute(digit);
            e.Handled = true;
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            var digit = ((int)e.Key - (int)Key.NumPad0).ToString();
            vm.PressDigitCommand.Execute(digit);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            vm.BackspaceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
