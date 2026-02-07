using System.Windows;
using System.Windows.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
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

    public MainWindow(MainViewModel viewModel, PlayerViewModel playerViewModel, WatermarkViewModel watermarkViewModel, IVideoPlayerService videoPlayerService)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        _playerViewModel = playerViewModel;
        DataContext = _viewModel;
        
        PlayerArea.DataContext = _playerViewModel;
        _playerViewModel.CloseRequested += (s, e) =>
        {
            PlayerArea.Visibility = Visibility.Collapsed;
            ShowMainContent();
        };

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

        _playerViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_playerViewModel.IsFullScreen))
            {
                if (_playerViewModel.IsFullScreen)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }
        };
    }

    private void ChannelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Channel channel)
        {
            // Tek otorite: PlayerViewModel
            _ = _playerViewModel.PlayChannelAsync(channel);
            
            // Video player'ı göster
            PlayerArea.Visibility = Visibility.Visible;
            HideMainContent();
        }
        else if (sender is FrameworkElement seriesElement && seriesElement.DataContext is Series series)
        {
            // TODO: Series detay sayfasını aç
            MessageBox.Show($"Dizi detayı açılacak: {series.Name}");
        }
    }

    private void HideMainContent()
    {
        HomeView.Visibility = Visibility.Collapsed;
        MoviesView.Visibility = Visibility.Collapsed;
        SeriesView.Visibility = Visibility.Collapsed;
        SearchView.Visibility = Visibility.Collapsed;
        if (LiveView != null) LiveView.Visibility = Visibility.Collapsed;
    }

    private void ShowMainContent()
    {
        // View-model'e göre geri yükle
        _viewModel.NavigateCommand.Execute(_viewModel.ActiveView);
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

    private void Favorites_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ShowOnlyFavorites = !_viewModel.ShowOnlyFavorites;
        _viewModel.NavigateCommand.Execute(AppView.Home); // Favorileri şu an home üzerinden de gösterebiliriz veya Search
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
                if (_playerViewModel.IsAudioSettingsOpen || _playerViewModel.IsQualitySettingsOpen)
                {
                    _playerViewModel.ClosePanelsCommand.Execute(null);
                    e.Handled = true;
                }
                else if (PlayerArea.Visibility == Visibility.Visible)
                {
                    PlayerArea.Visibility = Visibility.Collapsed;
                    ShowMainContent();
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