using System;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public sealed class PictureInPictureModeChangedEventArgs : EventArgs
{
    public PictureInPictureModeChangedEventArgs(bool isInPictureInPictureMode)
    {
        IsInPictureInPictureMode = isInPictureInPictureMode;
    }

    public bool IsInPictureInPictureMode { get; }
}

public sealed class PictureInPicturePlaybackState
{
    public bool CanEnterPictureInPicture { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsLiveContent { get; init; }
    public bool IsSeriesContent { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
}

public interface IPictureInPictureService
{
    event EventHandler<PictureInPictureModeChangedEventArgs>? PictureInPictureModeChanged;

    bool IsSupported { get; }

    bool IsInPictureInPictureMode { get; }

    Task<bool> EnterPictureInPictureAsync();

    Task<bool> TryEnterAutoPictureInPictureAsync();

    void UpdatePictureInPictureState(PictureInPicturePlaybackState state);
}
