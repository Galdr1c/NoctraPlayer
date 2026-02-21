using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class ProfileLoadingWindow : Window
{
    public ProfileLoadingWindow()
    {
        InitializeComponent();
    }

    public ProfileLoadingWindow(ProfileLoadingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
