using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Mobile.Controls;

public partial class MobileContinueWatchingCard : UserControl
{
    public MobileContinueWatchingCard()
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
                MobileCardGridKind.ContinueWatching,
                MobileCardPresentationMode.Standard);
        }
    }
}
