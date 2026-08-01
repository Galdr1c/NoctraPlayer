using Avalonia.Controls;
using Avalonia.Input;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesDetailView : UserControl
{
    public MobileSeriesDetailView()
    {
        InitializeComponent();
    }

    private void MyListToggleButton_Tapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedSeries is not { } series ||
            !viewModel.AddToMyListCommand.CanExecute(series))
        {
            return;
        }

        // AsyncRelayCommand remains the single-flight gate. The Button is
        // intentionally event-driven so its visual state does not become a
        // disabled/grey surface while the database write is in progress.
        viewModel.AddToMyListCommand.Execute(series);
    }

    private void FavoriteToggleButton_Tapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedSeries is not { } series ||
            !viewModel.ToggleFavoriteCommand.CanExecute(series))
        {
            return;
        }

        viewModel.ToggleFavoriteCommand.Execute(series);
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            listBox.SelectedIndex = -1;
        }
    }
}
