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
    public void MobilePlayerTopOverlay_HasPiPAndLockButtons()
    {
        var topOverlay = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerTopOverlay.axaml");

        Assert.Contains("EnterPiPCommand",
            topOverlay, StringComparison.Ordinal);
        Assert.Contains("ToggleLockCommand",
            topOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("QualityResolutionText",
            topOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("QualityVideoCodecText",
            topOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentChannel.Name",
            topOverlay, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentProgram.Title",
            topOverlay, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTopOverlay_ExposesAccessibleNamesForEveryButton()
    {
        var topOverlay = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerTopOverlay.axaml");
        var playerView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml");

        foreach (var command in new[]
                 {
                     "ClosePlayerCommand",
                     "EnterPiPCommand",
                     "ToggleLockCommand"
                 })
        {
            var button = ExtractStartTag(
                topOverlay,
                $"Command=\"{{Binding {command}}}\"");
            Assert.Contains(
                "AutomationProperties.Name=",
                button,
                StringComparison.Ordinal);
        }

        var lockButton = ExtractStartTag(
            topOverlay,
            "Command=\"{Binding ToggleLockCommand}\"");
        Assert.Contains(
            "ToolTip.Tip=\"{Binding LockAccessibilityName}\"",
            lockButton,
            StringComparison.Ordinal);

        var lockIndicator = ExtractStartTag(
            playerView,
            "x:Name=\"LockIndicator\"");
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding LockAccessibilityName}\"",
            lockIndicator,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTransportBar_HasContentInfoAndLiveBadgeNextToTime()
    {
        var transportBar = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml");

        Assert.Contains("IsLiveContent",
            transportBar, StringComparison.Ordinal);
        Assert.Contains("PositionText",
            transportBar, StringComparison.Ordinal);
        Assert.Contains("DurationText",
            transportBar, StringComparison.Ordinal);
        Assert.Contains("CurrentChannel.Name",
            transportBar, StringComparison.Ordinal);
        Assert.Contains("OverlaySecondaryText",
            transportBar, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTransportBar_ExposesAccessibleNamesForEveryAction()
    {
        var transportBar = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTransportBar.axaml");

        foreach (var command in new[]
                 {
                     "SkipBackwardCommand",
                     "PlayPreviousLiveChannelCommand",
                     "PlayPauseCommand",
                     "SkipForwardCommand",
                     "PlayNextLiveChannelCommand",
                     "ToggleMuteCommand",
                     "GoToLiveCommand",
                     "ToggleLiveFavoriteCommand",
                     "ToggleEpgPanelCommand",
                     "OpenActionsPanelCommand"
                 })
        {
            var button = ExtractStartTag(
                transportBar,
                $"Command=\"{{Binding {command}}}\"");
            Assert.Contains(
                "AutomationProperties.Name=",
                button,
                StringComparison.Ordinal);
        }

        var moreButton = ExtractStartTag(
            transportBar,
            "Command=\"{Binding OpenActionsPanelCommand}\"");
        Assert.Contains(
            "ToolTip.Tip=\"{loc:Translate Mobile.Nav.More}\"",
            moreButton,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{loc:Translate Mobile.Nav.More}\"",
            moreButton,
            StringComparison.Ordinal);
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
    public void MobilePlayer_PictureInPictureAllowsPausedLoadedMedia()
    {
        var mainView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MainView.axaml.cs");

        Assert.Contains(
            "(vm.IsPlaying || vm.VideoPlayerService.HasLoadedMedia)",
            mainView,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "vm.CurrentChannel is not null && vm.IsPlaying",
            mainView,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerLockIndicator_UsesSingleTapCommandAndIconOnly()
    {
        var playerView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml");
        var playerViewCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs");

        var lockIndicator = ExtractStartTag(
            playerView,
            "x:Name=\"LockIndicator\"");

        Assert.Contains(
            "Command=\"{Binding ToggleLockCommand}\"",
            lockIndicator,
            StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"100\"", lockIndicator, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"56\"", lockIndicator, StringComparison.Ordinal);
        Assert.DoesNotContain("Player.Mobile.Locked", playerView, StringComparison.Ordinal);
        Assert.DoesNotContain("OnLockIndicatorPressed", playerViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OnLockIndicatorReleased", playerViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_lockPressTimer", playerViewCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidPictureInPicture_AutoEnterKeepsManualFallbackOnModernAndroid()
    {
        var pictureInPictureService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidPictureInPictureService.cs");
        var autoEnterMethod = ExtractMethodBody(
            pictureInPictureService,
            "public Task<bool> TryEnterAutoPictureInPictureAsync()");

        Assert.DoesNotContain(
            "Build.VERSION.SdkInt >= BuildVersionCodes.S",
            autoEnterMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (IsInPictureInPictureMode)",
            autoEnterMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "return EnterPictureInPictureAsync();",
            autoEnterMethod,
            StringComparison.Ordinal);
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

    [Fact]
    public void AndroidPlayer_ContinuesBufferedPositionTelemetryWhileLoadedMediaIsPaused()
    {
        var playerService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs");

        var queueUpdate = ExtractMethodBody(
            playerService,
            "private void QueuePositionUpdate()");
        var publishUpdate = ExtractMethodBody(
            playerService,
            "private void PublishPlaybackPosition()");
        var playingChanged = ExtractMethodBody(
            playerService,
            "public void OnIsPlayingChanged(bool isPlaying)");

        Assert.Contains("!_hasLoadedMedia", queueUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("!_isPlaying", queueUpdate, StringComparison.Ordinal);
        Assert.Contains("!_hasLoadedMedia", publishUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain("!_isPlaying", publishUpdate, StringComparison.Ordinal);
        Assert.Contains(
            "_service.UpdatePositionPollingForLoadedMedia();",
            playingChanged,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTimeline_UsesProtectedTouchTargetAndBufferedLayer()
    {
        var timeline = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTimeline.axaml");
        var timelineCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTimeline.axaml.cs");

        var rootGrid = ExtractStartTag(timeline, "x:Name=\"RootGrid\"");
        var thumb = ExtractStartTag(timeline, "x:Name=\"Thumb\"");

        Assert.Contains("Height=\"40\"", rootGrid, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", rootGrid, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TrackGrid\"", timeline, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BufferBar\"", timeline, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.45\"", timeline, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding TimelineAccessibilityName}\"",
            timeline,
            StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"BufferEndMarker\"", timeline, StringComparison.Ordinal);
        Assert.Contains("Width=\"8\"", thumb, StringComparison.Ordinal);
        Assert.Contains("Height=\"8\"", thumb, StringComparison.Ordinal);
        Assert.Contains("nameof(PlayerViewModel.BufferedPosition)",
            timelineCode, StringComparison.Ordinal);
        Assert.DoesNotContain("BufferEndMarker", timelineCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTimeline_PreviewsDuringDragAndCommitsSeekOnlyAfterDrag()
    {
        var timelineCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTimeline.axaml.cs");

        var pointerMoved = ExtractMethodBody(timelineCode, "private void OnPointerMoved");
        var pointerReleased = ExtractMethodBody(timelineCode, "private void OnPointerReleased");

        Assert.Contains("UpdatePreview(", pointerMoved, StringComparison.Ordinal);
        Assert.DoesNotContain("SeekCommand", pointerMoved, StringComparison.Ordinal);
        Assert.Contains("CommitSeek();", pointerReleased, StringComparison.Ordinal);
        Assert.Contains("StartSeekingCommand", timelineCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerTrackSheet_UsesSelectionSheetSelectedVisuals()
    {
        var selectionSheet = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobileSelectionSheet.axaml");
        var trackSheet = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTrackSheet.axaml");

        foreach (var sharedVisual in new[]
                 {
                     "AccentSubtleBrush",
                     "RadioboxMarked",
                     "AccentBrush"
                 })
        {
            Assert.Contains(sharedVisual, selectionSheet, StringComparison.Ordinal);
            Assert.Contains(sharedVisual, trackSheet, StringComparison.Ordinal);
        }

        Assert.Contains("EqualityToBoolMultiConverter",
            trackSheet, StringComparison.Ordinal);
        Assert.Contains("SelectedAudioTrack",
            trackSheet, StringComparison.Ordinal);
        Assert.Contains("SelectedSubtitleTrack",
            trackSheet, StringComparison.Ordinal);
        Assert.Equal(2,
            trackSheet.Split("IsHitTestVisible=\"False\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void MobilePlayerQualitySheet_UsesSelectionSheetVisualsForCurrentPlaybackRate()
    {
        var qualitySheet = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerQualitySheet.axaml");

        Assert.Contains("CurrentPlaybackRateKey",
            qualitySheet, StringComparison.Ordinal);
        Assert.Contains("EqualityToBoolMultiConverter",
            qualitySheet, StringComparison.Ordinal);
        Assert.Equal(6,
            qualitySheet.Split("AccentSubtleBrush", StringSplitOptions.None).Length - 1);
        Assert.Equal(6,
            qualitySheet.Split("RadioboxMarked", StringSplitOptions.None).Length - 1);
        Assert.Equal(6,
            qualitySheet.Split("IsHitTestVisible=\"False\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "CommandParameter=\"0.",
            qualitySheet,
            StringComparison.Ordinal);
        Assert.Equal(6,
            qualitySheet.Split("CommandParameter=\"{x:Static vm:PlayerViewModel.PlaybackRate", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void MobilePlayerSleepSheet_UsesSelectionSheetVisualsForCurrentTimerMode()
    {
        var sleepSheet = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerSleepSheet.axaml");

        Assert.Contains("SleepTimerMode",
            sleepSheet, StringComparison.Ordinal);
        Assert.Contains("EqualityToBoolMultiConverter",
            sleepSheet, StringComparison.Ordinal);
        Assert.Equal(5,
            sleepSheet.Split("AccentSubtleBrush", StringSplitOptions.None).Length - 1);
        Assert.Equal(5,
            sleepSheet.Split("RadioboxMarked", StringSplitOptions.None).Length - 1);
        Assert.Equal(5,
            sleepSheet.Split("IsHitTestVisible=\"False\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void MobilePlayerTimeline_UpdatesBottomVodTimeDuringDragWithoutFloatingBubble()
    {
        var timeline = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTimeline.axaml");
        var timelineCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "Player",
            "MobilePlayerTimeline.axaml.cs");

        Assert.DoesNotContain("x:Name=\"PreviewBubble\"",
            timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"PreviewText\"",
            timeline, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatePreviewBubble(",
            timelineCode, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatPreviewTime(",
            timelineCode, StringComparison.Ordinal);
        Assert.Contains(
            "DisplayedPositionText",
            LoadProjectFile(
                "Noctra.Mobile",
                "Views",
                "Player",
                "MobilePlayerTransportBar.axaml"),
            StringComparison.Ordinal);
        Assert.Contains(
            "UpdateSeekPreview(",
            timelineCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "ClearSeekPreview();",
            timelineCode,
            StringComparison.Ordinal);

        var pointerPressed = ExtractMethodBody(
            timelineCode,
            "private void OnPointerPressed");
        Assert.Contains("_vm.IsLiveContent",
            pointerPressed, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(",
            timelineCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayerGestures_RequireVerticalIntentAndExcludeActualTransportBounds()
    {
        var playerView = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml");
        var playerViewCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs");

        Assert.Contains("x:Name=\"PlayerControls\"", playerView, StringComparison.Ordinal);
        Assert.Contains("PlayerGesturePolicy.Classify", playerViewCode, StringComparison.Ordinal);
        Assert.Contains("PlayerControls.TranslatePoint", playerViewCode, StringComparison.Ordinal);
        Assert.Contains("SwipeSensitivityDivisor = 1.75d",
            playerViewCode, StringComparison.Ordinal);
        Assert.DoesNotContain("BottomControlsGestureExclusionHeight",
            playerViewCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MobilePlayer_NormalVideoSurfaceUsesMatchParentAcrossRotation()
    {
        var playerViewCode = LoadProjectFile(
            "Noctra.Mobile",
            "Views",
            "MobilePlayerView.axaml.cs");

        var normalLayout = ExtractMethodBody(
            playerViewCode,
            "private void UpdateNormalVideoLayout()");
        var epgLayout = ExtractMethodBody(
            playerViewCode,
            "private void UpdateEpgVideoLayout()");

        Assert.Contains(
            "GetVideoSurfaceService()?.SetBounds(0, 0, -1, -1);",
            normalLayout,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SyncNativeSurfaceTo",
            normalLayout,
            StringComparison.Ordinal);
        Assert.Contains(
            "GetVideoSurfaceService()?.SetBounds(px, py, pw, ph);",
            epgLayout,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidPlayer_PositionAndBufferedPositionUseSeconds()
    {
        var playerInterface = LoadProjectFile(
            "Noctra.Core",
            "Services",
            "Interfaces",
            "IVideoPlayerService.cs");
        var playerService = LoadProjectFile(
            "Noctra.Android",
            "Services",
            "AndroidVideoPlayerService.cs");

        Assert.Contains("double BufferedPosition", playerInterface, StringComparison.Ordinal);
        Assert.Contains("public double BufferedPosition => _bufferedPosition;",
            playerService, StringComparison.Ordinal);
        Assert.Contains("get => CurrentTimeMilliseconds / 1000d;",
            playerService, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Math.Clamp((CurrentTimeMilliseconds / 1000d) / duration, 0, 1)",
            playerService,
            StringComparison.Ordinal);
        Assert.Contains("_exoPlayer.BufferedPosition", playerService, StringComparison.Ordinal);
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

    private static string ExtractMethodBody(string contents, string signature)
    {
        var signatureIndex = contents.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Method '{signature}' was not found.");

        var bodyStart = contents.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Method '{signature}' has no body.");

        var depth = 0;
        for (var index = bodyStart; index < contents.Length; index++)
        {
            if (contents[index] == '{')
            {
                depth++;
            }
            else if (contents[index] == '}' && --depth == 0)
            {
                return contents[bodyStart..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method '{signature}' body was not closed.");
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
