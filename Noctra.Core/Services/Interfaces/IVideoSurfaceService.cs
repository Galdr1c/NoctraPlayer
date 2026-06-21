using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface IVideoSurfaceService
{
    Task ShowAsync();
    void Hide();

    /// <summary>
    /// Video yüzeyini ekran (piksel) koordinatlarına yerleştirir.
    /// EPG split görünümünde videoyu üst bölgeye küçültmek için kullanılır.
    /// width veya height &lt;= 0 verilirse tam ekran (MatchParent) moduna döner.
    /// </summary>
    void SetBounds(int x, int y, int width, int height);
}
