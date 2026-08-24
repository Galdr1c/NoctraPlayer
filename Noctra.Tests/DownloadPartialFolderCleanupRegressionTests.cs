using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Noctra.Core.Services;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests;

public sealed class DownloadPartialFolderCleanupRegressionTests
{
    [Fact]
    public void StaleFolderCleanup_PreservesSeriesFolderWhilePartialDownloadExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "NoctraPartialCleanup", Guid.NewGuid().ToString("N"));
        var seasonDirectory = Path.Combine(root, "Series", "A Series", "Season 01");
        Directory.CreateDirectory(seasonDirectory);
        var partialPath = Path.Combine(seasonDirectory, "A Series - S01E01.mkv.part");
        File.WriteAllBytes(partialPath, new byte[] { 1, 2, 3 });

        try
        {
            var context = new DownloadTestContext(downloadRoot: root);
            var appPathsField = typeof(MainViewModel).GetField(
                "_appPaths",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(appPathsField);
            appPathsField!.SetValue(context.MainVM, new DesktopAppPathService(root));

            var cleanup = typeof(MainViewModel).GetMethod(
                "CleanupStaleDownloadFolders",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(cleanup);

            cleanup!.Invoke(
                context.MainVM,
                new object[]
                {
                    root,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mkv" }
                });

            Assert.True(File.Exists(partialPath));
            Assert.True(Directory.Exists(Path.Combine(root, "Series", "A Series")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
