using System.Windows;
using System.Windows.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer;

/// <summary>
/// MainWindow.xaml etkileşim mantığı
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly PlayerViewModel _playerViewModel;
    private bool _isDarkTheme = true;

    public MainWindow(MainViewModel viewModel, PlayerViewModel playerViewModel, VideoPlayerService videoPlayerService)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        _playerViewModel = playerViewModel;
        DataContext = _viewModel;

        // Video player'ı bağla
        VideoView.MediaPlayer = videoPlayerService.GetMediaPlayer();

        // Window sürükleme
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
        };
    }

    private void ChannelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Channel channel)
        {
            _viewModel.PlayChannelCommand.Execute(channel);
            _ = _playerViewModel.PlayChannelAsync(channel);
            
            // Video player'ı göster
            PlayerArea.Visibility = Visibility.Visible;
            ChannelScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void GroupItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is string group)
        {
            _viewModel.SelectedGroup = group;
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // Card click'i tetiklememesi için
        if (sender is FrameworkElement element && element.DataContext is Channel channel)
        {
            _viewModel.ToggleFavoriteCommand.Execute(channel);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Ayarlar penceresini aç
        // TODO: SettingsWindow oluşturulacak
    }

    private void LiveTV_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectedChannelType = Models.ChannelType.Live;
    }

    private void VOD_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectedChannelType = Models.ChannelType.VOD;
    }

    private void Series_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.SelectedChannelType = Models.ChannelType.Series;
    }

    private void Favorites_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ShowOnlyFavorites = !_viewModel.ShowOnlyFavorites;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ThemeButton.Content = _isDarkTheme ? "🌙" : "☀️";
        // TODO: Tema değişikliği uygulanacak
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Keyboard shortcuts
        switch (e.Key)
        {
            case Key.Space:
                _playerViewModel.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
            case Key.F11:
                _playerViewModel.ToggleFullScreenCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                if (PlayerArea.Visibility == Visibility.Visible)
                {
                    PlayerArea.Visibility = Visibility.Collapsed;
                    ChannelScrollViewer.Visibility = Visibility.Visible;
                    e.Handled = true;
                }
                break;
            case Key.Left:
                _playerViewModel.SkipBackwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _playerViewModel.SkipForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                _playerViewModel.Volume = Math.Min(100, _playerViewModel.Volume + 5);
                e.Handled = true;
                break;
            case Key.Down:
                _playerViewModel.Volume = Math.Max(0, _playerViewModel.Volume - 5);
                e.Handled = true;
                break;
            case Key.M:
                _playerViewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}