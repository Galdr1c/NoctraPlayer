using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IPTVPlayer.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Behaviors;

public static class CachedImage
{
    public static readonly DependencyProperty SourceUrlProperty =
        DependencyProperty.RegisterAttached(
            "SourceUrl",
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourceUrlChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.RegisterAttached(
            "DecodePixelWidth",
            typeof(int),
            typeof(CachedImage),
            new PropertyMetadata(0));

    public static readonly DependencyProperty PlaceholderSourceProperty =
        DependencyProperty.RegisterAttached(
            "PlaceholderSource",
            typeof(ImageSource),
            typeof(CachedImage),
            new PropertyMetadata(null));

    private static readonly DependencyProperty RequestIdProperty =
        DependencyProperty.RegisterAttached(
            "RequestId",
            typeof(Guid),
            typeof(CachedImage),
            new PropertyMetadata(Guid.Empty));

    public static void SetSourceUrl(DependencyObject element, string? value) => element.SetValue(SourceUrlProperty, value);
    public static string? GetSourceUrl(DependencyObject element) => (string?)element.GetValue(SourceUrlProperty);

    public static void SetDecodePixelWidth(DependencyObject element, int value) => element.SetValue(DecodePixelWidthProperty, value);
    public static int GetDecodePixelWidth(DependencyObject element) => (int)element.GetValue(DecodePixelWidthProperty);

    public static void SetPlaceholderSource(DependencyObject element, ImageSource? value) => element.SetValue(PlaceholderSourceProperty, value);
    public static ImageSource? GetPlaceholderSource(DependencyObject element) => (ImageSource?)element.GetValue(PlaceholderSourceProperty);

    private static async void OnSourceUrlChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Image image)
        {
            return;
        }

        var requestId = Guid.NewGuid();
        image.SetValue(RequestIdProperty, requestId);

        var placeholder = GetPlaceholderSource(image);
        if (placeholder != null)
        {
            image.Source = placeholder;
        }

        var url = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var app = App.Current;
        if (app?.Services == null)
        {
            return;
        }

        var imageCacheService = app.Services.GetService<IImageCacheService>();
        if (imageCacheService == null)
        {
            return;
        }

        var decodeWidth = GetDecodePixelWidth(image);
        var bitmap = await imageCacheService.GetImageAsync(url, decodeWidth);
        if (bitmap == null)
        {
            return;
        }

        var activeRequestId = (Guid)image.GetValue(RequestIdProperty);
        if (activeRequestId != requestId)
        {
            return;
        }

        image.Source = bitmap;
    }
}
