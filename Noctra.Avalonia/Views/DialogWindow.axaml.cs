using Avalonia;
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
    public bool ShowPrimary { get; }
    public bool Result { get; private set; }

    public DialogWindow()
        : this("Bilgi", string.Empty, DialogMode.Information)
    {
    }

    public DialogWindow(string title, string message, DialogMode mode)
    {
        TitleText = title;
        MessageText = message;

        ShowPrimary = mode != DialogMode.Notification;

        (PrimaryButtonText, SecondaryButtonText, ShowSecondary) = mode switch
        {
            DialogMode.Confirmation => ("EVET", "HAYIR", true),
            DialogMode.Error => ("TAMAM", string.Empty, false),
            DialogMode.Warning => ("TAMAM", "İPTAL", true),
            _ => ("TAMAM", string.Empty, false)
        };

        InitializeComponent();
        DataContext = this;

        if (mode == DialogMode.Notification)
        {
            Width = 350;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.Manual;
            
            // Try to position bottom-right
            if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && 
                desktop.MainWindow != null)
            {
                var screen = desktop.MainWindow.Screens.ScreenFromVisual(desktop.MainWindow) ?? desktop.MainWindow.Screens.Primary;
                if (screen != null)
                {
                    var workingArea = screen.WorkingArea;
                    Position = new global::Avalonia.PixelPoint(
                        workingArea.X + workingArea.Width - (int)(Width * screen.Scaling) - 20,
                        workingArea.Y + workingArea.Height - (int)(Height * screen.Scaling) - 20);
                }
            }
        }

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
            case DialogMode.Notification:
                dialogIcon.Kind = global::Material.Icons.MaterialIconKind.CheckCircleOutline;
                dialogIcon.Foreground = Brushes.LightGreen;
                
                // Auto close notification after 4 seconds
                var timer = new System.Timers.Timer(4000);
                timer.Elapsed += (s, e) => global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Close());
                timer.AutoReset = false;
                timer.Start();
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
    Confirmation,
    Notification
}
