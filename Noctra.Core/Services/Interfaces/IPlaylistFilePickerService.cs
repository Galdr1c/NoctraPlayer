using Noctra.Core.Models;

namespace Noctra.Services.Interfaces;

public interface IPlaylistFilePickerService
{
    /// <summary>
    /// Opens a file picker so the user can choose a local M3U playlist.
    /// When the selected file must be copied into the app's private storage
    /// (e.g. on Android), <paramref name="copyProgress"/> reports byte-level
    /// progress during the copy phase.
    /// </summary>
    Task<string?> PickM3uFileAsync(
        IProgress<FileCopyProgress>? copyProgress = null,
        CancellationToken cancellationToken = default);
}
