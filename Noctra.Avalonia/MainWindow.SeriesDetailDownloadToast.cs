using System;
using Avalonia;
using Avalonia.Threading;
using Noctra.ViewModels;

namespace Noctra.Avalonia;

public partial class MainWindow
{
    public static readonly StyledProperty<bool> IsSeriesDetailDownloadToastVisibleProperty =
        AvaloniaProperty.Register<MainWindow, bool>(nameof(IsSeriesDetailDownloadToastVisible));

    public static readonly StyledProperty<string> SeriesDetailDownloadToastTextProperty =
        AvaloniaProperty.Register<MainWindow, string>(nameof(SeriesDetailDownloadToastText), string.Empty);

    private readonly DispatcherTimer _seriesDetailDownloadToastTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(2600)
    };

    public bool IsSeriesDetailDownloadToastVisible
    {
        get => GetValue(IsSeriesDetailDownloadToastVisibleProperty);
        private set => SetValue(IsSeriesDetailDownloadToastVisibleProperty, value);
    }

    public string SeriesDetailDownloadToastText
    {
        get => GetValue(SeriesDetailDownloadToastTextProperty);
        private set => SetValue(SeriesDetailDownloadToastTextProperty, value);
    }

    private void InitializeSeriesDetailDownloadToast()
    {
        _seriesDetailDownloadToastTimer.Tick += (_, _) => StopSeriesDetailDownloadToast();
    }

    private void UpdateSeriesDetailDownloadToast(string? propertyName)
    {
        if (propertyName is not (nameof(MainViewModel.DownloadStatusMessage)
            or nameof(MainViewModel.IsDownloadInProgress)
            or nameof(MainViewModel.IsSeriesDetailVisible)))
        {
            return;
        }

        // Season queue completion may publish from a worker. Only the UI thread
        // may update the toast, and an already-closed detail must not resurrect it.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_mainViewModel.IsSeriesDetailVisible)
            {
                StopSeriesDetailDownloadToast();
                return;
            }

            if (propertyName == nameof(MainViewModel.IsSeriesDetailVisible) ||
                (propertyName == nameof(MainViewModel.IsDownloadInProgress) &&
                 !_mainViewModel.IsDownloadInProgress) ||
                string.IsNullOrWhiteSpace(_mainViewModel.DownloadStatusMessage))
            {
                return;
            }

            SeriesDetailDownloadToastText = _mainViewModel.DownloadStatusMessage;
            IsSeriesDetailDownloadToastVisible = true;
            _seriesDetailDownloadToastTimer.Stop();
            _seriesDetailDownloadToastTimer.Start();
        });
    }

    private void StopSeriesDetailDownloadToast()
    {
        _seriesDetailDownloadToastTimer.Stop();
        IsSeriesDetailDownloadToastVisible = false;
    }
}
