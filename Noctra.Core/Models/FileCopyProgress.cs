namespace Noctra.Core.Models;

/// <summary>
/// Progress data reported while copying a selected playlist file
/// from the system document provider to the app's private storage.
/// </summary>
public sealed class FileCopyProgress
{
    /// <summary>Total bytes of the source file, if known from the document provider.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Number of bytes copied so far.</summary>
    public long BytesCopied { get; init; }

    /// <summary>Percentage of completion (0–100) when <see cref="TotalBytes"/> is available.</summary>
    public int PercentComplete
    {
        get
        {
            if (TotalBytes is { } total && total > 0)
            {
                return Math.Min(100, (int)(BytesCopied * 100 / total));
            }
            return -1;
        }
    }
}
