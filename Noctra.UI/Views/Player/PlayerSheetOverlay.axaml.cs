using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Noctra.ViewModels;

namespace Noctra.UI.Views.Player;

public partial class PlayerSheetOverlay : UserControl
{
    public static readonly StyledProperty<IDataTemplate?> EpisodeThumbnailTemplateProperty =
        AvaloniaProperty.Register<PlayerSheetOverlay, IDataTemplate?>(nameof(EpisodeThumbnailTemplate));

    private PlayerViewModel? _viewModel;

    public PlayerSheetOverlay()
    {
        InitializeComponent();
    }

    public IDataTemplate? EpisodeThumbnailTemplate
    {
        get => GetValue(EpisodeThumbnailTemplateProperty);
        set => SetValue(EpisodeThumbnailTemplateProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnPlayerPropertyChanged;

        base.OnDataContextChanged(e);
        _viewModel = DataContext as PlayerViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnPlayerPropertyChanged;

        UpdateVisibility();
    }

    private void OnPlayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.IsActionsPanelOpen)
            or nameof(PlayerViewModel.IsAudioSettingsOpen)
            or nameof(PlayerViewModel.IsQualitySettingsOpen)
            or nameof(PlayerViewModel.IsSubtitleAppearanceSettingsOpen)
            or nameof(PlayerViewModel.IsInfoPanelOpen)
            or nameof(PlayerViewModel.IsEpisodesPanelOpen)
            or nameof(PlayerViewModel.IsSleepTimerPanelOpen))
            UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        IsVisible = _viewModel is not null &&
                    (_viewModel.IsActionsPanelOpen
                     || _viewModel.IsAudioSettingsOpen
                     || _viewModel.IsQualitySettingsOpen
                     || _viewModel.IsSubtitleAppearanceSettingsOpen
                     || _viewModel.IsInfoPanelOpen
                     || _viewModel.IsEpisodesPanelOpen
                     || _viewModel.IsSleepTimerPanelOpen);
    }
}
