using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface IVideoSurfaceService
{
    Task ShowAsync();
    void Hide();

    /// <summary>
    /// Video yüzeyini ana içerik kökü (content-root) yerel piksel koordinatlarına yerleştirir.
    /// EPG split görünümünde videoyu üst bölgeye küçültmek için kullanılır.
    /// width veya height &lt;= 0 verilirse tam ekran (MatchParent) moduna döner.
    /// </summary>
    void SetBounds(int x, int y, int width, int height);

    /// <summary>
    /// Applies a user-driven transform on top of the platform video layout.
    /// Pan values are expressed in native view pixels.
    /// </summary>
    void SetInteractionTransform(float zoom, float panX, float panY);

    /// <summary>
    /// Clears the user-driven transform and returns the video surface to its layout transform.
    /// </summary>
    void ResetInteractionTransform();
}
