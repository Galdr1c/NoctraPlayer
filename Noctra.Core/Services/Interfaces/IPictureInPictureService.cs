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

public interface IPictureInPictureService
{
    event EventHandler<PictureInPictureModeChangedEventArgs>? PictureInPictureModeChanged;

    bool IsSupported { get; }

    Task<bool> EnterPictureInPictureAsync();
}
