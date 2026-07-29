using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileVodCard : UserControl
{
    public static readonly StyledProperty<MobileCardPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<MobileVodCard, MobileCardPresentationMode>(
            nameof(PresentationMode));

    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowHistoryMenu));

    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowRemoveFavoriteMenu));

    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<MobileVodCard, bool>(nameof(ShowRemoveMyListMenu));

    public bool ShowHistoryMenu
    {
        get => GetValue(ShowHistoryMenuProperty);
        set => SetValue(ShowHistoryMenuProperty, value);
    }

    public bool ShowRemoveFavoriteMenu
    {
        get => GetValue(ShowRemoveFavoriteMenuProperty);
        set => SetValue(ShowRemoveFavoriteMenuProperty, value);
    }

    public bool ShowRemoveMyListMenu
    {
        get => GetValue(ShowRemoveMyListMenuProperty);
        set => SetValue(ShowRemoveMyListMenuProperty, value);
    }

    public MobileCardPresentationMode PresentationMode
    {
        get => GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    public MobileVodCard()
    {
        InitializeComponent();
    }

    private void CardContainer_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is MobilePressableCard pressable &&
            pressable.ConsumeLongPressTapSuppression())
        {
            e.Handled = true;
            return;
        }

        if (e.Handled ||
            DataContext is not Channel channel ||
            this.FindAncestorOfType<ItemsControl>()?.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectMediaCommand.CanExecute(channel))
        {
            viewModel.SelectMediaCommand.Execute(channel);
            e.Handled = true;
        }
    }

    private void CardContainer_LongPressed(object? sender, EventArgs e)
    {
        if (DataContext is Channel media)
        {
            MobileCardActions.Raise(
                this,
                media,
                MobileCardGridKind.Vod,
                PresentationMode);
        }
    }
}
