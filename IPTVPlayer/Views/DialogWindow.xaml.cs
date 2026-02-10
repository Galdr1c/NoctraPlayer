using System.Windows;

namespace IPTVPlayer.Views;

public partial class DialogWindow : Window
{
    public string Message { get; set; } = string.Empty;
    public bool DialogResultValue { get; private set; }

    public DialogWindow(string title, string message, DialogMode mode)
    {
        InitializeComponent();
        DataContext = this;
        Title = title;
        Message = message;

        SetupMode(mode);
    }

    private void SetupMode(DialogMode mode)
    {
        switch (mode)
        {
            case DialogMode.Information:
                // Info icon path
                IconPath.Data = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
                IconPath.Fill = (System.Windows.Media.Brush)Application.Current.Resources["TextPrimaryBrush"];
                PrimaryButton.Content = "TAMAM";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
            case DialogMode.Error:
                // Error icon path
                IconPath.Data = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z");
                IconPath.Fill = System.Windows.Media.Brushes.Red; // Keep error red for visibility or use specific brush
                PrimaryButton.Content = "TAMAM";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
            case DialogMode.Question:
                // Question icon path
                IconPath.Data = System.Windows.Media.Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z");
                IconPath.Fill = (System.Windows.Media.Brush)Application.Current.Resources["TextPrimaryBrush"];
                PrimaryButton.Content = "EVET";
                SecondaryButton.Content = "HAYIR";
                SecondaryButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultValue = true;
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResultValue = false;
        DialogResult = false;
        Close();
    }
}

public enum DialogMode
{
    Information,
    Error,
    Question
}
