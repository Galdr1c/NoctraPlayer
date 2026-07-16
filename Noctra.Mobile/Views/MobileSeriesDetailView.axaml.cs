using Avalonia.Controls;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesDetailView : UserControl
{
    public MobileSeriesDetailView()
    {
        InitializeComponent();
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            listBox.SelectedIndex = -1;
        }
    }
}
