using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Noctra.Avalonia.Views;

public partial class DialogWindow : Window
{
    public string TitleText { get; }
    public string MessageText { get; }
    public string PrimaryButtonText { get; }
    public string SecondaryButtonText { get; }
    public bool ShowSecondary { get; }
    public bool Result { get; private set; }

    public DialogWindow()
        : this("Bilgi", string.Empty, DialogMode.Information)
    {
    }

    public DialogWindow(string title, string message, DialogMode mode)
    {
        TitleText = title;
        MessageText = message;

        (PrimaryButtonText, SecondaryButtonText, ShowSecondary) = mode switch
        {
            DialogMode.Confirmation => ("EVET", "HAYIR", true),
            DialogMode.Error => ("TAMAM", string.Empty, false),
            DialogMode.Warning => ("TAMAM", "İPTAL", true),
            _ => ("TAMAM", string.Empty, false)
        };

        InitializeComponent();
        DataContext = this;

        SetupIcon(mode);
    }

    private void SetupIcon(DialogMode mode)
    {
        var iconPath = this.FindControl<global::Avalonia.Controls.Shapes.Path>("IconPath");
        if (iconPath == null) return;

        switch (mode)
        {
            case DialogMode.Information:
                iconPath.Data = StreamGeometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");
                iconPath.Fill = this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White;
                break;
            case DialogMode.Error:
                iconPath.Data = StreamGeometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 13.59L15.59 17 12 13.41 8.41 17 7 15.59 10.59 12 7 8.41 8.41 7 12 10.59 15.59 7 17 8.41 13.41 12 17 15.59z");
                iconPath.Fill = Brushes.Red;
                break;
            case DialogMode.Warning:
                iconPath.Data = StreamGeometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
                iconPath.Fill = this.FindResource("WarningBrush") as IBrush ?? Brushes.Orange;
                break;
            case DialogMode.Confirmation:
                iconPath.Data = StreamGeometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z");
                iconPath.Fill = this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.White;
                break;
        }
    }

    private void Primary_Click(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close(true);
    }

    private void Secondary_Click(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close(false);
    }
}

public enum DialogMode
{
    Information,
    Warning,
    Error,
    Confirmation
}
