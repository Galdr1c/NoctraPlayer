using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Noctra.Mobile.Navigation;
using Noctra.ViewModels;

namespace Noctra.Mobile.Views;

public partial class MobileSearchView : UserControl, IMobileNavigationStateParticipant
{
    public MobileSearchView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += (_, _) => SubscribeToSearchReset();
    }

    private MainViewModel? _subscribedViewModel;
    private bool _isAttachedToVisualTree;

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = true;
        SubscribeToSearchReset();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        UnsubscribeFromSearchReset();
    }

    private void SubscribeToSearchReset()
    {
        if (!_isAttachedToVisualTree)
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            UnsubscribeFromSearchReset();
            return;
        }

        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        UnsubscribeFromSearchReset();
        _subscribedViewModel = viewModel;
        viewModel.SearchScrollResetRequested += OnSearchScrollResetRequested;
    }

    private void UnsubscribeFromSearchReset()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.SearchScrollResetRequested -= OnSearchScrollResetRequested;
            _subscribedViewModel = null;
        }
    }

    private void OnSearchScrollResetRequested(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(
            () => MobileNavigationScrollState.TryRestore(
                PrimaryScrollContent,
                MobilePageScrollState.Empty,
                allowClamping: true),
            DispatcherPriority.Loaded);

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(PrimaryScrollContent, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(PrimaryScrollContent, state, allowClamping);

    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.CommitSearchCommand.CanExecute(null))
        {
            viewModel.CommitSearchCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SearchQuery = string.Empty;
            viewModel.SearchText = string.Empty;
        }

        e.Handled = true;
    }

}
