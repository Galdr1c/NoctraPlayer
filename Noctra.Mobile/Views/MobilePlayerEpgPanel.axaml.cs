using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Noctra.Mobile.Controls;
using Noctra.Mobile.Localization;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobilePlayerEpgPanel : UserControl
{
    public MobilePlayerEpgPanel()
    {
        InitializeComponent();
    }

    public event Action<Channel>? ChannelSelected;

    public Control? VideoSlotControl => VideoSlot;

    /// <summary>
    /// Desktop EPG'deki saat başlığı / zaman penceresi mantığını mobile timeline'a uygular.
    /// </summary>
    public void InitializeTimelineHeader()
    {
        var now = DateTime.Now;
        BuildEpgTimeHeader(now);

        var windowLabel = this.FindControl<TextBlock>("EpgTimeWindowLabel");
        if (windowLabel is not null)
        {
            var start = now.AddHours(-PlayerViewModel.EpgPastHours).ToString("HH:mm");
            var end = now.AddHours(PlayerViewModel.EpgFutureHours).ToString("HH:mm");
            windowLabel.Text = $"{start} - {end}";
        }

        QueueFocusCurrentRow();
    }

    public void QueueFocusCurrentRow()
    {
        Dispatcher.UIThread.Post(FocusCurrentEpgRow, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(FocusCurrentEpgRow, DispatcherPriority.Background);
    }

    private void BuildEpgTimeHeader(DateTime now)
    {
        var canvas = this.FindControl<Canvas>("EpgTimeHeaderCanvas");
        if (canvas is null)
        {
            return;
        }

        canvas.Children.Clear();

        var lineBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
        var halfLineBrush = new SolidColorBrush(Color.Parse("#1AFFFFFF"));
        var nowBrush = new SolidColorBrush(Color.Parse("#CC7B2FBE"));
        var accentBrush = new SolidColorBrush(Color.Parse("#7B2FBE"));
        var totalMinutes = (PlayerViewModel.EpgPastHours + PlayerViewModel.EpgFutureHours) * 60;

        for (var minute = 30; minute < totalMinutes; minute += 30)
        {
            var line = new Border
            {
                Width = 1,
                Height = 36,
                Background = minute % 60 == 0 ? lineBrush : halfLineBrush
            };
            Canvas.SetLeft(line, minute * PlayerViewModel.EpgPxPerMinute);
            canvas.Children.Add(line);
        }

        for (var hour = -(int)PlayerViewModel.EpgPastHours; hour <= (int)PlayerViewModel.EpgFutureHours; hour++)
        {
            if (hour == 0)
            {
                continue;
            }

            var label = new TextBlock
            {
                Text = now.AddHours(hour).ToString("HH:mm"),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#80FFFFFF"))
            };
            Canvas.SetLeft(label, (hour + PlayerViewModel.EpgPastHours) * 60 * PlayerViewModel.EpgPxPerMinute + 4);
            Canvas.SetTop(label, 14);
            canvas.Children.Add(label);
        }

        var nowLine = new Border
        {
            Width = 1.5,
            Height = 36,
            Background = nowBrush,
            ZIndex = MobileZIndex.EpgNowLine
        };
        Canvas.SetLeft(nowLine, PlayerViewModel.EpgNowPixelPos);
        canvas.Children.Add(nowLine);

        var nowLabel = new TextBlock
        {
            FontSize = 7,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        nowLabel.Bind(TextBlock.TextProperty, new Binding("[Player.Epg.Now]")
        {
            Source = LocalizationSource.Instance
        });

        var nowBadge = new Border
        {
            Width = 26,
            Height = 17,
            CornerRadius = new CornerRadius(4),
            Background = accentBrush,
            ZIndex = MobileZIndex.EpgNowBadge,
            Child = nowLabel
        };
        Canvas.SetLeft(nowBadge, PlayerViewModel.EpgNowPixelPos - 13);
        Canvas.SetTop(nowBadge, 5);
        canvas.Children.Add(nowBadge);
    }

    private void FocusCurrentEpgRow()
    {
        var timelineScroll = this.FindControl<ScrollViewer>("EpgTimelineScroll");
        if (timelineScroll is null)
        {
            return;
        }

        var targetX = Math.Max(0, PlayerViewModel.EpgNowPixelPos - timelineScroll.Viewport.Width / 2);
        var targetY = timelineScroll.Offset.Y;

        if (DataContext is PlayerViewModel { EpgFocusRowIndex: >= 0 } playerVm)
        {
            const double rowHeight = 60;
            targetY = Math.Max(0, playerVm.EpgFocusRowIndex * rowHeight - timelineScroll.Viewport.Height / 2 + rowHeight / 2);
        }

        timelineScroll.Offset = new Vector(targetX, targetY);

        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        if (timeHeader is not null)
        {
            timeHeader.Offset = new Vector(targetX, 0);
        }

        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        if (namesScroll is not null)
        {
            namesScroll.Offset = new Vector(0, targetY);
        }
    }

    /// <summary>
    /// Timeline scroll değişince üst saat başlığını ve soldaki frozen kanal listesini senkron tutar.
    /// </summary>
    private void EpgTimelineScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer timelineScroll)
        {
            return;
        }

        var timeHeader = this.FindControl<ScrollViewer>("EpgTimeHeaderScroll");
        if (timeHeader is not null)
        {
            timeHeader.Offset = new Vector(timelineScroll.Offset.X, 0);
        }

        var namesScroll = this.FindControl<ScrollViewer>("EpgNamesScroll");
        if (namesScroll is not null)
        {
            namesScroll.Offset = new Vector(0, timelineScroll.Offset.Y);
        }
    }

    /// <summary>
    /// EPG timeline kanal satırına tıklandığında çağrılır.
    /// Seçilen kanalı ChannelSelected event'i ile iletir, EPG panelini kapatır.
    /// </summary>
    private void EpgRow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        if (sender is Control { DataContext: EpgPanelRow row })
        {
            if (DataContext is PlayerViewModel playerVm)
            {
                playerVm.ToggleEpgPanelCommand.Execute(null);
            }

            ChannelSelected?.Invoke(row.Channel);
            e.Handled = true;
        }
    }
}
