using System.Windows;
using IPTVPlayer.Models;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.Views;

public partial class EditChannelWindow : Window
{
    private readonly EditChannelViewModel _viewModel;

    public EditChannelWindow(EditChannelViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += (s, e) => DialogResult = true;

        // Initialize ComboBox manually if needed or bind to enum values
        // For simplicity in code-behind:
        var types = Enum.GetValues(typeof(ChannelType));
        // Find the ComboBox in visual tree or name it in XAML
        // But better to bind in XAML. Let's assume XAML binding is fixed later or we set it here.
    }
}
