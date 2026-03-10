using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Controls;

public partial class SeriesCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<SeriesCard, bool>(nameof(ShowHistoryMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public SeriesCard()
    {
        InitializeComponent();
    }

    private void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.AddToMyListCommand.Execute(series);
        }
    }

    private void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.ToggleFavoriteCommand.Execute(series);
        }
    }

    private void Context_RemoveFromHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Series series && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.RemoveFromHistoryCommand.Execute(series);
        }
    }
}
