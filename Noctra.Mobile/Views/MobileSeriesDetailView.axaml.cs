using System;
using Avalonia.Controls;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesDetailView : UserControl
{
    public MobileSeriesDetailView()
    {
        InitializeComponent();
        MyListToggleButton.Click += (_, _) => Console.WriteLine($"[UI] MyListButton click tick={Environment.TickCount}");
        FavoriteToggleButton.Click += (_, _) => Console.WriteLine($"[UI] FavoriteButton click tick={Environment.TickCount}");
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            listBox.SelectedIndex = -1;
        }
    }
}
