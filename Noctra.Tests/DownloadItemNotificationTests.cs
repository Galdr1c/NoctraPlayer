using Noctra.Models;

namespace Noctra.Tests;

public sealed class DownloadItemNotificationTests
{
    [Fact]
    public void ProgressChangesNotifyOnlyTheDerivedBindingsThatNeedRefresh()
    {
        var item = new DownloadItem { BytesTotal = 1000 };
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.BytesDownloaded = 250;
        item.SpeedBytesPerSecond = 512;
        item.EstimatedSecondsRemaining = 12;

        Assert.Contains(nameof(DownloadItem.BytesDownloaded), changed);
        Assert.Contains(nameof(DownloadItem.ProgressPercent), changed);
        Assert.Contains(nameof(DownloadItem.ProgressText), changed);
        Assert.Contains(nameof(DownloadItem.SizeText), changed);
        Assert.Contains(nameof(DownloadItem.SpeedText), changed);
        Assert.Contains(nameof(DownloadItem.EtaText), changed);
    }

    [Fact]
    public void StatusChangesNotifyStateAndLocalizedStatusBindings()
    {
        var item = new DownloadItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.Status = DownloadStatus.Paused;

        Assert.Contains(string.Empty, changed);
        Assert.Contains(nameof(DownloadItem.IsActive), changed);
        Assert.Contains(nameof(DownloadItem.IsPaused), changed);
        Assert.Contains(nameof(DownloadItem.StatusText), changed);
    }
}
