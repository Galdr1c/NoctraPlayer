using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class AvaloniaFilePickerService : IPlaylistFilePickerService
{
    public async Task<string?> PickM3uFileAsync(CancellationToken cancellationToken = default)
    {
        var window = GetActiveWindow();
        if (window == null)
        {
            return null;
        }

        var storageProvider = window.StorageProvider;
        if (storageProvider == null)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "M3U Playlist Seçin",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("M3U Playlist (*.m3u, *.m3u8)")
                {
                    Patterns = new[] { "*.m3u", "*.m3u8" }
                },
                new FilePickerFileType("Tüm Dosyalar (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        var file = result?.FirstOrDefault();
        if (file == null)
        {
            return null;
        }

        if (file.TryGetLocalPath() is { } localPath)
        {
            return localPath;
        }

        return file.Path.LocalPath;
    }

    private Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible) 
                   ?? desktop.MainWindow;
        }
        return null;
    }
}
