using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Noctra.Core.Services;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    /// <summary>
    /// Scenario tests for the download-center visibility fix:
    /// the download tabs / empty-state must react to ANY download state
    /// (active, queued or failed) and not only to completed library items.
    /// </summary>
    public class DownloadCenterVisibilityTests
    {
        private static async Task RefreshFromServiceAsync(MainViewModel vm, int profileId)
        {
            var method = typeof(MainViewModel).GetMethod(
                "RefreshDownloadsFromServiceAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (Task)method.Invoke(vm, new object[] { profileId })!;
            await task;
        }

        /// <summary>
        /// Scenario 1: user opens the download center with no download state at all.
        /// Expectation: empty state shown, tabs hidden.
        /// </summary>
        [Fact]
        public async Task NoDownloads_ShowsEmptyState_AndHidesCenterTabs()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.False(ctx.MainVM.HasDownloadedItems);
            Assert.False(ctx.MainVM.HasAnyDownloadState);
            Assert.True(ctx.MainVM.ShowDownloadsEmptyState);
            Assert.Empty(ctx.MainVM.ActiveDownloadItems);
            Assert.Empty(ctx.MainVM.QueuedDownloadItems);
            Assert.Empty(ctx.MainVM.FailedDownloadItems);
        }

        /// <summary>
        /// Scenario 2 (the reported regression): the very first download has
        /// started but has NOT completed, so there are no library items yet.
        /// Expectation: download center must remain visible so the user can
        /// track the active download.
        /// </summary>
        [Fact]
        public async Task FirstDownload_Ongoing_UnlocksDownloadCenterBeforeAnyLibraryItem()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie A",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.False(ctx.MainVM.HasDownloadedItems, "No completed library item exists yet.");
            Assert.True(ctx.MainVM.HasAnyDownloadState, "Active downloads must keep the center visible.");
            Assert.False(ctx.MainVM.ShowDownloadsEmptyState, "Empty state must not appear while a download is running.");
            Assert.Single(ctx.MainVM.ActiveDownloadItems);
            Assert.Single(ctx.MainVM.ActiveDownloadingItems);
            Assert.Empty(ctx.MainVM.QueuedDownloadItems);
            Assert.Empty(ctx.MainVM.FailedDownloadItems);
        }

        /// <summary>
        /// Scenario 3: only queued downloads exist.
        /// Expectation: download center stays visible.
        /// </summary>
        [Fact]
        public async Task OnlyQueuedDownload_KeepsCenterVisible()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Queued,
                DisplayName = "Series episode",
                SourceUrl = "http://media/s1e1.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.True(ctx.MainVM.HasAnyDownloadState);
            Assert.False(ctx.MainVM.ShowDownloadsEmptyState);
            Assert.Single(ctx.MainVM.QueuedDownloadItems);
            Assert.Single(ctx.MainVM.ActiveDownloadItems);
            Assert.Empty(ctx.MainVM.ActiveDownloadingItems);
        }

        /// <summary>
        /// Scenario 4: only a failed download remains.
        /// Expectation: the user must still reach the center to retry/delete.
        /// </summary>
        [Fact]
        public async Task OnlyFailedDownload_KeepsCenterVisible()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Failed,
                ErrorMessage = "Server error",
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.True(ctx.MainVM.HasAnyDownloadState);
            Assert.False(ctx.MainVM.ShowDownloadsEmptyState);
            Assert.Single(ctx.MainVM.FailedDownloadItems);
            Assert.Empty(ctx.MainVM.ActiveDownloadingItems);
            Assert.Empty(ctx.MainVM.QueuedDownloadItems);
        }

        /// <summary>
        /// Scenario 5: completed library content plus an ongoing download.
        /// Both states together must keep the center visible.
        /// </summary>
        [Fact]
        public async Task CompletedLibraryPlusOngoingDownload_KeepsCenterVisible()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;
            ctx.MainVM.TotalDownloadedCount = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.True(ctx.MainVM.HasDownloadedItems);
            Assert.True(ctx.MainVM.HasAnyDownloadState);
            Assert.False(ctx.MainVM.ShowDownloadsEmptyState);
            Assert.Single(ctx.MainVM.ActiveDownloadingItems);
        }

        /// <summary>
        /// Scenario 6: everything is removed.
        /// Expectation: center falls back to the empty state again.
        /// </summary>
        [Fact]
        public async Task AllDownloadsRemoved_ReturnsToEmptyState()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);
            Assert.True(ctx.MainVM.HasAnyDownloadState);

            ctx.DownloadService.MockItems.Clear();
            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.False(ctx.MainVM.HasAnyDownloadState);
            Assert.True(ctx.MainVM.ShowDownloadsEmptyState);
            Assert.Empty(ctx.MainVM.ActiveDownloadItems);
            Assert.Empty(ctx.MainVM.QueuedDownloadItems);
            Assert.Empty(ctx.MainVM.FailedDownloadItems);
        }

        /// <summary>
        /// Scenario 7: state notification flows even when the counts change while
        /// a genuine downloads-changed event fires (no library involved).
        /// </summary>
        [Fact]
        public async Task ServiceDownloadsChanged_RefreshesVisibleState()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.CurrentProfileId = 1;

            var req = new DownloadContentRequest(1, DownloadItemType.Vod, "Movie", "http://media/a.mp4");
            await ctx.DownloadService.QueueDownloadAsync(req);

            Assert.True(ctx.MainVM.HasAnyDownloadState);
            Assert.False(ctx.MainVM.ShowDownloadsEmptyState);

            await ctx.DownloadService.CancelDownloadAsync(1);

            Assert.False(ctx.MainVM.HasAnyDownloadState);
            Assert.True(ctx.MainVM.ShowDownloadsEmptyState);
        }
    }
}