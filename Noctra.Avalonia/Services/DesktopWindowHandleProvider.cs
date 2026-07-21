using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

/// <summary>
/// Microsoft Store modal pencereleri için o anda geçerli olan Avalonia HWND'sini sağlar.
/// Pencere nesnesini constructor'da saklamaz; splash -> profiles -> main/settings geçişlerinde
/// eski veya kapanmış bir handle döndürülmesini önler.
/// </summary>
public sealed class DesktopWindowHandleProvider : IWindowHandleProvider
{
    public IntPtr WindowHandle
    {
        get
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                {
                    return IntPtr.Zero;
                }

                Window? owner = desktop.Windows
                    .Where(window => window.IsVisible)
                    .OrderByDescending(window => window.IsActive)
                    .FirstOrDefault()
                    ?? desktop.MainWindow;

                return owner?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
