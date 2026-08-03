using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Noctra.Core.Services;
using Noctra.Data;
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

        private static IDbContextFactory<AppDbContext> CreateSqliteFactory()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"Noctra-TabTests-{Guid.NewGuid():N}.db")}")
                .Options;
            using (var db = new AppDbContext(options))
            {
                db.Database.EnsureCreated();
            }

            return new SimpleDbContextFactory(options);
        }

        /// <summary>Isolated, empty download root so the DB-driven library discovery
        /// never picks up real files from the machine's download folder.</summary>
        private static string CreateEmptyDownloadRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), $"Noctra-TabTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return root;
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

        /// <summary>
        /// Scenario 8: user starts a download then opens the Downloads page.
        /// Expectation: the Download Center tab (index 1) is selected, not Library.
        /// </summary>
        [Fact]
        public async Task NavigateToDownloads_WithActiveDownload_OpensDownloadCenterTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);
            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);

            Assert.True(ctx.MainVM.IsDownloadCenterVisible);
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
        }

        /// <summary>
        /// Scenario 9: user has a failed download and opens Downloads.
        /// Expectation: the Download Center tab is selected so the error is visible.
        /// </summary>
        [Fact]
        public async Task NavigateToDownloads_WithFailedDownload_OpensDownloadCenterTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
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
            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);

            Assert.True(ctx.MainVM.IsDownloadCenterVisible);
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
        }

        /// <summary>
        /// Scenario 10: user opens Downloads with no active or failed downloads.
        /// Expectation: the Library tab (index 0) is selected.
        /// </summary>
        [Fact]
        public async Task NavigateToDownloads_WithoutActiveOrFailed_OpensLibraryTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;

            await RefreshFromServiceAsync(ctx.MainVM, 1);
            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);

            Assert.False(ctx.MainVM.IsDownloadCenterVisible);
            Assert.Equal(0, ctx.MainVM.DownloadTabIndex);
        }

        /// <summary>
        /// Scenario 11 (cold start): the Downloads page is opened before the download
        /// state has been loaded; once the refresh completes, the Download Center tab
        /// is selected automatically while the library is empty.
        /// </summary>
        [Fact]
        public async Task ColdStart_OpenDownloadsBeforeLoaded_OngoingDownload_SelectsCenterTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;

            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);
            Assert.False(ctx.MainVM.IsDownloadCenterVisible, "Nothing loaded yet, Library default.");

            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.True(ctx.MainVM.HasAnyDownloadState);
            Assert.True(ctx.MainVM.IsDownloadCenterVisible);
        }

        /// <summary>
        /// Scenario 12: the TabControl binds SelectedIndex to DownloadTabIndex.
        /// Programmatically switching to the Download Center must raise a
        /// DownloadTabIndex change notification, otherwise the UI stays on Library.
        /// </summary>
        [Fact]
        public void TogglingIsDownloadCenterVisible_RaisesDownloadTabIndexNotification()
        {
            var ctx = new DownloadTestContext();
            var notifications = new List<string?>();
            ctx.MainVM.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

            ctx.MainVM.IsDownloadCenterVisible = true;

            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
            Assert.Contains(nameof(MainViewModel.DownloadTabIndex), notifications);

            notifications.Clear();
            ctx.MainVM.IsDownloadCenterVisible = false;

            Assert.Equal(0, ctx.MainVM.DownloadTabIndex);
            Assert.Contains(nameof(MainViewModel.DownloadTabIndex), notifications);
        }

        /// <summary>
        /// Scenario 13: full entry flow — an ongoing download exists, the user
        /// navigates to Downloads, lands on the Download Center tab, leaves, then
        /// re-enters; the Center must still be selected on re-entry.
        /// </summary>
        [Fact]
        public async Task ReenteringDownloads_WithOngoingDownload_KeepsCenterTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);

            ctx.MainVM.NavigateCommand.Execute(AppView.Home);
            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);

            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
            Assert.True(ctx.MainVM.IsDownloadCenterVisible);
        }

        /// <summary>
        /// Scenario 14 (device reproduction): the user has library items AND a
        /// paused/active download, but the download state is still loading when
        /// the Downloads page is opened. Once the state arrives, the Center tab
        /// must be selected even though a library already exists.
        /// </summary>
        [Fact]
        public async Task LibraryExists_ButStateLoadsLate_ActiveDownload_SelectsCenterTab()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;
            ctx.MainVM.TotalDownloadedCount = 1;

            // A real library file: whichever refresh wins the race, the library
            // discovery deterministically reports one downloaded item.
            File.WriteAllBytes(Path.Combine(ctx.DownloadRoot, "already-downloaded.mp4"), new byte[16]);

            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);
            Assert.False(ctx.MainVM.IsDownloadCenterVisible, "State not loaded yet, Library default.");

            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Paused,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.True(ctx.MainVM.HasDownloadedItems);
            Assert.True(ctx.MainVM.IsDownloadCenterVisible, $"Center must be selected once the active download is known. View={ctx.MainVM.ActiveView}, Active={ctx.MainVM.ActiveDownloadItems.Count}, Failed={ctx.MainVM.FailedDownloadItems.Count}");
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
        }

        /// <summary>
        /// Scenario 15: the user explicitly picks the Library tab while downloads
        /// are active; a later state refresh must NOT yank them back to Center.
        /// </summary>
        [Fact]
        public async Task ManualLibrarySelection_IsNotOverriddenByRefresh()
        {
            var ctx = new DownloadTestContext(CreateSqliteFactory(), CreateEmptyDownloadRoot());
            ctx.MainVM.CurrentProfileId = 1;
            ctx.DownloadService.MockItems.Add(new DownloadItem
            {
                Id = 1,
                ProfileId = 1,
                Status = DownloadStatus.Downloading,
                DisplayName = "Movie film",
                SourceUrl = "http://media/a.mp4"
            });

            await RefreshFromServiceAsync(ctx.MainVM, 1);
            ctx.MainVM.NavigateCommand.Execute(AppView.Downloads);
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);

            ctx.MainVM.DownloadTabIndex = 0;
            Assert.False(ctx.MainVM.IsDownloadCenterVisible);

            await RefreshFromServiceAsync(ctx.MainVM, 1);

            Assert.False(ctx.MainVM.IsDownloadCenterVisible, "User chose Library; refresh must not override.");
            Assert.Equal(0, ctx.MainVM.DownloadTabIndex);
        }

        /// <summary>
        /// Scenario 16: the automatic default survives the TwoWay writeback —
        /// setting the Center via the VM is not mistaken for a user tap.
        /// </summary>
        [Fact]
        public void ProgrammaticCenterSelection_IsNotCountedAsUserTap()
        {
            var ctx = new DownloadTestContext();
            ctx.MainVM.IsDownloadCenterVisible = true;

            ctx.MainVM.DownloadTabIndex = 1;

            Assert.True(ctx.MainVM.IsDownloadCenterVisible);
            Assert.Equal(1, ctx.MainVM.DownloadTabIndex);
        }
    }
}