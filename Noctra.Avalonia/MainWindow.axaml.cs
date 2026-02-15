using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using LibVLCSharp.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia;

public partial class MainWindow : Window
{
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly MainViewModel _mainViewModel;
    private readonly PlayerViewModel _playerViewModel;
    private readonly VideoView _videoView;

    public MainWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<PlayerViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IVideoPlayerService>())
    {
    }

    public MainWindow(MainViewModel mainViewModel, PlayerViewModel playerViewModel, IVideoPlayerService videoPlayerService)
    {
        InitializeComponent();

        _mainViewModel = mainViewModel;
        _playerViewModel = playerViewModel;
        _videoPlayerService = videoPlayerService;
        DataContext = _mainViewModel;

        _videoView = new VideoView
        {
            MediaPlayer = _videoPlayerService.GetMediaPlayer(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        VideoHost.Children.Add(_videoView);
        OverlayControl.DataContext = _playerViewModel;
        Closed += OnClosed;
        _mainViewModel.OnMediaSelected += MainViewModel_OnMediaSelected;
        _playerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mainViewModel.OnMediaSelected -= MainViewModel_OnMediaSelected;
        _playerViewModel.PropertyChanged -= PlayerViewModel_PropertyChanged;
        _videoView.MediaPlayer = null;
    }

    // === Window Chrome ===
    private void HeaderBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    // === Settings ===
    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = ((App)Application.Current!).Services.GetRequiredService<Views.SettingsWindow>();
        await settingsWindow.ShowDialog(this);
    }

    // === Group Filter Reset ===
    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {
        _mainViewModel.SelectedGroup = null;
    }

    // === Search Overlay Keyboard ===
    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _mainViewModel.CommitSearchCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            _mainViewModel.CloseSearchCommand.Execute(null);
        }
    }

    // === Media Selection ===
    private async void MainViewModel_OnMediaSelected(object? media)
    {
        if (media is not Channel channel)
        {
            return;
        }

        PlayerArea.IsVisible = true;
        await _playerViewModel.PlayChannelAsync(channel);
    }

    // === Player ===
    private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.IsFullScreen))
        {
            return;
        }

        WindowState = _playerViewModel.IsFullScreen
            ? WindowState.FullScreen
            : WindowState.Normal;
    }

    // === Navigation ===
    private void NavigateHome_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Home);
    private void NavigateLive_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Live);
    private void NavigateMovies_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Movies);
    private void NavigateSeries_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Series);
    private void NavigateSearch_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Search);
    private void NavigateMyList_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.MyList);
    private void NavigateFavorites_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.Favorites);
    private void NavigateHistory_Click(object? sender, RoutedEventArgs e) => _mainViewModel.NavigateCommand.Execute(AppView.History);

    private void SwitchProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var profilesWindow = ((App)Application.Current!).Services.GetRequiredService<Views.ProfilesWindow>();
        profilesWindow.DisableAutoSelect = true;
        desktop.MainWindow = profilesWindow;
        profilesWindow.Show();
        Hide();
    }
}
