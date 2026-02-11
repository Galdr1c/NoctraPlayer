using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.ViewModels;
using IPTVPlayer.Views;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _isDarkTheme = true;

    public MainWindow(MainViewModel viewModel, PlayerViewModel playerViewModel, 
                      IVideoPlayerService videoPlayerService,
                      HoverPreviewService hoverPreviewService,
                      IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt"), $"[{DateTime.Now}] MainWindow initialized.\n"); } catch { }
        
        _viewModel = viewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        _hoverPreviewService = hoverPreviewService;
        _scopeFactory = scopeFactory;
        DataContext = _viewModel;
        
        PlayerArea.DataContext = _playerViewModel;
        _playerViewModel.CloseRequested += (s, e) =>
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt"), $"[{DateTime.Now}] CloseRequested event received.\n"); } catch { }
            ExitPlayerMode();
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

        StateChanged += (s, e) =>
        {
            // State changes are now handled via XAML DataTriggers for better performance
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
            if (e.PropertyName == nameof(PlayerViewModel.IsFullScreen))
            {
                if (_playerViewModel.IsFullScreen)
                {
                    WindowState = WindowState.Maximized;
                    ResizeMode = ResizeMode.NoResize;
                }
                else
                {
                    // Restore to normal (user can maximize manually if desired)
                    WindowState = WindowState.Normal;
                    ResizeMode = ResizeMode.CanResize;
                }
            }
        };

        _playerViewModel.OpenEpisodesRequested += (s, e) =>
        {
            if (_viewModel.SelectedSeries != null)
            {
                ExitPlayerMode();
                _viewModel.IsSeriesDetailVisible = true;
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

        // Keep Popup overlay aligned with main window while moving/resizing.
        LocationChanged += (_, _) => RefreshOverlayPopupPosition();
        SizeChanged += (_, _) => RefreshOverlayPopupPosition();
        StateChanged += (_, _) => RefreshOverlayPopupPosition();

        // Subscribe to Edit Channel requests
        _viewModel.RequestEditChannel += OnRequestEditChannel;
    }

    private void RefreshOverlayPopupPosition()
    {
        if (OverlayPopup.IsOpen)
        {
            var offset = OverlayPopup.HorizontalOffset;
            OverlayPopup.HorizontalOffset = offset + 1;
            OverlayPopup.HorizontalOffset = offset;
        }
    }

    private void OnRequestEditChannel(Channel channel)
    {
        try
        {
            var window = App.Current.Services.GetRequiredService<EditChannelWindow>();
            if (window.DataContext is EditChannelViewModel vm)
            {
                vm.Initialize(channel);
                window.Owner = this;
                if (window.ShowDialog() == true)
                {
                    // Refresh if needed, though ObservableObject should handle property updates
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Kanal düzenleme penceresi açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
                ExitPlayerMode();
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

    private void EnterPlayerMode()
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt"), $"[{DateTime.Now}] EnterPlayerMode called.\n"); } catch { }

        // 1. Show Player Area (covers the window content)
        PlayerArea.Visibility = Visibility.Visible;
        
        // 2. Hide Mini Player (if active)
        MiniPlayer.Visibility = Visibility.Collapsed;
        MiniVideoView.MediaPlayer = null;
        
        // 3. Hide other overlays if any
        _viewModel.IsSearchOverlayVisible = false;
        
        // 4. Attach Player
        if (VideoView.MediaPlayer == null)
        {
            if (VideoView.IsLoaded)
            {
                VideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();
            }
            else
            {
                RoutedEventHandler? loadedHandler = null;
                loadedHandler = (s, e) => 
                {
                    VideoView.Loaded -= loadedHandler; // Run only once
                    if (VideoView.MediaPlayer == null)
                        VideoView.MediaPlayer = _videoPlayerService.GetMediaPlayer();
                };
                VideoView.Loaded += loadedHandler;
            }
        }
    }

    private async void ExitPlayerMode()
    {
        try
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt"), $"[{DateTime.Now}] ExitPlayerMode called.\n"); } catch { }

            // 1. Hide Player Area FIRST to stop rendering logic in VideoView
            PlayerArea.Visibility = Visibility.Collapsed;

            // Allow UI to update and VideoView to realize it's hidden
            await Task.Delay(50);

            // 2. Stop Player Logic
            if (_playerViewModel != null)
            {
                // Reset FullScreen state if active (before stopping, to ensure proper window restoration)
                if (_playerViewModel.IsFullScreen)
                    _playerViewModel.IsFullScreen = false;
                
                _playerViewModel.StopCommand.Execute(null);
            }

            // 3. Detach MediaPlayer safely
            // Ensure we are on UI thread (we are)
            if (VideoView != null)
            {
                VideoView.MediaPlayer = null; // Detach to reset HWND hook
            }
            
            // 4. Restore Window State (handled by IsFullScreen property change mostly, but ensure here)
            if (WindowState == WindowState.Maximized && ResizeMode == ResizeMode.NoResize)
            {
                 WindowState = WindowState.Normal;
                 ResizeMode = ResizeMode.CanResize;
            }
            
            // 5. Show Main Content & Mini Player
            ShowMainContent();
            
            // 6. Reset Cursor
            Cursor = Cursors.Arrow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExitPlayerMode Error: {ex}");
            // Prevent crash
        }
    }

    private void PlayChannel(Channel channel)
    {
        try
        {
            // Sync profile ID for watch history
            if (_viewModel != null)
                _playerViewModel.CurrentProfileId = _viewModel.CurrentProfileId;
            
            // Start Playback via ViewModel
            _ = _playerViewModel.PlayChannelAsync(channel);
            
            // Enter Full Window Mode
            EnterPlayerMode();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayChannel error: {ex}");
            MessageBox.Show($"Video oynatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

        // PiP logic: If video is playing AND PlayerArea is closed, show mini player
        if (_playerViewModel.IsPlaying && PlayerArea.Visibility != Visibility.Visible)
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
        using (var scope = _scopeFactory.CreateScope())
        {
            var settingsWindow = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
            settingsWindow.Owner = this;
            settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            settingsWindow.ShowDialog();
        }
    }

    public void OpenProfileSelection()
    {
        try
        {
            if (_playerViewModel.IsPlaying)
            {
                _playerViewModel.StopCommand.Execute(null);
            }

            if (PlayerArea.Visibility == Visibility.Visible)
            {
                PlayerArea.Visibility = Visibility.Collapsed;
            }

            var profilesWindow = App.Current.Services.GetRequiredService<ProfilesWindow>();
            profilesWindow.DisableAutoSelect = true;
            profilesWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Hide();
            profilesWindow.ShowDialog();

            if (!IsVisible && Application.Current.ShutdownMode != ShutdownMode.OnExplicitShutdown)
            {
                Show();
            }
        }
        catch (Exception ex)
        {
            Show();
            MessageBox.Show($"Profil secme ekrani acilamadi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Favorites_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ShowOnlyFavorites = !_viewModel.ShowOnlyFavorites;
        _viewModel.NavigateCommand.Execute(AppView.Home); // Favorileri şu an home üzerinden de gösterebiliriz veya Search
    }

    private void EpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Bölüm listesini göster/gizle
    }

    private void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        _playerViewModel.OpenAudioSettingsCommand.Execute(null);
    }

    private void SubtitleButton_Click(object sender, RoutedEventArgs e)
    {
        _playerViewModel.OpenAudioSettingsCommand.Execute(null);
    }

    private void QualityButton_Click(object sender, RoutedEventArgs e)
    {
        _playerViewModel.OpenQualitySettingsCommand.Execute(null);
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

    private async void ContentView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        await _viewModel.LoadMoreChannelsIfNeededAsync(scrollViewer.VerticalOffset, scrollViewer.ScrollableHeight);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
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
        bool isLive = _playerViewModel.IsLiveContent;

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
                if (!isLive)
                {
                    _playerViewModel.SkipBackwardCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.Right:
                if (!isLive)
                {
                    _playerViewModel.SkipForwardCommand.Execute(null);
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
            case Key.N:
                if (_viewModel.SelectedChannel?.Type == ChannelType.Series)
                {
                    _playerViewModel.PlayNextEpisodeCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }
}
