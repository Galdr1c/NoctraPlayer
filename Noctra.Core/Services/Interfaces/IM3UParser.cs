using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// M3U dosyalarını parse eden servis interface'i
/// </summary>
public interface IM3UParser
{
    /// <summary>
    /// Son parse edilen M3U başlığından tespit edilen EPG URL'i (x-tvg-url).
    /// </summary>
    string? LastDetectedEpgUrl { get; }

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
    /// Dosyadaki kanallari tum listeyi bellekte biriktirmeden sirayla uretir.
    /// </summary>
    IAsyncEnumerable<Channel> ParseFromFileStreamAsync(
        string filePath,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// URL'den M3U parse eder
    /// </summary>
    /// <param name="url">M3U URL</param>
    /// <returns>Parse edilen kanal listesi</returns>
    Task<List<Channel>> ParseFromUrlAsync(string url);

    /// <summary>
    /// URL'deki kanallari yanit govdesini listeye donusturmeden sirayla uretir.
    /// </summary>
    IAsyncEnumerable<Channel> ParseFromUrlStreamAsync(
        string url,
        CancellationToken cancellationToken = default);
}


