using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileSeriesCard : UserControl
{
    public static readonly StyledProperty<MobileCardPresentationMode> PresentationModeProperty =
        AvaloniaProperty.Register<MobileSeriesCard, MobileCardPresentationMode>(
            nameof(PresentationMode));

    public static readonly StyledProperty<bool> ShowHistoryMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowHistoryMenu));

    public static readonly StyledProperty<bool> ShowRemoveFavoriteMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowRemoveFavoriteMenu));

    public static readonly StyledProperty<bool> ShowRemoveMyListMenuProperty =
        AvaloniaProperty.Register<MobileSeriesCard, bool>(nameof(ShowRemoveMyListMenu));

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

    public MobileSeriesCard()
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
            DataContext is not Series series ||
            this.FindAncestorOfType<ItemsControl>()?.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectMediaCommand.CanExecute(series))
        {
            viewModel.SelectMediaCommand.Execute(series);
            e.Handled = true;
        }
    }

    private void CardContainer_LongPressed(object? sender, EventArgs e)
    {
        if (DataContext is Series media)
        {
            MobileCardActions.Raise(
                this,
                media,
                MobileCardGridKind.Series,
                PresentationMode);
        }
    }
}
