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
        var dialogIcon = this.FindControl<global::Material.Icons.Avalonia.MaterialIcon>("DialogIcon");
        if (dialogIcon == null) return;

        switch (mode)
        {
            case DialogMode.Information:
                dialogIcon.Kind = global::Material.Icons.MaterialIconKind.InformationOutline;
                dialogIcon.Foreground = this.FindResource("AccentLightBrush") as IBrush ?? Brushes.LightBlue;
                break;
            case DialogMode.Error:
                dialogIcon.Kind = global::Material.Icons.MaterialIconKind.AlertCircleOutline;
                dialogIcon.Foreground = Brushes.Tomato;
                break;
            case DialogMode.Warning:
                dialogIcon.Kind = global::Material.Icons.MaterialIconKind.AlertOutline;
                dialogIcon.Foreground = this.FindResource("WarningBrush") as IBrush ?? Brushes.Orange;
                break;
            case DialogMode.Confirmation:
                dialogIcon.Kind = global::Material.Icons.MaterialIconKind.HelpCircleOutline;
                dialogIcon.Foreground = this.FindResource("AccentBrush") as IBrush ?? Brushes.MediumPurple;
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
