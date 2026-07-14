namespace Noctra.Models;

public sealed record PlaybackMediaMetadata(
    string Title,
    string? Subtitle = null,
    string? ArtworkUrl = null);
