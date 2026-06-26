using Avalonia.Controls;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSearchView : UserControl
{
    public MobileSearchView()
    {
        InitializeComponent();
    }

    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.CommitSearchCommand.Execute(null);
        e.Handled = true;
    }
}
