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
                IconText.Text = "ℹ️";
                PrimaryButton.Content = "TAMAM";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
            case DialogMode.Error:
                IconText.Text = "❌";
                PrimaryButton.Content = "TAMAM";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
            case DialogMode.Question:
                IconText.Text = "❓";
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
