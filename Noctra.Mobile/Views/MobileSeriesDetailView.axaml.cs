using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSeriesDetailView : UserControl
{
    public static readonly StyledProperty<bool> IsStatusToastVisibleProperty =
        AvaloniaProperty.Register<MobileSeriesDetailView, bool>(nameof(IsStatusToastVisible));

    public static readonly StyledProperty<string> StatusToastTextProperty =
        AvaloniaProperty.Register<MobileSeriesDetailView, string>(nameof(StatusToastText), string.Empty);

    private readonly DispatcherTimer _statusToastTimer;
    private MainViewModel? _boundViewModel;

    public MobileSeriesDetailView()
    {
        InitializeComponent();

        _statusToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2600)
        };
        _statusToastTimer.Tick += OnStatusToastTimerTick;
    }

    public bool IsStatusToastVisible
    {
        get => GetValue(IsStatusToastVisibleProperty);
        private set => SetValue(IsStatusToastVisibleProperty, value);
    }

    public string StatusToastText
    {
        get => GetValue(StatusToastTextProperty);
        private set => SetValue(StatusToastTextProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachToViewModel();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachFromViewModel();
        _statusToastTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachToViewModel()
    {
        var viewModel = DataContext as MainViewModel;
        if (ReferenceEquals(_boundViewModel, viewModel))
        {
            return;
        }

        DetachFromViewModel();
        if (viewModel is null)
        {
            return;
        }

        _boundViewModel = viewModel;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachFromViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel viewModel ||
            !ReferenceEquals(_boundViewModel, viewModel))
        {
            return;
        }

        var isDownloadMessageChanged =
            e.PropertyName == nameof(MainViewModel.DownloadStatusMessage) &&
            !string.IsNullOrWhiteSpace(viewModel.DownloadStatusMessage);
        var downloadStarted =
            e.PropertyName == nameof(MainViewModel.IsDownloadInProgress) &&
            viewModel.IsDownloadInProgress;

        if (!isDownloadMessageChanged && !downloadStarted)
        {
            return;
        }

        // QueueDownloadAsync may resume off the UI thread. Marshal the
        // presentation state just like the player toast does.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible ||
                !ReferenceEquals(_boundViewModel, viewModel) ||
                string.IsNullOrWhiteSpace(viewModel.DownloadStatusMessage))
            {
                return;
            }

            StatusToastText = viewModel.DownloadStatusMessage;
            IsStatusToastVisible = true;
            _statusToastTimer.Stop();
            _statusToastTimer.Start();
        });
    }

    private void OnStatusToastTimerTick(object? sender, EventArgs e)
    {
        _statusToastTimer.Stop();
        IsStatusToastVisible = false;
    }

    private void MyListToggleButton_Tapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedSeries is not { } series ||
            !viewModel.AddToMyListCommand.CanExecute(series))
        {
            return;
        }

        // AsyncRelayCommand remains the single-flight gate. The Button is
        // intentionally event-driven so its visual state does not become a
        // disabled/grey surface while the database write is in progress.
        viewModel.AddToMyListCommand.Execute(series);
    }

    private void FavoriteToggleButton_Tapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedSeries is not { } series ||
            !viewModel.ToggleFavoriteCommand.CanExecute(series))
        {
            return;
        }

        viewModel.ToggleFavoriteCommand.Execute(series);
    }

    private static void ClearTransientSelection(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedIndex: >= 0 } listBox)
        {
            listBox.SelectedIndex = -1;
        }
    }
}
