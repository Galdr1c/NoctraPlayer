namespace Noctra.Services.Interfaces;

public interface IPlaylistFilePickerService
{
    Task<string?> PickM3uFileAsync(CancellationToken cancellationToken = default);
}
