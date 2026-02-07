using System.Windows;
using IPTVPlayer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Views;

public partial class UpsellWindow : Window
{
    private readonly ILicenseService _licenseService;

    public UpsellWindow(ILicenseService licenseService)
    {
        InitializeComponent();
        _licenseService = licenseService;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Kapatınca uygulamadan çıkmak mı yoksa free devam etmek mi?
        // Genelde pencereyi kapatmak uygulamayı kapatır.
        Application.Current.Shutdown();
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
