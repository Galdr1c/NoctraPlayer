using System.Windows;
using System.Windows.Input;
using Noctra.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Noctra.Views;

public partial class UpsellWindow : Window
{
    private readonly ILicenseService _licenseService;

    public UpsellWindow(ILicenseService licenseService)
    {
        InitializeComponent();
        _licenseService = licenseService;

        // Window sürükleme
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BuyButton_Click(object sender, RoutedEventArgs e)
    {
        _licenseService.ActivatePremium();
        DialogResult = true;
        Close();
    }

    private void ContinueFree_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true; // Free olarak devam et
        Close();
    }
}


