using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Noctra.Helpers;

/// <summary>
/// WPF Popup'ın "TopMost" davranışını kaldıran custom Popup.
/// Standart WPF Popup, Win32 seviyesinde WS_EX_TOPMOST ile açılır ve
/// diğer uygulamaların üstüne çıkar. Bu sınıf, Popup açıldığında
/// SetWindowPos(HWND_NOTOPMOST) çağırarak bunu engeller.
/// Video overlay gibi senaryolarda ideal: video üstünde kalır ama
/// diğer uygulamaların üstüne taşmaz.
/// </summary>
public class NonTopmostPopup : Popup
{
    // Win32 Constants
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RemoveTopMost();
    }

    private void RemoveTopMost()
    {
        // Popup'ın Win32 pencere handle'ını bul
        var hwndSource = (HwndSource?)PresentationSource.FromVisual(Child);
        if (hwndSource?.Handle == IntPtr.Zero)
            return;

        // TopMost'u kaldır — video üstünde kalır ama diğer app'lerin üstüne çıkmaz
        SetWindowPos(
            hwndSource.Handle,
            HWND_NOTOPMOST,
            0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }
}
