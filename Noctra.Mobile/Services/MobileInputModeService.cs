using System;

namespace Noctra.Mobile.Services;

public enum MobileInputMode
{
    Touch,
    Remote
}

public sealed class MobileInputModeService
{
    public MobileInputMode CurrentMode { get; private set; } = MobileInputMode.Touch;

    public bool IsRemote => CurrentMode == MobileInputMode.Remote;

    public event EventHandler<MobileInputMode>? ModeChanged;

    public void SetMode(MobileInputMode mode)
    {
        if (CurrentMode == mode)
        {
            return;
        }

        CurrentMode = mode;
        ModeChanged?.Invoke(this, mode);
    }
}
