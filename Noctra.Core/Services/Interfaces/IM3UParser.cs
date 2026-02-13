using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// M3U dosyalarını parse eden servis interface'i
/// </summary>
public interface IM3UParser
{
    /// <summary>
    /// M3U içeriğini parse eder
    /// </summary>
    /// <param name="content">M3U dosya içeriği</param>
    /// <returns>Parse edilen kanal listesi</returns>
    Task<List<Channel>> ParseAsync(string content);
    
    /// <summary>
    /// Dosyadan M3U parse eder
    /// </summary>
    /// <param name="filePath">Dosya yolu</param>
    /// <returns>Parse edilen kanal listesi</returns>
    Task<List<Channel>> ParseFromFileAsync(string filePath);
    
    /// <summary>
    /// URL'den M3U parse eder
    /// </summary>
    /// <param name="url">M3U URL</param>
    /// <returns>Parse edilen kanal listesi</returns>
    Task<List<Channel>> ParseFromUrlAsync(string url);
}


