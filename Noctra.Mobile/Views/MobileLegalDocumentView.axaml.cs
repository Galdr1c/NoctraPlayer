using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Noctra.Mobile.Views;

public partial class MobileLegalDocumentView : UserControl
{
    public MobileLegalDocumentView()
    {
        InitializeComponent();
    }

    public void ShowDocument(string title, string body)
    {
        TitleTextBlock.Text = title;
        BodyTextBlock.Text = body;
        IsVisible = true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        IsVisible = false;
    }
}
