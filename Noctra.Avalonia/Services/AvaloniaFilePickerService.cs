using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Noctra.Core.Models;
using Noctra.Core.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class AvaloniaFilePickerService : IPlaylistFilePickerService
{
    private const int CopyBufferSize = 81920;

    private readonly IAppPathService _appPaths;

    public AvaloniaFilePickerService(IAppPathService appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task<string?> PickM3uFileAsync(
        IProgress<FileCopyProgress>? copyProgress = null,
        CancellationToken cancellationToken = default)
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

        // If the file is already in our app storage, use it directly.
        var sourcePath = file.TryGetLocalPath() ?? file.Path.LocalPath;
        var importsDirectory = Path.Combine(_appPaths.UserDataDirectory, "Imports");
        if (sourcePath.StartsWith(importsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        // Copy the file into app-private storage for consistency with Android.
        Directory.CreateDirectory(importsDirectory);
        var fileName = GetSafeFileName(sourcePath);
        var destinationPath = CreateUniqueDestinationPath(importsDirectory, fileName);

        long? totalBytes = null;
        try
        {
            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Exists)
            {
                totalBytes = fileInfo.Length;
            }
        }
        catch
        {
            // Ignore — progress will show indeterminate.
        }

        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous);

        await CopyWithProgressAsync(input, output, totalBytes, copyProgress, cancellationToken);

        return destinationPath;
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        long? totalBytes,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        long totalRead = 0;
        int bytesRead;

        progress?.Report(new FileCopyProgress { TotalBytes = totalBytes, BytesCopied = 0 });

        while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            progress?.Report(new FileCopyProgress
            {
                TotalBytes = totalBytes,
                BytesCopied = totalRead
            });
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetSafeFileName(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"playlist-{DateTime.UtcNow:yyyyMMdd-HHmmss}.m3u";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        fileName = new string(fileName
            .Select(c => invalidCharacters.Contains(c) ? '_' : c)
            .ToArray());

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".m3u", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".m3u";
        }

        return fileName;
    }

    private static string CreateUniqueDestinationPath(string directory, string fileName)
    {
        var destinationPath = Path.Combine(directory, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; ; suffix++)
        {
            destinationPath = Path.Combine(directory, $"{name}-{suffix}{extension}");
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }
        }
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
