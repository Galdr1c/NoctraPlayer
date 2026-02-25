namespace Noctra.Models;

public class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public DateTime ReleaseDate { get; set; }
}
