using System.Windows;
using Microsoft.Win32;
using IPTVPlayer.ViewModels;

namespace IPTVPlayer.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
    
    private void ChangeDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "İndirme Klasörünü Seçin",
            InitialDirectory = _viewModel.DownloadPath
        };
        
        if (dialog.ShowDialog() == true)
        {
            _viewModel.DownloadPath = dialog.FolderName;
        }
    }
}
