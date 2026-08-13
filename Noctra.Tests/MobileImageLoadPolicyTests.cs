using Noctra.Mobile.Services;
using Avalonia.Controls;
using Noctra.Mobile.Controls;

namespace Noctra.Tests;

public sealed class MobileImageLoadPolicyTests
{
    [Fact]
    public void InactiveSurface_IsInheritedByImageCreatedAfterDeactivation()
    {
        var surface = new Grid();
        RemoteImage.SetDescendantLoadsActive(surface, false);

        var image = new RemoteImage();
        surface.Children.Add(image);

        Assert.False(image.GetValue(RemoteImage.SurfaceLoadsActiveProperty));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void CanStart_RequiresEveryPersistentAndVisualCondition(
        bool isForeground,
        bool isSurfaceActive,
        bool isAttached,
        bool isVisible,
        bool expected)
    {
        Assert.Equal(
            expected,
            MobileImageLoadPolicy.CanStart(
                isForeground,
                isSurfaceActive,
                isAttached,
                isVisible));
    }

    [Fact]
    public void RemoteImage_UsesLeasedResourcesInsteadOfRawBitmapCacheEntries()
    {
        var source = RemoteImageSource();

        Assert.Contains(
            "ByteBudgetLruCache<string, SharedImageResource<Bitmap>>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("SharedImageLease<Bitmap>? _sourceLease", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ByteBudgetLruCache<string, Bitmap>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteImage_PublishesOnlyWhenConsumerClaimsAndReleasesStaleLease()
    {
        var source = RemoteImageSource();

        Assert.Contains("TryPublishToCache", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseProducerIfNotPublished", source, StringComparison.Ordinal);
        Assert.Contains("lease.Dispose();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddToCache(cacheKey, bitmap);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteImage_DetachAndInactiveSurfaceClearVisibleOwnership()
    {
        var source = RemoteImageSource();
        var detachStart = source.IndexOf(
            "protected override void OnDetachedFromVisualTree",
            StringComparison.Ordinal);
        var detachEnd = source.IndexOf(
            "internal static void SetDescendantLoadsActive",
            detachStart,
            StringComparison.Ordinal);
        var inactiveStart = source.IndexOf(
            "private void SetSurfaceLoadsActive",
            StringComparison.Ordinal);
        var inactiveEnd = source.IndexOf(
            "private void StartImageLoad",
            inactiveStart,
            StringComparison.Ordinal);

        Assert.True(detachStart >= 0 && detachEnd > detachStart);
        Assert.True(inactiveStart >= 0 && inactiveEnd > inactiveStart);
        Assert.Contains(
            "ClearSourceAndReleaseLease",
            source[detachStart..detachEnd],
            StringComparison.Ordinal);
        Assert.Contains(
            "ClearSourceAndReleaseLease",
            source[inactiveStart..inactiveEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceMutationGeneration_RejectsQueuedOldClearAfterNewerLeaseApply()
    {
        var state = new MobileImageSourceMutationState();
        var queuedOldClear = state.BeginMutation();
        var newerLeaseApply = state.BeginMutation();

        Assert.True(state.IsCurrent(newerLeaseApply));
        Assert.False(state.IsCurrent(queuedOldClear));
    }

    [Fact]
    public void RemoteImage_SourceMutationsCaptureAndValidateGenerationBeforeUiCommit()
    {
        var source = RemoteImageSource();

        Assert.True(
            source.Split("_sourceMutationState.BeginMutation()", StringSplitOptions.None).Length - 1 >= 4);
        Assert.Equal(
            4,
            source.Split("_sourceMutationState.IsCurrent(sourceMutation)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void RemoteImage_SameKeyPreserveInvalidatesOlderQueuedSourceMutation()
    {
        var source = RemoteImageSource();
        var start = source.IndexOf("private void StartImageLoad()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LoadAndApplyAsync", start, StringComparison.Ordinal);
        var method = source[start..end];
        var newIntent = method.IndexOf("_sourceMutationState.BeginMutation()", StringComparison.Ordinal);
        var sameKeyPreserve = method.IndexOf("_sourceLease is not null", StringComparison.Ordinal);

        Assert.True(newIntent >= 0 && newIntent < sameKeyPreserve);
    }

    private static string RemoteImageSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory.FullName,
            "Noctra.Mobile",
            "Controls",
            "MobileRemoteImage.cs"));
    }
}
