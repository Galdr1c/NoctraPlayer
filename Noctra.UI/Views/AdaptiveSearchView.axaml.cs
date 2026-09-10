using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Noctra.ViewModels;

namespace Noctra.UI.Views;

public partial class AdaptiveSearchView : UserControl
{
    public static readonly StyledProperty<Control?> ResultsHostProperty =
        AvaloniaProperty.Register<AdaptiveSearchView, Control?>(nameof(ResultsHost));

    public AdaptiveSearchView()
    {
        InitializeComponent();
    }

    public Control? ResultsHost
    {
        get => GetValue(ResultsHostProperty);
        set => SetValue(ResultsHostProperty, value);
    }

    public void FocusSearchInput()
    {
        SearchInput.Focus();
        SearchInput.SelectAll();
    }

    private void SearchInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.CommitSearchCommand.CanExecute(null))
            viewModel.CommitSearchCommand.Execute(null);

        e.Handled = true;
    }

    private void ClearSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SearchQuery = string.Empty;
            viewModel.SearchText = string.Empty;
        }

        SearchInput.Focus();
        e.Handled = true;
    }
}
