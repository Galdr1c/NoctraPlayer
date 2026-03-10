using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Controls;

public partial class VodCard : UserControl
{
    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<VodCard, bool>(nameof(ShowHistoryMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public VodCard()
    {
        InitializeComponent();
    }

    private void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.AddToMyListCommand.Execute(channel);
        }
    }

    private void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.ToggleFavoriteCommand.Execute(channel);
        }
    }

    private void Context_RemoveFromHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Channel channel && VisualRoot is Control root && root.DataContext is MainViewModel vm)
        {
            vm.RemoveFromHistoryCommand.Execute(channel);
        }
    }
}
