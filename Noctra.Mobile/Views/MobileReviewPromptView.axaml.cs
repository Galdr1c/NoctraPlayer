using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Noctra.Models;

namespace Noctra.Mobile.Views;

/// <summary>
/// Avalonia bottom-sheet overlay that replaces the native Android Dialog
/// for the review prompt fallback. Styled to match MobileUpsellView.
/// </summary>
public partial class MobileReviewPromptView : UserControl
{
    private TaskCompletionSource<ReviewPromptResult>? _tcs;

    public MobileReviewPromptView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the overlay and wait for the user to choose Rate / Later / Never.
    /// </summary>
    public Task<ReviewPromptResult> WaitForResultAsync(CancellationToken cancellationToken = default)
    {
        _tcs = new TaskCompletionSource<ReviewPromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        IsVisible = true;

        // If the caller cancels, resolve with Later so the service can snooze.
        cancellationToken.Register(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _tcs?.TrySetResult(ReviewPromptResult.Later);
                IsVisible = false;
            });
        });

        return _tcs.Task;
    }

    private void Rate_Click(object? sender, RoutedEventArgs e)
        => Complete(ReviewPromptResult.RateNow);

    private void Later_Click(object? sender, RoutedEventArgs e)
        => Complete(ReviewPromptResult.Later);

    private void Never_Click(object? sender, RoutedEventArgs e)
        => Complete(ReviewPromptResult.Never);

    private void Complete(ReviewPromptResult result)
    {
        IsVisible = false;
        _tcs?.TrySetResult(result);
    }
}
