using Noctra.Models;

namespace Noctra.ViewModels;

/// <summary>
/// EPG timeline panelindeki tek bir program bloğunun görsel veri modeli.
/// PixelLeft/PixelWidth değerleri LoadEpgPanelAsync sırasında hesaplanır;
/// Avalonia Thickness dönüşümü VideoOverlayView.axaml.cs içindeki converter ile yapılır.
/// </summary>
public class EpgProgramBlock
{
    public EpgProgram Program { get; set; } = null!;

    /// <summary>Timeline canvas içindeki sol kenar konumu (px).</summary>
    public double PixelLeft { get; set; }

    /// <summary>Timeline canvas içindeki genişlik (px), minimum 24px.</summary>
    public double PixelWidth { get; set; }

    /// <summary>Bu program şu an yayında mı?</summary>
    public bool IsCurrentProgram { get; set; }

    /// <summary>Program zaman penceresi dışında kısmen mi? (Kırpılmış sol kenar)</summary>
    public bool IsClippedLeft { get; set; }

    /// <summary>Program geçmişte kaldı (sona erdi).</summary>
    public bool IsPast { get; set; }

    /// <summary>
    /// İlerleme çubuğunun piksel genişliği (IsCurrentProgram=true ise anlamlı).
    /// (Program.ProgressPercentage / 100) * PixelWidth ile hesaplanır.
    /// </summary>
    public double ProgressPixelWidth =>
        IsCurrentProgram ? Program.ProgressPercentage / 100.0 * PixelWidth : 0;

    public double TitleTextWidth => Math.Max(0, PixelWidth - 16);

    public bool HasReadableText => PixelWidth >= 84;
}

/// <summary>
/// EPG panelinde bir satıra karşılık gelen kanal + program veri modeli.
/// </summary>
public class EpgPanelRow
{
    public Channel Channel { get; set; } = null!;
    public double CanvasWidth => PlayerViewModel.EpgCanvasWidth;
    public double NowLineLeft => PlayerViewModel.EpgNowPixelPos;

    /// <summary>Zaman penceresindeki program blokları (sol→sağ sıralı).</summary>
    public List<EpgProgramBlock> Blocks { get; set; } = [];

    /// <summary>Şu an oynatılan kanal mı?</summary>
    public bool IsCurrentChannel { get; set; }

    public bool HasEpgData => Blocks.Count > 0;
}
