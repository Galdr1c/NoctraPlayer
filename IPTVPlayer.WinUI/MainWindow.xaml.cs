using Microsoft.UI.Xaml;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        this.InitializeComponent();
        ViewModel = viewModel;
        Title = "Kynora IPTV Player";
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Varsayılan olarak Ana Sayfayı seç (Items[0] Home)
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(Pages.SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "Home":
                    ContentFrame.Navigate(typeof(Pages.HomePage));
                    break;
                case "Player":
                    ContentFrame.Navigate(typeof(Pages.PlayerPage));
                    break;
            }
        }
    }
}
