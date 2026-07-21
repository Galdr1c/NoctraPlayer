using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Avalonia desktop uygulaması için pencere handle'ı sağlayıcı.
/// Microsoft Store modal dialog'unu ana pencereye bağlamak için kullanılır.
/// </summary>
public sealed class DesktopWindowHandleProvider : IWindowHandleProvider
{
    private readonly Window _mainWindow;

    public DesktopWindowHandleProvider(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    /// <summary>
    /// Ana pencerenin platform handle'ı (HWND).
    /// Avalonia'da TryGetPlatformHandle() ile alınır.
    /// </summary>
    public IntPtr WindowHandle
    {
        get
        {
            try
            {
                var platformHandle = _mainWindow.TryGetPlatformHandle();
                return platformHandle?.Handle ?? IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
