using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
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
    private MainViewModel _viewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly HoverPreviewService _hoverPreviewService;
    private bool _isDarkTheme = true;
    private WindowState _previousWindowState;
    private bool _isPlayerMode;
    private AppView _previousActiveView;
    private bool _previousSearchOverlayVisible;
    private bool _previousSeriesDetailVisible;

    public MainWindow(MainViewModel viewModel, PlayerViewModel playerViewModel, 
                      IVideoPlayerService videoPlayerService,
                      HoverPreviewService hoverPreviewService)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        _hoverPreviewService = hoverPreviewService;
        DataContext = _viewModel;
        
        // Set DataContext explicitly
        OverlayView.DataContext = _playerViewModel;
        Panel.SetZIndex(OverlayView, 1000);
        
        PlayerArea.DataContext = _playerViewModel;
        _playerViewModel.CloseRequested += (s, e) =>
        {
            PlayerArea.Visibility = Visibility.Collapsed;
            VideoView.MediaPlayer = null; // Detach to reset HWND hook
            ShowMainContent();
            ExitPlayerMode();
            
            // Ensure cursor is visible when leaving player
            Cursor = Cursors.Arrow;
        };
        _playerViewModel.NextEpisodeRequested += (s, e) =>
        {
            if (_viewModel.SelectedEpisode == null)
            {
                return;
            }

            var nextEpisode = _viewModel.GetNextEpisode(_viewModel.SelectedEpisode);
            if (nextEpisode != null)
            {
                _viewModel.PlayEpisodeCommand.Execute(nextEpisode);
            }
        };

        // Video player'ı bağla
        VideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();

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

        // Subscribe to DataContext changes to handle ViewModel reassignment from ProfilesWindow
        DataContextChanged += (s, e) =>
        {
            if (e.OldValue is MainViewModel oldVm)
            {
                oldVm.OnMediaSelected -= OnMediaSelected;
            }
            if (e.NewValue is MainViewModel newVm)
            {
                _viewModel = newVm;
                newVm.OnMediaSelected += OnMediaSelected;
            }
        };

        // Initial subscription
        _viewModel.OnMediaSelected += OnMediaSelected;

        _playerViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_playerViewModel.IsFullScreen))
            {
                if (_playerViewModel.IsFullScreen)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }

            if (e.PropertyName == nameof(_playerViewModel.IsVisible) && PlayerArea.Visibility == Visibility.Visible)
            {
                Cursor = _playerViewModel.IsVisible ? Cursors.Arrow : Cursors.None;
            }
        };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.IsSearchOverlayVisible))
            {
                if (_viewModel.IsSearchOverlayVisible)
                {
                    // Focus search box after UI updates
                    Dispatcher.BeginInvoke(() => SearchInput.Focus(), DispatcherPriority.Input);
                }
            }
        };

        // InitializeControlsTimer(); // Conflict with PlayerViewModel logic
        
        // PreviewKeyDown ile global key handling
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_playerViewModel.IsAudioSettingsOpen || _playerViewModel.IsQualitySettingsOpen)
            {
                _playerViewModel.ClosePanelsCommand.Execute(null);
                e.Handled = true;
            }
            else if (PlayerArea.Visibility == Visibility.Visible)
            {
                _playerViewModel.ClosePlayerCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void ChannelCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Channel channel)
        {
            PlayChannel(channel);
        }
        else if (sender is FrameworkElement seriesElement && seriesElement.DataContext is Series series)
        {
            // TODO: Series detay sayfasını aç
            MessageBox.Show($"Dizi detayı açılacak: {series.Name}");
        }
    }

    private void OnMediaSelected(object media)
    {
        try
        {
            if (media is Channel channel)
            {
                PlayChannel(channel);
            }
            else if (media is Series series)
            {
                // Series detail view will be shown by ViewModel
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnMediaSelected error: {ex}");
        }
    }

    private void PlayChannel(Channel channel)
    {
        try
        {
            // Sync profile ID for watch history
            if (_viewModel != null)
                _playerViewModel.CurrentProfileId = _viewModel.CurrentProfileId;
            
            // Tek otorite: PlayerViewModel
            _ = _playerViewModel.PlayChannelAsync(channel);

            EnterPlayerMode();
            _viewModel.CloseSearchCommand.Execute(null);
            _viewModel.IsSeriesDetailVisible = false;
            
            // Video player'ı göster
            PlayerArea.Visibility = Visibility.Visible;
            MiniPlayer.Visibility = Visibility.Collapsed;
            MiniVideoView.MediaPlayer = null;
            
            // ALways re-attach to ensure HWND is hooked correctly
            // Detach first just in case
            VideoView.MediaPlayer = null; 

            if (VideoView.IsLoaded)
            {
                VideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();
            }
            else
            {
                VideoView.Loaded += (s, e) => 
                {
                    if (VideoView.MediaPlayer == null)
                        VideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayChannel error: {ex}");
            MessageBox.Show($"Video oynatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EnterPlayerMode()
    {
        if (_isPlayerMode)
        {
            return;
        }

        _isPlayerMode = true;
        _previousWindowState = WindowState;
        _previousActiveView = _viewModel.ActiveView;
        _previousSearchOverlayVisible = _viewModel.IsSearchOverlayVisible;
        _previousSeriesDetailVisible = _viewModel.IsSeriesDetailVisible;
        WindowState = WindowState.Maximized;
    }

    private void ExitPlayerMode()
    {
        if (!_isPlayerMode)
        {
            return;
        }

        WindowState = _previousWindowState;
        _viewModel.ActiveView = _previousActiveView;
        _viewModel.IsSearchOverlayVisible = _previousSearchOverlayVisible;
        _viewModel.IsSeriesDetailVisible = _previousSeriesDetailVisible;
        _isPlayerMode = false;
    }

    private void ShowMainContent()
    {
        // Force visibility of views based on ViewModel state
        // This is necessary because we stopped collapsing them in PlayChannel, but if we did, we need to restore.
        // Also, if navigation state is messed up, this fixes it.
        
        // Ensure Home/Etc are visible if ActiveView matches
        // Binding should handle it, but let's trigger update
        // _viewModel.OnPropertyChanged(nameof(_viewModel.ActiveView)); // Protected, removed
        
        // Manual visibility restore just in case
        if (_viewModel.ActiveView == AppView.Home) HomeView.Visibility = Visibility.Visible;
        else if (_viewModel.ActiveView == AppView.Movies) MoviesView.Visibility = Visibility.Visible;
        else if (_viewModel.ActiveView == AppView.Series) SeriesView.Visibility = Visibility.Visible;
        else if (_viewModel.ActiveView == AppView.Search) SearchView.Visibility = Visibility.Visible;
        if (LiveView != null && _viewModel.ActiveView == AppView.Live) LiveView.Visibility = Visibility.Visible;

        // PiP logic: If video is playing, show mini player
        if (_playerViewModel.IsPlaying)
        {
            MiniPlayer.Visibility = Visibility.Visible;
            MiniVideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();
        }
        else
        {
            MiniPlayer.Visibility = Visibility.Collapsed;
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

    private void SearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.CommitSearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.CloseSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void MediaCard_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border card && card.DataContext is Channel media)
        {
            if (string.IsNullOrEmpty(media.StreamUrl)) return;

            // Find preview container and video view inside the card
            var previewContainer = card.FindName("PreviewContainer") as Border;
            var videoView = card.FindName("PreviewVideoView") as LibVLCSharp.WPF.VideoView;

            if (previewContainer != null && videoView != null)
            {
                await _hoverPreviewService.StartPreviewAsync(media.StreamUrl, previewContainer, videoView);
            }
        }
    }

    private void MediaCard_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverPreviewService.StopPreview();
    }

    private void HomeView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ParallaxTransform != null)
        {
            // Parallax: 30% of scroll speed
            ParallaxTransform.Y = e.VerticalOffset * 0.3;
        }

        if (HeroZoomTransform != null)
        {
            // Subtle zoom as we scroll
            double zoomFactor = 1.0 + (e.VerticalOffset / 2500); // 1.1 at 250px scroll
            HeroZoomTransform.ScaleX = zoomFactor;
            HeroZoomTransform.ScaleY = zoomFactor;
            
            // Fade out the hero section as we scroll down
            HeroGrid.Opacity = Math.Max(0.2, 1.0 - (e.VerticalOffset / 800));
        }
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

    private void CloseMiniPlayer_Click(object sender, RoutedEventArgs e)
    {
        MiniPlayer.Visibility = Visibility.Collapsed;
        MiniVideoView.MediaPlayer = null;
        _playerViewModel.PlayPauseCommand.Execute(null); // Stop playback
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled) return;

        // Don't intercept shortcuts if any TextBox has focus (typing) or overlay is open
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase || 
            _viewModel.IsSearchOverlayVisible ||
            e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)
        {
            if (e.Key != Key.Escape)
            {
                base.OnKeyDown(e);
                return;
            }
        }

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
            // Escape is handled in PreviewKeyDown
            case Key.Left:
                if (!_playerViewModel.IsLiveContent)
                {
                    _playerViewModel.SkipBackwardCommand.Execute("10");
                    e.Handled = true;
                }
                break;
            case Key.Right:
                if (!_playerViewModel.IsLiveContent)
                {
                    _playerViewModel.SkipForwardCommand.Execute("10");
                    e.Handled = true;
                }
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
