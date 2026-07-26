namespace Noctra.Tests;

public sealed class VideoOverlayInputSurfaceTests
{
    [Fact]
    public void MouseCaptureLayer_UsesNonZeroAlphaBackground()
    {
        var mainWindow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Noctra.Avalonia",
            "MainWindow.axaml"));

        var marker = "x:Name=\"MouseCaptureLayer\"";
        var markerIndex = mainWindow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "MouseCaptureLayer was not found.");

        var tagEnd = mainWindow.IndexOf("/>", markerIndex, StringComparison.Ordinal);
        Assert.True(tagEnd >= 0, "MouseCaptureLayer tag was not closed.");

        var mouseCaptureTag = mainWindow[markerIndex..tagEnd];

        Assert.Contains("Background=\"#01000000\"", mouseCaptureTag, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", mouseCaptureTag, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_UsesNonZeroAlphaBackground()
    {
        var memoryVideoView = LoadProjectFile(
            "Noctra.Avalonia",
            "Controls",
            "MemoryVideoView.cs");

        Assert.Contains("Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))",
            memoryVideoView, StringComparison.Ordinal);
        Assert.DoesNotContain("Background = Brushes.Transparent",
            memoryVideoView, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindow_RestoresNativeZOrderWhenOwnerReactivates()
    {
        var memoryVideoView = LoadProjectFile(
            "Noctra.Avalonia",
            "Controls",
            "MemoryVideoView.cs");

        Assert.Contains("_rootWindow.Activated += Root_Activated",
            memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("_rootWindow.Activated -= Root_Activated",
            memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("private void Root_Activated", memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("RestoreOverlayZOrder();", memoryVideoView, StringComparison.Ordinal);
        Assert.Contains("SetWindowPos(", memoryVideoView, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayer_DoesNotPaintOpaqueBackgroundOverNativeVideoSurface()
    {
        var mainView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MainView.axaml");
        var mainViewCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs");
        var playerView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml");

        var mainViewRoot = ExtractStartTag(mainView, "x:Class=\"Noctra.Mobile.Views.MainView\"");
        var shellLayer = ExtractStartTag(mainView, "x:Name=\"ShellLayer\"");
        var playerHost = ExtractStartTag(mainView, "x:Name=\"PlayerHost\"");
        var playerRootGrid = ExtractStartTag(playerView, "<Grid ");

        Assert.Contains("Background=\"Transparent\"", mainViewRoot, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource Bg0Brush}\"", shellLayer, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", playerHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Black\"", playerHost, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", playerRootGrid, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerSurfaceBrush", playerRootGrid, StringComparison.Ordinal);
        Assert.Contains("ShellLayer.IsVisible = !isPlayerVisible;",
            mainViewCode, StringComparison.Ordinal);
        Assert.Contains("topLevel.TransparencyLevelHint =",
            mainViewCode, StringComparison.Ordinal);
        Assert.Contains("WindowTransparencyLevel.Transparent",
            mainViewCode, StringComparison.Ordinal);
        Assert.Contains("WindowTransparencyLevel.None",
            mainViewCode, StringComparison.Ordinal);
        Assert.Contains("topLevel.Background = Brushes.Transparent;",
            mainViewCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidAvaloniaSurface_ComposesTransparentPlayerPixelsAboveNativeVideo()
    {
        var mainActivity = LoadProjectFile(
            "Noctra.Android",
            "MainActivity.cs");
        var videoSurfaceService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoSurfaceService.cs");

        Assert.Contains("ConfigureAvaloniaOverlaySurface();",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains("surfaceView.SetZOrderOnTop(true);",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains("surfaceView.Holder.SetFormat(Format.Translucent);",
            mainActivity, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "throw new InvalidOperationException(\"Avalonia rendering surface is unavailable.\")",
            mainActivity,
            StringComparison.Ordinal);
        Assert.Contains("ConfigureAvaloniaOverlaySurface(remainingAttempts - 1)",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains(
            "SetAvaloniaSurfaceVisibilityForPictureInPicture(isInPictureInPictureMode);",
            mainActivity,
            StringComparison.Ordinal);
        Assert.Contains("surfaceView.Visibility = isInPictureInPictureMode",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains("? ViewStates.Gone", mainActivity, StringComparison.Ordinal);
        Assert.Contains(": ViewStates.Visible", mainActivity, StringComparison.Ordinal);
        Assert.Contains("content.AddView(\n            _textureView,\n            content.ChildCount,",
            videoSurfaceService.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("content.AddView(\n            _textureView,\n            0,",
            videoSurfaceService.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("_textureView.SetBackgroundColor(",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("_backdropView = new View(activity);",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("_backdropView.SetBackgroundColor(Color.Black);",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("content.AddView(\n            _backdropView,\n            content.ChildCount,",
            videoSurfaceService.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("RemoveView(_backdropView);",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("activity?.IsInPictureInPictureMode == true",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("_boundsW = -1;",
            videoSurfaceService, StringComparison.Ordinal);
        Assert.Contains("_boundsH = -1;",
            videoSurfaceService, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTopOverlay_ShowsResolutionAndCodecWithoutPersistentConnectionStatus()
    {
        var topOverlay = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerTopOverlay.axaml");

        Assert.DoesNotContain("Text=\"{Binding ConnectionStatus}\"",
            topOverlay, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding QualityResolutionText}\"",
            topOverlay, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding QualityVideoCodecText}\"",
            topOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidPictureInPicture_PreparesVideoCompositionBeforeEntering()
    {
        var pictureInPictureService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidPictureInPictureService.cs");
        var mainActivity = LoadProjectFile(
            "Noctra.Android",
            "MainActivity.cs");

        var prepareCall = pictureInPictureService.IndexOf(
            "await mainActivity.PrepareVideoSurfaceForPictureInPictureAsync()",
            StringComparison.Ordinal);
        var enterCall = pictureInPictureService.IndexOf(
            "activity.EnterPictureInPictureMode(parameters)",
            StringComparison.Ordinal);

        Assert.True(prepareCall >= 0, "PiP must prepare the native video composition.");
        Assert.True(enterCall > prepareCall, "PiP composition must be prepared before entering PiP.");
        Assert.Contains("RestoreAvaloniaSurfaceAfterFailedPictureInPictureEntry",
            pictureInPictureService, StringComparison.Ordinal);
        Assert.Contains("PrepareVideoSurfaceForPictureInPictureAsync",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains("decorView.Post(",
            mainActivity, StringComparison.Ordinal);
        Assert.Contains("ConfigChanges.ScreenLayout",
            mainActivity, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidPlayer_PublishesPlaybackPositionWhileMediaIsPlaying()
    {
        var playerService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs");

        Assert.Contains("StartPositionUpdates();",
            playerService, StringComparison.Ordinal);
        Assert.Contains("StopPositionUpdates();",
            playerService, StringComparison.Ordinal);
        Assert.Contains("_positionUpdateTimer.Change(0, PositionUpdateIntervalMs);",
            playerService, StringComparison.Ordinal);
        Assert.Contains("PositionChanged?.Invoke(this, _currentTimeMs / 1000d);",
            playerService, StringComparison.Ordinal);
        Assert.Contains("BufferingChanged?.Invoke(_service, 100f);",
            playerService, StringComparison.Ordinal);
    }

    private static string ExtractStartTag(string contents, string marker)
    {
        var markerIndex = contents.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Marker '{marker}' was not found.");

        var tagStart = contents.LastIndexOf('<', markerIndex);
        var tagEnd = contents.IndexOf('>', markerIndex);
        Assert.True(tagStart >= 0 && tagEnd > tagStart, $"Tag for marker '{marker}' was not closed.");

        return contents[tagStart..(tagEnd + 1)];
    }

    private static string LoadProjectFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine(
            new[] { FindRepositoryRoot() }.Concat(pathParts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
