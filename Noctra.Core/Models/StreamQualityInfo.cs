namespace Noctra.Models;

/// <summary>
/// Stream kalite bilgisi
/// </summary>
public class StreamQualityInfo
{
    // Video
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public int VideoBitrate { get; set; }
    public string VideoCodec { get; set; } = string.Empty;

    // Audio
    public int AudioBitrate { get; set; }
    public int AudioChannels { get; set; }
    public string AudioCodec { get; set; } = string.Empty;

    /// <summary>
    /// Kullanıcıya gösterilecek çözünürlük etiketi (örn: "1080p60")
    /// </summary>
    public string ResolutionLabel
    {
        get
        {
            if (Height <= 0) return "Bilinmiyor";

            var label = Height switch
            {
                >= 2160 => "4K",
                >= 1440 => "1440p",
                >= 1080 => "1080p",
                >= 720 => "720p",
                >= 480 => "480p",
                >= 360 => "360p",
                _ => $"{Height}p"
            };

            if (Fps > 30) label += Fps.ToString();
            return label;
        }
    }

    /// <summary>
    /// Kalite seviyesi puanı (sıralama için)
    /// </summary>
    public string QualityTier => Height switch
    {
        >= 2160 => "Ultra HD",
        >= 1080 => "Full HD",
        >= 720 => "HD",
        _ => "SD"
    };

    /// <summary>
    /// Detaylı bilgi (codec + bitrate)
    /// </summary>
    public string DetailLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(VideoCodec)) parts.Add(VideoCodec.ToUpperInvariant());
            if (VideoBitrate > 0) parts.Add($"{VideoBitrate / 1000.0:F1} Mbps");
            return parts.Count > 0 ? string.Join(" • ", parts) : "";
        }
    }

    /// <summary>
    /// Ses detay bilgisi
    /// </summary>
    public string AudioDetailLabel
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(AudioCodec)) parts.Add(AudioCodec.ToUpperInvariant());
            if (AudioChannels > 0)
            {
                var channelLabel = AudioChannels switch
                {
                    1 => "Mono",
                    2 => "Stereo",
                    6 => "5.1",
                    8 => "7.1",
                    _ => $"{AudioChannels}ch"
                };
                parts.Add(channelLabel);
            }
            if (AudioBitrate > 0) parts.Add($"{AudioBitrate} kbps");
            return parts.Count > 0 ? string.Join(" • ", parts) : "";
        }
    }
}


