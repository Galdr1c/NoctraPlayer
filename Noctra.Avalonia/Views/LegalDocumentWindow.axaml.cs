using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Noctra.Avalonia.Localization;

namespace Noctra.Avalonia.Views;

public partial class LegalDocumentWindow : Window
{
    public string TitleText { get; }
    public string BodyText { get; }
    public string CloseButtonText { get; }

    public LegalDocumentWindow()
        : this(string.Empty, string.Empty)
    {
    }

    public LegalDocumentWindow(string title, string body)
    {
        TitleText = title;
        BodyText = body;
        CloseButtonText = LocalizationSource.Instance["Dialog.Ok"];

        InitializeComponent();
        DataContext = this;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
