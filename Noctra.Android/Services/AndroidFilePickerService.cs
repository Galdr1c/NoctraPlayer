using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using Noctra.Core.Models;
using Noctra.Core.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidFilePickerService : IPlaylistFilePickerService
{
    public const int M3uFileRequestCode = 4107;

    /// <summary>Buffer size used when copying the selected playlist file into app storage.</summary>
    private const int CopyBufferSize = 81920;

    private static readonly string[] AcceptedMimeTypes =
    {
        "audio/x-mpegurl",
        "application/vnd.apple.mpegurl",
        "application/x-mpegurl",
        "text/plain",
        "application/octet-stream"
    };

    private readonly AndroidActivityProvider _activityProvider;
    private readonly Context _context;
    private readonly IAppPathService _appPaths;
    private readonly object _sync = new();
    private TaskCompletionSource<string?>? _pendingSelection;
    private CancellationTokenRegistration _cancellationRegistration;
    private IProgress<FileCopyProgress>? _copyProgress;

    public AndroidFilePickerService(
        AndroidActivityProvider activityProvider,
        Context context,
        IAppPathService appPaths)
    {
        _activityProvider = activityProvider;
        _context = context;
        _appPaths = appPaths;
    }

    public Task<string?> PickM3uFileAsync(
        IProgress<FileCopyProgress>? copyProgress = null,
        CancellationToken cancellationToken = default)
    {
        var activity = _activityProvider.CurrentActivity
            ?? throw new InvalidOperationException("No active Android activity is available.");

        TaskCompletionSource<string?> completion;
        lock (_sync)
        {
            if (_pendingSelection is not null)
            {
                throw new InvalidOperationException("A playlist file selection is already active.");
            }

            completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingSelection = completion;
            _copyProgress = copyProgress;
            _cancellationRegistration = cancellationToken.Register(
                () => CompleteSelection((string?)null));
        }

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, AcceptedMimeTypes);
        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission |
            ActivityFlags.GrantPersistableUriPermission);

        activity.RunOnUiThread(() =>
        {
            try
            {
                activity.StartActivityForResult(intent, M3uFileRequestCode);
            }
            catch (Exception ex)
            {
                CompleteSelection(ex);
            }
        });

        return completion.Task;
    }

    public bool TryHandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != M3uFileRequestCode)
        {
            return false;
        }

        if (resultCode != Result.Ok || data?.Data is null)
        {
            CompleteSelection((string?)null);
            return true;
        }

        _ = ImportSelectedFileAsync(data);
        return true;
    }

    private async Task ImportSelectedFileAsync(Intent data)
    {
        try
        {
            var uri = data.Data
                ?? throw new InvalidOperationException("The selected document has no URI.");
            var takeFlags = data.Flags &
                (ActivityFlags.GrantReadUriPermission |
                 ActivityFlags.GrantWriteUriPermission);
            try
            {
                _context.ContentResolver?.TakePersistableUriPermission(uri, takeFlags);
            }
            catch (Java.Lang.SecurityException)
            {
                // Some document providers grant one-time access only; the private copy still works.
            }

            var importsDirectory = Path.Combine(_appPaths.UserDataDirectory, "Imports");
            Directory.CreateDirectory(importsDirectory);
            var fileName = GetSafeFileName(uri);
            var destinationPath = CreateUniqueDestinationPath(importsDirectory, fileName);

            var totalBytes = QueryFileSize(uri);

            await using var input = _context.ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException("The selected document could not be opened.");
            await using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous);

            await CopyWithProgressAsync(input, output, totalBytes);

            CompleteSelection(destinationPath);
        }
        catch (Exception ex)
        {
            CompleteSelection(ex);
        }
        finally
        {
            lock (_sync)
            {
                _copyProgress = null;
            }
        }
    }

    private async Task CopyWithProgressAsync(
        Stream input, Stream output, long? totalBytes)
    {
        var buffer = new byte[CopyBufferSize];
        long totalRead = 0;
        int bytesRead;

        IProgress<FileCopyProgress>? progress;
        lock (_sync)
        {
            progress = _copyProgress;
        }

        // Report initial state so the UI can display the copying stage.
        progress?.Report(new FileCopyProgress { TotalBytes = totalBytes, BytesCopied = 0 });

        while ((bytesRead = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
            totalRead += bytesRead;

            progress?.Report(new FileCopyProgress
            {
                TotalBytes = totalBytes,
                BytesCopied = totalRead
            });
        }

        await output.FlushAsync().ConfigureAwait(false);
    }

    private long? QueryFileSize(global::Android.Net.Uri uri)
    {
        try
        {
            using ICursor? cursor = _context.ContentResolver?.Query(
                uri,
                new[] { IOpenableColumns.Size },
                null,
                null,
                null);
            if (cursor is null || !cursor.MoveToFirst())
            {
                return null;
            }

            var sizeIndex = cursor.GetColumnIndex(IOpenableColumns.Size);
            return sizeIndex >= 0 ? cursor.GetLong(sizeIndex) : null;
        }
        catch
        {
            // Some providers may not support size queries; progress will still work
            // with an indeterminate indicator.
            return null;
        }
    }

    private string GetSafeFileName(global::Android.Net.Uri uri)
    {
        var displayName = TryReadDisplayName(uri);
        var fileName = Path.GetFileName(displayName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"playlist-{DateTime.UtcNow:yyyyMMdd-HHmmss}.m3u";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        fileName = new string(fileName
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".m3u", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".m3u";
        }

        return fileName;
    }

    private string? TryReadDisplayName(global::Android.Net.Uri uri)
    {
        using ICursor? cursor = _context.ContentResolver?.Query(
            uri,
            new[] { IOpenableColumns.DisplayName },
            null,
            null,
            null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return null;
        }

        var columnIndex = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return columnIndex >= 0 ? cursor.GetString(columnIndex) : null;
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

    private void CompleteSelection(string? filePath)
    {
        TaskCompletionSource<string?>? completion;
        lock (_sync)
        {
            completion = _pendingSelection;
            _pendingSelection = null;
            _cancellationRegistration.Dispose();
        }

        completion?.TrySetResult(filePath);
    }

    private void CompleteSelection(Exception exception)
    {
        TaskCompletionSource<string?>? completion;
        lock (_sync)
        {
            completion = _pendingSelection;
            _pendingSelection = null;
            _cancellationRegistration.Dispose();
        }

        completion?.TrySetException(exception);
    }
}
