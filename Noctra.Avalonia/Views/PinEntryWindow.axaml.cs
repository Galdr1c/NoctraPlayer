using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class PinEntryWindow : Window
{
    private int _lastShakeTrigger;
    private bool _isShaking;
    private PinEntryViewModel? _subscribedViewModel;

    public PinEntryWindow()
    {
        InitializeComponent();
    }

    public PinEntryWindow(PinEntryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SubscribeToViewModel(viewModel);
        Closed += (_, _) => UnsubscribeFromViewModel();
    }

    private void SubscribeToViewModel(PinEntryViewModel vm)
    {
        UnsubscribeFromViewModel();
        _subscribedViewModel = vm;
        vm.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not PinEntryViewModel vm) return;

        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            var digit = ((int)e.Key - (int)Key.D0).ToString();
            vm.PressDigitCommand.Execute(digit);
            e.Handled = true;
        }
        else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
        {
            var digit = ((int)e.Key - (int)Key.NumPad0).ToString();
            vm.PressDigitCommand.Execute(digit);
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            vm.BackspaceCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PinEntryViewModel.ShakeTrigger) &&
            DataContext is PinEntryViewModel vm &&
            vm.ShakeTrigger != _lastShakeTrigger)
        {
            _lastShakeTrigger = vm.ShakeTrigger;
            if (!_isShaking)
                _ = PlayShakeAnimation();
        }
    }

    private async System.Threading.Tasks.Task PlayShakeAnimation()
    {
        _isShaking = true;
        try
        {
            var transform = new TranslateTransform(0, 0);
            PinDotsHost.RenderTransform = transform;

            var errorBrush = this.FindResource("ErrorBrush") as IBrush;
            var dotBrushes = new[] { Dot1.Fill, Dot2.Fill, Dot3.Fill, Dot4.Fill };

            // Red flash on dots
            if (errorBrush is not null)
            {
                Dot1.Fill = errorBrush;
                Dot2.Fill = errorBrush;
                Dot3.Fill = errorBrush;
                Dot4.Fill = errorBrush;
            }

            // Shake: left-right-left-right with decreasing amplitude
            await AnimateShake(transform, 14, 60);
            await AnimateShake(transform, -12, 50);
            await AnimateShake(transform, 8, 40);
            await AnimateShake(transform, -6, 30);
            await AnimateShake(transform, 3, 20);
            transform.X = 0;

            // Restore dot colors after a brief pause
            await System.Threading.Tasks.Task.Delay(300);
            Dot1.Fill = dotBrushes[0];
            Dot2.Fill = dotBrushes[1];
            Dot3.Fill = dotBrushes[2];
            Dot4.Fill = dotBrushes[3];
        }
        finally { _isShaking = false; }
    }

    private static async System.Threading.Tasks.Task AnimateShake(TranslateTransform transform, double targetX, int durationMs)
    {
        var start = transform.X;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < durationMs)
        {
            var t = (double)sw.ElapsedMilliseconds / durationMs;
            // Ease-out cubic
            t = 1 - Math.Pow(1 - t, 3);
            transform.X = start + (targetX - start) * t;
            await System.Threading.Tasks.Task.Delay(8);
        }
        transform.X = targetX;
    }
}
