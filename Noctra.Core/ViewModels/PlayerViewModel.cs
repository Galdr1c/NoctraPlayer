using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noctra.Models;
using Noctra.Core.Services;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Noctra.ViewModels;

/// <summary>
/// Video player view model - refactored to decompose concerns
/// </summary>
public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private const double OverlayAutoHideDelayMs = 4000;
    public const float PlaybackRateHalf = 0.5f;
    public const float PlaybackRateThreeQuarters = 0.75f;
    public const float PlaybackRateNormal = 1.0f;
    public const float PlaybackRateOneAndQuarter = 1.25f;
    public const float PlaybackRateOneAndHalf = 1.5f;
    public const float PlaybackRateDouble = 2.0f;

    private static readonly float[] SupportedPlaybackRates =
    [
        PlaybackRateHalf,
        PlaybackRateThreeQuarters,
        PlaybackRateNormal,
        PlaybackRateOneAndQuarter,
        PlaybackRateOneAndHalf,
        PlaybackRateDouble
    ];
    
    public sealed record TrackOption(int Id, string Name, string? LanguageCode = null);
    public sealed class SkipOverlayEventArgs : EventArgs
    {
        public SkipOverlayEventArgs(double seconds)
        {
            Seconds = seconds;
        }

        public double Seconds { get; }
    }
    
    public event EventHandler? PiPRequested;
    public event EventHandler? PremiumUpsellRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? NextLiveChannelRequested;
    public event EventHandler? PreviousLiveChannelRequested;
    public event EventHandler<Episode>? NextEpisodeRequested;
    public event EventHandler<Episode>? EpisodeRequested;
    public event EventHandler<Episode>? EpisodeProgressUpdated;
    public event EventHandler<SkipOverlayEventArgs>? SkipOverlayRequested;

    public enum SleepTimerOption { Off, Minutes15, Minutes30, Minutes60, EndOfEpisode }
    public enum FillMode { Fit, Fill, Stretch, Original }
    public enum MobilePanelState { None, Actions, Audio, Quality, Info, Episodes, Sleep, Epg, Resume, NextEpisode }

    // ── Controllers / Subclasses (Decomposition Pattern) ────────────────────
    public PlayerPlaybackController PlaybackController { get; }
    public PlayerOverlayManager OverlayManager { get; }
    public PlayerEpisodeNavigator EpisodeNavigator { get; }
    public PlayerStallDetector StallDetector { get; }
    public PlayerQualityMonitor QualityMonitor { get; }
    public PlayerSettingsAdapter SettingsAdapter { get; }

    // ── Sleep Timer ────────────────────────────────────────────────────────
    internal CancellationTokenSource? _sleepCountdownCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSleepTimerActive))]
    [NotifyPropertyChangedFor(nameof(SleepTimerLabel))]
    private SleepTimerOption _sleepTimerMode = SleepTimerOption.Off;

    [ObservableProperty]
    private string _sleepTimerCountdown = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isSleepTimerPanelOpen;

    public bool IsSleepTimerActive => SleepTimerMode != SleepTimerOption.Off;

    public string SleepTimerLabel => SleepTimerMode switch
    {
        SleepTimerOption.Minutes15 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 15),
        SleepTimerOption.Minutes30 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 30),
        SleepTimerOption.Minutes60 => string.Format(_localizationService.GetString("Player.Sleep.LabelFormat"), 60),
        SleepTimerOption.EndOfEpisode => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndOfEpisode" : "Player.Sleep.EndOfMovie"),
        _ => _localizationService.GetString("Player.Overlay.SleepTimer.Tooltip")
    };

    public string EndOfContentText => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndContent.Episode" : "Player.Sleep.EndContent.Movie");
    public string EndOfContentDescription => _localizationService.GetString(IsSeriesContent ? "Player.Sleep.EndDescription.Episode" : "Player.Sleep.EndDescription.Movie");

    // ── Infrastructure Dependencies ──────────────────────────────────────────
    private readonly IVideoPlayerService _videoPlayerService;
    private readonly IEpgService _epgService;
    private readonly IMetadataService _metadataService;
    private readonly IMediaService _mediaService;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly INetworkService _networkService;
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly ILocalizationService _localizationService;
    private readonly MainViewModel _mainViewModel;
    private readonly IWatchHistoryService? _watchHistoryService;
    private readonly IStalkerPortalService _stalkerPortalService;
    private readonly IDispatcherService _dispatcherService;

    // ── State Fields ────────────────────────────────────────────────────────
    internal int _playRequestVersion;
    internal bool _isPreferenceApplied;

    private readonly Dictionary<string, TrackSelectionSnapshot> _trackSelectionsByContent = new(StringComparer.Ordinal);

    internal sealed class TrackSelectionSnapshot
    {
        public int? AudioTrackId { get; set; }
        public string? AudioTrackName { get; set; }
        public string? AudioLanguageCode { get; set; }
        public int? SubtitleTrackId { get; set; }
        public string? SubtitleTrackName { get; set; }
        public string? SubtitleLanguageCode { get; set; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayerVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlsVisible))]
    [NotifyPropertyChangedFor(nameof(AreMobileControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsTopOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveInfoVisible))]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    [NotifyPropertyChangedFor(nameof(IsVodPlotVisible))]
    [NotifyPropertyChangedFor(nameof(TimelineAccessibilityName))]
    private bool _isLiveContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    [NotifyPropertyChangedFor(nameof(IsVodPlotVisible))]
    [NotifyPropertyChangedFor(nameof(IsInfoSeriesHeaderVisible))]
    [NotifyPropertyChangedFor(nameof(IsInfoEpisodeVisible))]
    [NotifyPropertyChangedFor(nameof(InfoDirectorText))]
    [NotifyPropertyChangedFor(nameof(InfoCastText))]
    [NotifyPropertyChangedFor(nameof(IsInfoDirectorVisible))]
    [NotifyPropertyChangedFor(nameof(IsInfoCastVisible))]
    private bool _isSeriesContent;

    public bool IsPremium => _licenseService.IsPremium;

    [ObservableProperty]
    private bool _isDownloadedPlayback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsControlsVisible))]
    [NotifyPropertyChangedFor(nameof(AreMobileControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsTopOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    [NotifyPropertyChangedFor(nameof(LockAccessibilityName))]
    private bool _isLocked;

    [ObservableProperty]
    private bool _isLockIndicatorVisible;

    private CancellationTokenSource? _lockIndicatorVisibilityCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlsVisible))]
    [NotifyPropertyChangedFor(nameof(AreMobileControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsTopOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isPiPMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClosePlayerCommand))]
    private bool _isClosingPlayer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPiPControlsVisible))]
    private bool _isPiPControlsForceVisible;

    public bool IsPiPControlsVisible => IsPiPMode && IsPiPControlsForceVisible;

    /// <summary>
    /// Mobil oynatıcıda alt kontrol katmanının (bottom sheet) görünür olup olmadığı.
    /// Kontroller görünürken ve EPG paneli kapalıyken true olur.
    /// Tek dokunuşla aç/kapat (ToggleControls) ve otomatik gizleme bu değeri sürer.
    /// </summary>
    public bool IsPlayerVisible => IsVisible;
    public bool IsControlsVisible => IsVisible && !IsPiPMode && !IsLocked;
    public bool AreMobileControlsVisible => IsControlsVisible && !IsEpgPanelOpen;
    public bool IsTopOverlayVisible => AreMobileControlsVisible;

    public MobilePanelState ActiveMobilePanelState
    {
        get
        {
            if (IsEpgPanelOpen) return MobilePanelState.Epg;
            if (IsActionsPanelOpen) return MobilePanelState.Actions;
            if (IsAudioSettingsOpen) return MobilePanelState.Audio;
            if (IsQualitySettingsOpen) return MobilePanelState.Quality;
            if (IsInfoPanelOpen) return MobilePanelState.Info;
            if (IsEpisodesPanelOpen) return MobilePanelState.Episodes;
            if (IsSleepTimerPanelOpen) return MobilePanelState.Sleep;
            if (IsResumeDialogVisible) return MobilePanelState.Resume;
            if (IsNextEpisodePromptVisible) return MobilePanelState.NextEpisode;

            return MobilePanelState.None;
        }
    }

    public bool IsPanelOpen => ActiveMobilePanelState != MobilePanelState.None;

    public bool IsMobileDetailPanelOpen =>
        IsActionsPanelOpen ||
        IsAudioSettingsOpen ||
        IsQualitySettingsOpen ||
        IsInfoPanelOpen ||
        IsEpisodesPanelOpen ||
        IsSleepTimerPanelOpen ||
        IsResumeDialogVisible ||
        IsNextEpisodePromptVisible;

    public bool IsMobileCompactControlsVisible =>
        AreMobileControlsVisible && !IsMobileDetailPanelOpen;

    public bool IsBottomControlsVisible => IsMobileCompactControlsVisible;

    internal void SetMobilePanelState(MobilePanelState state)
    {
        if (state is MobilePanelState.Resume or MobilePanelState.NextEpisode)
        {
            return;
        }

        IsActionsPanelOpen = state == MobilePanelState.Actions;
        IsAudioSettingsOpen = state == MobilePanelState.Audio;
        IsQualitySettingsOpen = state == MobilePanelState.Quality;
        IsInfoPanelOpen = state == MobilePanelState.Info;
        IsEpisodesPanelOpen = state == MobilePanelState.Episodes;
        IsSleepTimerPanelOpen = state == MobilePanelState.Sleep;
        IsEpgPanelOpen = state == MobilePanelState.Epg;

        if (state != MobilePanelState.None)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
        }

        OnPropertyChanged(nameof(ActiveMobilePanelState));
        OnPropertyChanged(nameof(IsMobileDetailPanelOpen));
    }

    internal void ToggleMobilePanelState(MobilePanelState state)
    {
        if (state is MobilePanelState.Resume or MobilePanelState.NextEpisode)
        {
            return;
        }

        SetMobilePanelState(ActiveMobilePanelState == state ? MobilePanelState.None : state);
    }

    private MobilePanelState _panelParentState = MobilePanelState.None;

    [RelayCommand]
    private void OpenActionsPanel()
    {
        _panelParentState = MobilePanelState.None;
        SetMobilePanelState(MobilePanelState.Actions);
    }

    internal void OpenChildPanel(MobilePanelState panel)
    {
        _panelParentState = MobilePanelState.Actions;
        SetMobilePanelState(panel);
    }

    [RelayCommand]
    private void BackFromPlayerPanel()
    {
        if (IsResumeDialogVisible)
        {
            CancelResumeDialog();
            return;
        }

        if (IsNextEpisodePromptVisible)
        {
            IsNextEpisodePromptVisible = false;
            RestartAutoHideTimer();
            return;
        }

        if (_panelParentState == MobilePanelState.Actions)
        {
            _panelParentState = MobilePanelState.None;
            SetMobilePanelState(MobilePanelState.Actions);
            return;
        }

        SetMobilePanelState(MobilePanelState.None);
    }

    // ── EPG Timeline Panel ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(AreMobileControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsTopOverlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isEpgPanelOpen;
    [ObservableProperty] private bool _isEpgLoading;
    [ObservableProperty] private int _epgFocusRowIndex = -1;
    [ObservableProperty] private bool _isEpgUpdateRequired;
    [ObservableProperty] private string _epgStatusMessage = string.Empty;

    /// <summary>
    /// EPG paneli için zaman penceresi sabitleri.
    /// Pencere: şimdiki zaman - 2 saat → + 4 saat (6 saatlik pencere, 3 px/dk).
    /// "NOW" çizgisi: 2 * 60 * 3 = 360 px sola yerleştirilir.
    /// </summary>
    public const double EpgPxPerMinute  = 3.0;
    public const double EpgPastHours    = 6.0;
    public const double EpgFutureHours  = 12.0;
    public const double EpgNowPixelPos  = EpgPastHours * 60 * EpgPxPerMinute;
    public const double EpgCanvasWidth  = (EpgPastHours + EpgFutureHours) * 60 * EpgPxPerMinute;
    public double EpgCanvasPixelWidth => EpgCanvasWidth;

    /// <summary>
    /// Ekranın mevcut zamanında NOW çizgisinin piksel pozisyonunu döndürür.
    /// Panel açıldığında hesaplanır; otomatik güncellenmez (yeterince doğru).
    /// </summary>
    public double EpgNowLineLeft => EpgNowPixelPos;
    public double EpgNowBadgeLeft => EpgNowPixelPos - 13;

    public System.Collections.ObjectModel.ObservableCollection<EpgPanelRow> EpgRows { get; }
        = new System.Collections.ObjectModel.ObservableCollection<EpgPanelRow>();

    /// <summary>
    /// MainWindow tarafından set edilir; EPG paneli açıldığında canlı kanal listesini sağlar.
    /// </summary>
    public Func<Task<List<Models.Channel>>>? LiveChannelsLoader { get; set; }
    // ────────────────────────────────────────────────────────────────────────

    partial void OnIsPiPModeChanged(bool value)
    {
        if (value)
        {
            OverlayManager.ClosePanels();
        }

        IsPiPControlsForceVisible = value;
        IsVisible = true;
        RestartAutoHideTimer();
        OnPropertyChanged(nameof(IsPiPControlsVisible));
        OnPropertyChanged(nameof(AreMobileControlsVisible));
    }

    [ObservableProperty]
    private string _currentTimeStr = "00:00";

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private string _channelLogo = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private string _streamInfo = string.Empty;

    [ObservableProperty]
    private double _bufferingProgress;

    [ObservableProperty]
    private bool _isBuffering;

    public bool IsBufferShieldVisible => IsBuffering && (IsLiveContent || !_suppressBufferShieldForSeek);

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _playerLoadingWarningMessage = string.Empty;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isResizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isActionsPanelOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isAudioSettingsOpen;

    [ObservableProperty]
    private string _networkStatus = "Wi-Fi";

    [ObservableProperty]
    private string _networkIcon = "Wifi";

    [ObservableProperty]
    private string _remainingTime = "-00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVodPlotVisible))]
    [NotifyPropertyChangedFor(nameof(InfoVodMetaText))]
    [NotifyPropertyChangedFor(nameof(InfoDirectorText))]
    [NotifyPropertyChangedFor(nameof(InfoCastText))]
    [NotifyPropertyChangedFor(nameof(IsInfoDirectorVisible))]
    [NotifyPropertyChangedFor(nameof(IsInfoCastVisible))]
    [NotifyPropertyChangedFor(nameof(IsInfoSeriesHeaderVisible))]
    private Channel? _currentChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveFavoriteAccessibilityName))]
    private bool _isCurrentChannelFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveInfoVisible))]
    [NotifyPropertyChangedFor(nameof(LiveProgramProgress))]
    [NotifyPropertyChangedFor(nameof(TimelineAccessibilityName))]
    private EpgProgram? _currentProgram;

    public bool HasCurrentProgramInfo =>
        CurrentProgram != null &&
        !string.IsNullOrWhiteSpace(CurrentProgram.Title) &&
        !string.Equals(CurrentProgram.Title, _localizationService.GetString("Player.Epg.NoInfo"), StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _overlaySecondaryText = string.Empty;

    [ObservableProperty]
    private string _overlayMessage = string.Empty;

    [ObservableProperty]
    private bool _isOverlayMessageVisible;

    internal CancellationTokenSource? _overlayMessageCts;

    public bool IsLiveInfoVisible => IsLiveContent && CurrentProgram != null && !string.IsNullOrWhiteSpace(CurrentProgram.Title);
    public bool IsSeriesPlotVisible => IsSeriesContent && !IsLiveContent && CurrentEpisode != null && !string.IsNullOrWhiteSpace(CurrentEpisode.Plot);
    public bool IsVodPlotVisible => !IsLiveContent && !IsSeriesContent && CurrentChannel != null && !string.IsNullOrWhiteSpace(CurrentChannel.Plot);

    public double LiveProgramProgress
    {
        get
        {
            if (CurrentProgram is null)
                return 0;

            var duration = (CurrentProgram.EndTime - CurrentProgram.StartTime).TotalSeconds;
            if (duration <= 0)
                return 0;

            var elapsed = (DateTime.UtcNow - CurrentProgram.StartTime).TotalSeconds;
            return Math.Clamp(elapsed / duration * 100d, 0d, 100d);
        }
    }

    public string PlayPauseAccessibilityName =>
        _localizationService.GetString(
            IsPlaying
                ? "Player.Accessibility.Pause"
                : "Player.Accessibility.Play");

    public string MuteAccessibilityName =>
        _localizationService.GetString(
            IsMuted
                ? "Player.Accessibility.Unmute"
                : "Player.Accessibility.Mute");

    public string LiveFavoriteAccessibilityName =>
        _localizationService.GetString(
            IsCurrentChannelFavorite
                ? "Player.Accessibility.RemoveFavorite"
                : "Player.Accessibility.AddFavorite");

    public string LockAccessibilityName =>
        _localizationService.GetString(
            IsLocked
                ? "Player.Accessibility.Unlock"
                : "Player.Accessibility.Lock");

    public string TimelineAccessibilityName
    {
        get
        {
            if (IsLiveContent)
            {
                var progress = (int)Math.Round(
                    LiveProgramProgress,
                    MidpointRounding.AwayFromZero);

                if (HasCurrentProgramInfo)
                {
                    return string.Format(
                        _localizationService.GetString(
                            "Player.Accessibility.Timeline.LiveProgressWithTitle"),
                        CurrentProgram!.Title.Trim(),
                        progress);
                }

                return string.Format(
                    _localizationService.GetString(
                        "Player.Accessibility.Timeline.LiveProgress"),
                    progress);
            }

            var position = FormatAccessibilityTime(Position);
            if (Duration <= 0)
            {
                return string.Format(
                    _localizationService.GetString(
                        "Player.Accessibility.Timeline.UnknownDuration"),
                    position);
            }

            return string.Format(
                _localizationService.GetString(
                    "Player.Accessibility.Timeline.Progress"),
                position,
                FormatAccessibilityTime(Duration));
        }
    }

    private string FormatAccessibilityTime(double seconds)
    {
        var totalSeconds = (long)Math.Floor(Math.Max(0, seconds));
        var hours = (int)(totalSeconds / 3600);
        var minutes = (int)((totalSeconds % 3600) / 60);
        var remainingSeconds = (int)(totalSeconds % 60);
        var parts = new List<string>(3);

        if (hours > 0)
        {
            parts.Add(FormatAccessibilityTimeUnit(
                hours,
                "Player.Accessibility.Time.Hour.One",
                "Player.Accessibility.Time.Hour.Many"));
        }

        if (minutes > 0)
        {
            parts.Add(FormatAccessibilityTimeUnit(
                minutes,
                "Player.Accessibility.Time.Minute.One",
                "Player.Accessibility.Time.Minute.Many"));
        }

        if (remainingSeconds > 0 || parts.Count == 0)
        {
            parts.Add(FormatAccessibilityTimeUnit(
                remainingSeconds,
                "Player.Accessibility.Time.Second.One",
                "Player.Accessibility.Time.Second.Many"));
        }

        return string.Join(" ", parts);
    }

    private string FormatAccessibilityTimeUnit(
        int value,
        string singularKey,
        string pluralKey)
    {
        var format = _localizationService.GetString(
            value == 1 ? singularKey : pluralKey);
        return string.Format(format, value);
    }

    // Mobile info panel uses these richer desktop-parity metadata fields.
    public string? CurrentEpisodeDisplayTitle => FirstNonEmpty(CurrentEpisode?.TmdbEpisodeName, CurrentEpisode?.Name);
    public string? CurrentEpisodeMetaText => FirstNonEmpty(CurrentEpisode?.EpisodeMetaText, CurrentEpisode?.AirDateText);
    public bool IsInfoEpisodeVisible =>
        IsSeriesContent &&
        !IsLiveContent &&
        CurrentEpisode != null &&
        (!string.IsNullOrWhiteSpace(CurrentEpisodeDisplayTitle) ||
         !string.IsNullOrWhiteSpace(CurrentEpisodeMetaText) ||
         !string.IsNullOrWhiteSpace(CurrentEpisode.Plot));
    public string? SeriesInfoTitle => _currentSeriesContext?.Name;
    public string? SeriesInfoPlot => _currentSeriesContext?.Plot;
    public bool IsInfoSeriesHeaderVisible =>
        IsSeriesContent &&
        !string.IsNullOrWhiteSpace(SeriesInfoTitle) &&
        !string.Equals(SeriesInfoTitle, CurrentChannel?.Name, StringComparison.OrdinalIgnoreCase);

    public string? InfoDirectorText => IsLiveContent
        ? null
        : FirstNonEmpty(IsSeriesContent ? _currentSeriesContext?.Director : null, CurrentChannel?.Director);

    public string? InfoCastText => IsLiveContent
        ? null
        : FirstNonEmpty(IsSeriesContent ? _currentSeriesContext?.Cast : null, CurrentChannel?.Cast);

    public bool IsInfoDirectorVisible => !string.IsNullOrWhiteSpace(InfoDirectorText);
    public bool IsInfoCastVisible => !string.IsNullOrWhiteSpace(InfoCastText);

    public string? InfoVodMetaText
    {
        get
        {
            if (CurrentChannel == null || IsLiveContent || IsSeriesContent)
            {
                return null;
            }

            var parts = new List<string>();
            if (CurrentChannel.ReleaseYear.HasValue)
            {
                parts.Add(CurrentChannel.ReleaseYear.Value.ToString());
            }

            if (CurrentChannel.Rating.HasValue && CurrentChannel.Rating.Value > 0)
            {
                parts.Add($"★ {CurrentChannel.Rating.Value:0.0}");
            }

            if (!string.IsNullOrWhiteSpace(CurrentChannel.ContentRating))
            {
                parts.Add(CurrentChannel.ContentRating!);
            }

            if (CurrentChannel.Duration.HasValue && CurrentChannel.Duration.Value.TotalMinutes > 0)
            {
                parts.Add($"{(int)CurrentChannel.Duration.Value.TotalMinutes} dk");
            }

            return parts.Count > 0 ? string.Join("  •  ", parts) : null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseAccessibilityName))]
    private bool _isPlaying;

    partial void OnIsPlayingChanged(bool value)
    {
        if (value)
        {
            _unreachableWarningTimer?.Dispose();
            _unreachableWarningTimer = null;
            PlayerLoadingWarningMessage = string.Empty;
        }
    }

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private FillMode _videoFillMode = FillMode.Fit;

    [ObservableProperty]
    private int _subtitleFontSize = 40;

    [ObservableProperty]
    private int _subtitleBackgroundOpacity = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitlePositionTop))]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitlePositionBottom))]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitleTopVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitleBottomVisible))]
    private int _subtitleMargin = 40;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitleVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitleTopVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileSubtitleBottomVisible))]
    private string _currentSubtitleText = string.Empty;

    public bool IsMobileSubtitleVisible => !string.IsNullOrWhiteSpace(CurrentSubtitleText);
    public bool IsMobileSubtitlePositionTop => SubtitleMargin >= 900;
    public bool IsMobileSubtitlePositionBottom => !IsMobileSubtitlePositionTop;
    public bool IsMobileSubtitleTopVisible => IsMobileSubtitleVisible && IsMobileSubtitlePositionTop;
    public bool IsMobileSubtitleBottomVisible => IsMobileSubtitleVisible && IsMobileSubtitlePositionBottom;

    private CancellationTokenSource? _subtitleSaveCts;

    partial void OnSubtitleFontSizeChanged(int value) => QueueSubtitleSettingsSave();
    partial void OnSubtitleBackgroundOpacityChanged(int value) => QueueSubtitleSettingsSave();
    partial void OnSubtitleMarginChanged(int value) => QueueSubtitleSettingsSave();

    private void QueueSubtitleSettingsSave()
    {
        if (_settingsService == null) return;

        bool changed = false;
        if (_settingsService.Settings.SubtitleFontSize != SubtitleFontSize)
        {
            _settingsService.Settings.SubtitleFontSize = SubtitleFontSize;
            changed = true;
        }
        if (_settingsService.Settings.SubtitleBackgroundOpacity != SubtitleBackgroundOpacity)
        {
            _settingsService.Settings.SubtitleBackgroundOpacity = SubtitleBackgroundOpacity;
            changed = true;
        }
        if (_settingsService.Settings.SubtitleMargin != SubtitleMargin)
        {
            _settingsService.Settings.SubtitleMargin = SubtitleMargin;
            changed = true;
        }

        if (changed)
        {
            var oldCts = _subtitleSaveCts;
            _subtitleSaveCts = new CancellationTokenSource();
            var token = _subtitleSaveCts.Token;

            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (!token.IsCancellationRequested)
                    {
                        await _settingsService.SaveAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Failed to persist volume: {ex.Message}");
                }
            }, token);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteAccessibilityName))]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineAccessibilityName))]
    private double _position;

    [ObservableProperty]
    private double _bufferedPosition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineAccessibilityName))]
    private double _duration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedPositionText))]
    private string _positionText = "00:00:00";

    [ObservableProperty]
    private string _durationText = "00:00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedPositionText))]
    private bool _isSeekPreviewActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedPositionText))]
    private string _seekPreviewPositionText = string.Empty;

    public string DisplayedPositionText =>
        IsSeekPreviewActive ? SeekPreviewPositionText : PositionText;

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private bool _showControls = true;

    [ObservableProperty]
    private ObservableCollection<TrackOption> _audioTracks = new();

    [ObservableProperty]
    private ObservableCollection<TrackOption> _subtitleTracks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAudioTrackName))]
    private int _selectedAudioTrack = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSubtitleTrackName))]
    private int _selectedSubtitleTrack = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPlaybackRateKey))]
    private float _currentPlaybackRate = 1.0f;

    public string SelectedAudioTrackName => AudioTracks.FirstOrDefault(t => t.Id == SelectedAudioTrack)?.Name ?? string.Empty;

    public string SelectedSubtitleTrackName => SubtitleTracks.FirstOrDefault(t => t.Id == SelectedSubtitleTrack)?.Name ?? string.Empty;

    public string CurrentPlaybackRateKey =>
        CurrentPlaybackRate.ToString("0.0#", CultureInfo.InvariantCulture);

    public bool HasNetworkError =>
        !string.IsNullOrWhiteSpace(ConnectionStatus) &&
        (ConnectionStatus.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
         ConnectionStatus.Contains("error", StringComparison.OrdinalIgnoreCase) ||
         ConnectionStatus.Contains("disconnect", StringComparison.OrdinalIgnoreCase));

    public string QualitySummary
    {
        get
        {
            if (StreamQuality is null && string.IsNullOrWhiteSpace(QualityResolutionText))
                return string.Empty;

            var resolution = StreamQuality is { Width: > 0, Height: > 0 }
                ? $"{StreamQuality.Height}p"
                : QualityResolutionText;

            return resolution;
        }
    }

    public string AudioSubtitleSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SelectedAudioTrackName))
                parts.Add(SelectedAudioTrackName);

            if (!string.IsNullOrWhiteSpace(SelectedSubtitleTrackName) &&
                !string.Equals(SelectedSubtitleTrackName, "Off", StringComparison.OrdinalIgnoreCase))
                parts.Add(SelectedSubtitleTrackName);

            return parts.Count > 0 ? string.Join(" • ", parts) : string.Empty;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isQualitySettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isEpisodesPanelOpen;

    [ObservableProperty]
    private StreamQualityInfo? _streamQuality;

    public string QualityResolutionText => StreamQuality?.Height > 0
        ? StreamQuality.ResolutionLabel
        : _localizationService.GetString("Common.Unknown");

    public string QualityFpsText => StreamQuality?.Fps > 0
        ? $"{StreamQuality.Fps} FPS"
        : _localizationService.GetString("Common.Unknown");

    public string QualityVideoCodecText => !string.IsNullOrWhiteSpace(StreamQuality?.VideoCodecDisplay)
        ? StreamQuality.VideoCodecDisplay
        : _localizationService.GetString("Common.Unknown");

    public string QualityVideoBitrateText => StreamQuality?.VideoBitrate > 0
        ? FormatBitrate(StreamQuality.VideoBitrate)
        : _localizationService.GetString("Common.Unknown");

    public string QualityAudioText => !string.IsNullOrWhiteSpace(StreamQuality?.AudioDetailLabel)
        ? StreamQuality.AudioDetailLabel
        : _localizationService.GetString("Common.Unknown");

    public bool HasTopQualityBadgesReady =>
        StreamQuality != null &&
        StreamQuality.Height > 0 &&
        StreamQuality.Fps > 0 &&
        !string.IsNullOrWhiteSpace(StreamQuality.VideoCodecDisplay);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isInfoPanelOpen;

    [ObservableProperty]
    private List<Season> _episodeSeasons = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEpisodeSeasonEpisodes))]
    private Season? _selectedEpisodeSeason;

    public IReadOnlyList<Episode> SelectedEpisodeSeasonEpisodes =>
        SelectedEpisodeSeason?.Episodes?
            .OrderBy(e => e.EpisodeNumber)
            .ToList() ?? [];

    [ObservableProperty]
    private string _episodesPanelTitle = string.Empty;

    [ObservableProperty]
    private Episode? _nextEpisode;

    [ObservableProperty]
    private string _currentEpisodeIdentity = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isNextEpisodePromptVisible;

    [ObservableProperty]
    private bool _isCreditsZone;

    [ObservableProperty]
    private bool _isDownloadInProgress;

    [ObservableProperty]
    private string _downloadStatusMessage = string.Empty;

    public bool CanShowDownloadButton => CurrentChannel != null && !IsLiveContent && !IsDownloadedPlayback;
    public bool CanShowInfoButton => !IsDownloadedPlayback;
    public bool CanShowGoToLiveButton => IsLiveContent && Duration > 0;

    public bool CanDownloadCurrentContent =>
        CanShowDownloadButton &&
        !IsDownloadInProgress &&
        !string.IsNullOrWhiteSpace(CurrentChannel?.StreamUrl);

    public string DownloadButtonText => IsDownloadInProgress 
        ? _localizationService.GetString("Player.Download.Downloading") 
        : _localizationService.GetString("Player.Download.Download");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeriesPlotVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentEpisodeDisplayTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentEpisodeMetaText))]
    [NotifyPropertyChangedFor(nameof(IsInfoEpisodeVisible))]
    private Episode? _currentEpisode;

    // ── Playback Internal States ─────────────────────────────────────────────
    internal bool _creditsTriggered;
    internal bool _isUserSeeking;
    internal bool _hasPendingSkipSeekTarget;
    internal double _pendingSkipSeekTarget;
    internal DateTime _pendingSkipSeekExpiresUtc = DateTime.MinValue;
    internal double _skipAggregationSeconds;
    internal DateTime _skipAggregationLastUpdatedUtc = DateTime.MinValue;
    internal bool _isPlaybackEnded;
    internal int _recoveryState;
    internal double _lastPausedPosition;
    internal long _lastPausedTimeMs;
    internal double _pendingResumeSeekPosition;
    internal int _pendingResumeSeekAttempts;
    internal double _lastKnownValidPosition;
    internal int _prematureEndRecoveryCount;
    internal DateTime _lastPrematureEndRecoveryUtc = DateTime.MinValue;
    internal const int MaxPrematureEndRecoveries = 5;
    internal static readonly TimeSpan PrematureEndRecoveryCooldown = TimeSpan.FromSeconds(3);
    
    internal int _isPlayPauseInProgress;
    private int _isClockProcessing;
    internal bool _isIntentionallyPaused;
    internal bool _livePauseRequiresHardRestart;
    internal DateTime _lastLiveProgressAtUtc = DateTime.MinValue;
    internal DateTime _lastLivePositionEventAtUtc = DateTime.MinValue;
    internal DateTime _lastLiveAutoRecoverAttemptAtUtc = DateTime.MinValue;
    internal DateTime _liveRecoveryWindowStartUtc = DateTime.MinValue;
    internal int _volumeBeforeMute = 100;
    internal bool _suppressBufferShieldForSeek;
    internal int _seekShieldSuppressionToken;
    internal long _lastSeekTargetMs = -1;
    internal Series? _currentSeriesContext;
    internal bool _isContentTransitioning;
    internal bool _isUpdatingFromService;
    internal int _isDownloadActionRunning;



    internal DateTime _lastWatchHistoryUpdateUtc = DateTime.MinValue;
    internal DateTime _sessionPlaybackStartTimeUtc = DateTime.MinValue;

    internal readonly System.Threading.Timer _autoHideTimer;
    internal System.Threading.Timer? _unreachableWarningTimer;
    private readonly System.Timers.Timer _clockTimer;
    internal readonly System.Timers.Timer _watchHistoryTimer;

    private readonly SemaphoreSlim _exitGate = new(1, 1);
    private readonly object _playbackExitFlushSync = new();
    private Task _pendingPlaybackExitFlush = Task.CompletedTask;

    internal void LogDebug(string msg) {
        System.Diagnostics.Debug.WriteLine($"[PVM] {msg}");
    }

    public int? CurrentProfileId { get; set; }

    // ── Properties to expose internal services to subclasses ─────────────────
    internal IVideoPlayerService VideoPlayerService => _videoPlayerService;
    internal IEpgService EpgService => _epgService;
    internal IMediaService MediaService => _mediaService;
    internal IContentDownloadService ContentDownloadService => _contentDownloadService;
    internal INetworkService NetworkService => _networkService;
    internal ISettingsService SettingsService => _settingsService;
    internal ILicenseService LicenseService => _licenseService;
    internal ILocalizationService LocalizationService => _localizationService;
    internal IWatchHistoryService? WatchHistoryService => _watchHistoryService;
    internal IStalkerPortalService StalkerPortalService => _stalkerPortalService;
    internal IDispatcherService DispatcherService => _dispatcherService;
    internal MainViewModel MainViewModel => _mainViewModel;

    public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);
    internal void RaiseNextEpisodeRequestedEvent(Episode episode) => NextEpisodeRequested?.Invoke(this, episode);
    internal void RaiseEpisodeRequestedEvent(Episode episode) => EpisodeRequested?.Invoke(this, episode);
    internal void RaiseEpisodeProgressUpdatedEvent(Episode episode) => EpisodeProgressUpdated?.Invoke(this, episode);
    internal void RaiseSkipOverlayEvent(double seconds) => SkipOverlayRequested?.Invoke(this, new SkipOverlayEventArgs(seconds));

    public PlayerViewModel(
        IVideoPlayerService videoPlayerService,
        IEpgService epgService,
        IMetadataService metadataService,
        IMediaService mediaService,
        IContentDownloadService contentDownloadService,
        INetworkService networkService,
        IDispatcherService dispatcherService,
        ISettingsService settingsService,
        ILicenseService licenseService,
        ILocalizationService localizationService,
        MainViewModel mainViewModel,
        IWatchHistoryService? watchHistoryService,
        IStalkerPortalService stalkerPortalService)
    {
        _videoPlayerService = videoPlayerService;
        _epgService = epgService;
        _metadataService = metadataService;
        _mediaService = mediaService;
        _contentDownloadService = contentDownloadService;
        _networkService = networkService;
        _dispatcherService = dispatcherService;
        _settingsService = settingsService;
        _licenseService = licenseService;
        _localizationService = localizationService;
        _mainViewModel = mainViewModel;
        _watchHistoryService = watchHistoryService;
        _stalkerPortalService = stalkerPortalService;

        // Initialize Controllers
        PlaybackController = new PlayerPlaybackController(this);
        OverlayManager = new PlayerOverlayManager(this);
        EpisodeNavigator = new PlayerEpisodeNavigator(this);
        StallDetector = new PlayerStallDetector(this);
        QualityMonitor = new PlayerQualityMonitor(this);
        SettingsAdapter = new PlayerSettingsAdapter(this);

        _localizationService.LanguageChanged += OnLanguageChanged;

        ConnectionStatus = _localizationService.GetString("Player.Status.Connecting");
        StallDetector.UpdateNetworkStatus(_networkService.CurrentNetworkStatus);
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;

        if (_settingsService?.Settings != null)
        {
            SubtitleFontSize = _settingsService.Settings.SubtitleFontSize;
            SubtitleBackgroundOpacity = _settingsService.Settings.SubtitleBackgroundOpacity;
            SubtitleMargin = _settingsService.Settings.SubtitleMargin;

            // Sync initial volume and mute states from settings
            Volume = _settingsService.Settings.DefaultVolume;
            IsMuted = _settingsService.Settings.IsMuted;
            _volumeBeforeMute = Volume > 0 ? Volume : 100;
        }

        _settingsService.SettingsChanged += OnSettingsChanged;

        _autoHideTimer = new System.Threading.Timer(_ =>
            _dispatcherService.BeginInvoke(() =>
            {
                if (CanAutoHideOverlay())
                {
                    IsVisible = false;
                    if (IsPiPMode)
                        IsPiPControlsForceVisible = false;
                }
            }), null, Timeout.Infinite, Timeout.Infinite);

        _clockTimer = new System.Timers.Timer(1000);
        _clockTimer.Elapsed += async (s, e) =>
        {
            if (Interlocked.Exchange(ref _isClockProcessing, 1) == 1)
            {
                return;
            }

            try
            {
                _dispatcherService.BeginInvoke(() => CurrentTimeStr = DateTime.Now.ToString("HH:mm"));

                await Task.WhenAll(
                    Task.Run(async () => {
                        try { await CheckForEpgUpdateAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] EPG update failed: {ex.Message}"); }
                    }),
                    Task.Run(async () => {
                        try { await StallDetector.MonitorLivePlaybackHealthAsync(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Live monitor failed: {ex.Message}"); }
                    })
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel] Clock tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isClockProcessing, 0);
            }
        };
        _clockTimer.Start();
        CurrentTimeStr = DateTime.Now.ToString("HH:mm");

        _watchHistoryTimer = new System.Timers.Timer(5000);
        _watchHistoryTimer.Elapsed += async (s, e) => await TrackWatchHistoryAsync();
        _watchHistoryTimer.AutoReset = true;

        _videoPlayerService.PlayingChanged += OnVideoPlayerServicePlayingChanged;
        _videoPlayerService.PlaybackEnded += OnVideoPlayerServicePlaybackEnded;
        _videoPlayerService.QualityDetected += OnVideoPlayerServiceQualityDetected;
        _videoPlayerService.BufferingChanged += OnVideoPlayerServiceBufferingChanged;
        _videoPlayerService.ErrorOccurred += OnVideoPlayerServiceErrorOccurred;
        _videoPlayerService.PositionChanged += OnVideoPlayerServicePositionChanged;
        _videoPlayerService.VolumeChanged += OnVideoPlayerServiceVolumeChanged;
        _videoPlayerService.SubtitleTextChanged += OnVideoPlayerServiceSubtitleTextChanged;

        _licenseService.SubscriptionChanged += OnLicenseServiceSubscriptionChanged;
    }

    private void OnLicenseServiceSubscriptionChanged()
    {
        _dispatcherService.BeginInvoke(() => 
        {
            OnPropertyChanged(nameof(IsPremium));
            SetSleepTimerCommand.NotifyCanExecuteChanged();
        });
    }

    // ── Internal Helpers ─────────────────────────────────────────────────────
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    internal void RaiseInfoPanelMetadataChanged()
    {
        OnPropertyChanged(nameof(CurrentEpisodeDisplayTitle));
        OnPropertyChanged(nameof(CurrentEpisodeMetaText));
        OnPropertyChanged(nameof(IsInfoEpisodeVisible));
        OnPropertyChanged(nameof(SeriesInfoTitle));
        OnPropertyChanged(nameof(SeriesInfoPlot));
        OnPropertyChanged(nameof(IsInfoSeriesHeaderVisible));
        OnPropertyChanged(nameof(InfoDirectorText));
        OnPropertyChanged(nameof(InfoCastText));
        OnPropertyChanged(nameof(IsInfoDirectorVisible));
        OnPropertyChanged(nameof(IsInfoCastVisible));
        OnPropertyChanged(nameof(InfoVodMetaText));
    }

    internal void UpdateOverlaySecondaryText()
    {
        if (IsLiveContent)
        {
            OverlaySecondaryText = CurrentProgram?.Title ?? string.Empty;
            return;
        }

        if (CurrentChannel == null)
        {
            OverlaySecondaryText = string.Empty;
            return;
        }

        if (!string.IsNullOrWhiteSpace(CurrentChannel.Plot))
        {
            OverlaySecondaryText = CurrentChannel.Plot!;
            return;
        }

        var parts = new List<string>();
        if (CurrentChannel.ReleaseYear.HasValue)
        {
            parts.Add(CurrentChannel.ReleaseYear.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(CurrentChannel.Cast))
        {
            parts.Add(CurrentChannel.Cast!);
        }

        OverlaySecondaryText = string.Join(" • ", parts);
    }

    /// <summary>
    /// Starts a new user playback intent and invalidates every pending async playback path
    /// (resume dialogs, delayed VLC starts, health-check retries, stale URL resolution, etc.).
    /// Call this as soon as the user selects another content item, before showing any resume dialog.
    /// </summary>
    public int BeginPlaybackIntent(bool stopCurrentPlayback = true)
    {
        var requestVersion = Interlocked.Increment(ref _playRequestVersion);
        LogDebug($"BeginPlaybackIntent: requestVersion={requestVersion}, stopCurrentPlayback={stopCurrentPlayback}");

        QueueCurrentPlaybackExitSnapshot();
        CancelResumeDialog();
        _isStartingOver = false;
        _oldResumePosition = 0;
        _watchHistoryTimer.Stop();
        _pendingResumeSeekPosition = 0;
        _pendingResumeSeekAttempts = 0;
        _lastPausedPosition = 0;
        _lastPausedTimeMs = 0;
        _lastSeekTargetMs = -1;
        _prematureEndRecoveryCount = 0;
        _isPlaybackEnded = false;
        _isContentTransitioning = false;
        _isIntentionallyPaused = false;
        _livePauseRequiresHardRestart = false;
        PlayerLoadingWarningMessage = string.Empty;
        CurrentSubtitleText = string.Empty;

        if (stopCurrentPlayback)
        {
            try
            {
                VideoPlayerService.Stop();
            }
            catch (Exception ex)
            {
                LogDebug($"BeginPlaybackIntent: VideoPlayerService.Stop failed: {ex.Message}");
            }
        }

        IsPlaying = false;
        IsBuffering = false;
        BufferingProgress = 0;
        OnPropertyChanged(nameof(IsBufferShieldVisible));

        return requestVersion;
    }

    private void QueueCurrentPlaybackExitSnapshot()
    {
        var snapshot = EpisodeNavigator.CreatePlaybackExitSnapshot();
        if (snapshot.Channel is null ||
            snapshot.Channel.Type == ChannelType.Live ||
            snapshot.PositionSeconds <= 0)
        {
            return;
        }

        var flushTask = EpisodeNavigator.FlushPlaybackExitSnapshotAsync(snapshot);
        lock (_playbackExitFlushSync)
        {
            _pendingPlaybackExitFlush = Task.WhenAll(
                _pendingPlaybackExitFlush,
                flushTask);
        }
    }

    internal async Task FlushPendingPlaybackExitSnapshotAsync()
    {
        Task pendingFlush;
        lock (_playbackExitFlushSync)
        {
            pendingFlush = _pendingPlaybackExitFlush;
        }

        await pendingFlush.ConfigureAwait(false);

        lock (_playbackExitFlushSync)
        {
            if (ReferenceEquals(_pendingPlaybackExitFlush, pendingFlush))
            {
                _pendingPlaybackExitFlush = Task.CompletedTask;
            }
        }
    }

    public int PreemptCurrentPlayback() => BeginPlaybackIntent();

    public bool IsPlaybackIntentCurrent(int requestVersion)
        => requestVersion == Volatile.Read(ref _playRequestVersion);

    internal bool IsPlaybackIntentCurrent(int requestVersion, Channel? channel)
    {
        if (!IsPlaybackIntentCurrent(requestVersion))
        {
            return false;
        }

        // Before PlayChannelAsync assigns CurrentChannel, it may still point to the previous item.
        // Once it is assigned, a different channel means this request is stale.
        if (channel != null && CurrentChannel != null && CurrentChannel.Id != channel.Id)
        {
            return false;
        }

        return true;
    }

    internal void PrepareForContentLoading()
    {
        CancelResumeDialog();
        _isContentTransitioning = true;
        _isPlaybackEnded = false;
        _isUserSeeking = false;
        _pendingResumeSeekPosition = 0;
        _pendingResumeSeekAttempts = 0;
        _lastPausedPosition = 0;
        _lastPausedTimeMs = 0;
        _lastSeekTargetMs = -1;
        _prematureEndRecoveryCount = 0;
        _isIntentionallyPaused = false;
        _lastPrematureEndRecoveryUtc = DateTime.MinValue;
        Interlocked.Increment(ref _seekShieldSuppressionToken);
        _suppressBufferShieldForSeek = false;
        PlaybackController.ResetSeekInteractionState();
        IsPlaying = false;
        Position = 0;
        BufferedPosition = 0;
        PositionText = "00:00:00";
        Duration = 0;
        DurationText = "00:00:00";
        ResetPlaybackRate();
        RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        IsBuffering = true;
        BufferingProgress = 0;
        
        PlayerLoadingWarningMessage = string.Empty;

        // Yeni içerik yüklenirken eski state sızıntısını önle
        ResumePositionText = string.Empty;
        _sessionPlaybackStartTimeUtc = DateTime.MinValue;

        OnPropertyChanged(nameof(IsBufferShieldVisible));
    }

    internal EpgProgram GetFallbackProgram()
    {
        var now = DateTime.UtcNow;
        return new EpgProgram 
        { 
            Title = _localizationService.GetString("Player.Epg.NoInfo"),
            StartTime = now,
            EndTime = now.AddHours(1),
            Description = _localizationService.GetString("Player.Status.NoEpgInfo")
        };
    }

    private async Task CheckForEpgUpdateAsync()
    {
        if (CurrentChannel == null || !IsLiveContent || CurrentProgram == null) return;

        bool isFallback = string.Equals(CurrentProgram.Title, _localizationService.GetString("Player.Epg.NoInfo"), StringComparison.OrdinalIgnoreCase);

        if (DateTime.UtcNow > CurrentProgram.EndTime || isFallback)
        {
            var newProgram = await _epgService.GetCurrentProgramAsync(CurrentChannel);
            newProgram ??= GetFallbackProgram();

            if (newProgram.Title != CurrentProgram.Title)
            {
                _dispatcherService.Invoke(() => CurrentProgram = newProgram);
            }
        }
    }

    internal Task EnsurePlaybackHealthAsync(Channel channel, int requestVersion)
        => StallDetector.EnsurePlaybackHealthAsync(channel, requestVersion);

    internal Task TrackWatchHistoryAsync()
        => EpisodeNavigator.TrackWatchHistoryAsync();

    internal Task FlushWatchHistoryAsync(bool force, TimeSpan? incrementDelta = null)
        => EpisodeNavigator.FlushWatchHistoryAsync(force, incrementDelta);

    internal void UpdateMediaInfo()
        => QualityMonitor.UpdateMediaInfo();

    internal Task RefreshTracksWithRetryAsync()
        => QualityMonitor.RefreshTracksWithRetryAsync();

    internal void RestartAutoHideTimer()
        => OverlayManager.RestartAutoHideTimer();

    private bool CanAutoHideOverlay()
        => OverlayManager.CanAutoHideOverlay();

    internal void CheckIntroCreditsPosition(double pos)
        => EpisodeNavigator.CheckIntroCreditsPosition(pos);

    internal void RefreshEpisodeBrowserContext(Series? series)
        => EpisodeNavigator.RefreshEpisodeBrowserContext(series);

    internal void UpdateDurationFromService(bool force = false)
        => QualityMonitor.UpdateDurationFromService(force);

    // ── Command Forwarding (Maintains 100% Backward Compatibility) ───────────
    [RelayCommand]
    private async Task PlayPause() => await PlaybackController.PlayPause();

    [RelayCommand]
    private async Task Stop() => await PlaybackController.Stop();

    [RelayCommand]
    private void ToggleMute() => PlaybackController.ToggleMute();

    [RelayCommand]
    private void StartSeeking() => PlaybackController.StartSeeking();

    [RelayCommand]
    private void Seek(double position)
    {
        LogDebug($"UI Action: Seek requested (Position={position:F1})");
        PlaybackController.Seek(position);
    }

    [RelayCommand]
    private void SkipForward(object? parameter) => PlaybackController.SkipForward(parameter);

    [RelayCommand]
    private void SkipBackward(object? parameter) => PlaybackController.SkipBackward(parameter);

    [RelayCommand]
    private void ShowOverlay() => OverlayManager.ShowOverlay();

    /// <summary>
    /// Mobil: video yüzeyine tek dokunuşta kontrol katmanını aç/kapat.
    /// Görünürse anında gizler; gizliyse gösterir ve otomatik gizleme sayacını kurar.
    /// Kilitliyken kontroller açılmaz, yalnızca kilit ipucu kısa süre belirir.
    /// </summary>
    [RelayCommand]
    private void ToggleControls()
    {
        if (IsLocked)
        {
            ShowLockIndicatorBriefly();
            return;
        }

        if (IsVisible)
        {
            // Görünür -> anında gizle (immersion).
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = false;
        }
        else
        {
            // Gizli -> göster ve otomatik gizleme sayacını yeniden başlat.
            OverlayManager.RestartAutoHideTimer();
        }
    }

    [RelayCommand]
    private void ToggleLock() => OverlayManager.ToggleLock();

    internal void ShowLockIndicatorBriefly()
    {
        _lockIndicatorVisibilityCts?.Cancel();
        _lockIndicatorVisibilityCts?.Dispose();
        _lockIndicatorVisibilityCts = new CancellationTokenSource();

        IsLockIndicatorVisible = false;
        IsLockIndicatorVisible = true;
        _ = HideLockIndicatorAfterDelayAsync(_lockIndicatorVisibilityCts.Token);
    }

    private async Task HideLockIndicatorAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(2500, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                IsLockIndicatorVisible = false;
        }
        catch (OperationCanceledException)
        {
            // A newer tap restarted the complete visibility window.
        }
    }

    public void Unlock()
    {
        _lockIndicatorVisibilityCts?.Cancel();
        _lockIndicatorVisibilityCts?.Dispose();
        _lockIndicatorVisibilityCts = null;
        IsLocked = false;
        IsLockIndicatorVisible = false;
        OverlayManager.RestartAutoHideTimer();
    }

    [RelayCommand]
    private void OpenAudioSettings() => OverlayManager.OpenAudioSettings();

    [RelayCommand]
    private void OpenQualitySettings() => OverlayManager.OpenQualitySettings();

    [RelayCommand]
    private void OpenInfoPanel() => OverlayManager.OpenInfoPanel();

    [RelayCommand]
    private void ClosePanels() => OverlayManager.ClosePanels();

    public void CloseAllPanels() => OverlayManager.ClosePanels();

    [RelayCommand]
    private void ShowSleepTimerMenu() => OverlayManager.ShowSleepTimerMenu();

    [RelayCommand]
    private void SetSleepTimer(SleepTimerOption mode) => OverlayManager.SetSleepTimer(mode);

    [RelayCommand]
    private void CancelSleepTimer() => OverlayManager.CancelSleepTimer();

    [RelayCommand]
    private async Task PlayNextEpisode() => await EpisodeNavigator.PlayNextEpisode();

    [RelayCommand(CanExecute = nameof(CanDownloadCurrentContent))]
    private async Task DownloadCurrentContentAsync() => await EpisodeNavigator.DownloadCurrentContentAsync();

    [RelayCommand(CanExecute = nameof(CanPlayEpisodeFromOverlay))]
    private void PlayEpisodeFromOverlay(Episode? episode) => EpisodeNavigator.PlayEpisodeFromOverlay(episode);

    private bool CanPlayEpisodeFromOverlay(Episode? episode) => EpisodeNavigator.CanPlayEpisodeFromOverlay(episode);

    [RelayCommand]
    private void SelectEpisodeSeason(Season? season)
    {
        if (season is null || ReferenceEquals(SelectedEpisodeSeason, season))
        {
            return;
        }

        SelectedEpisodeSeason = season;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleSize(string size) => SettingsAdapter.SetSubtitleSize(size);

    [RelayCommand]
    private void SetSubtitleBackground(string opacity) => SettingsAdapter.SetSubtitleBackground(opacity);

    [RelayCommand]
    private void SetSubtitlePosition(string margin) => SettingsAdapter.SetSubtitlePosition(margin);

    [RelayCommand]
    private void CycleVideoFillMode() => SettingsAdapter.CycleVideoFillMode();

    [RelayCommand]
    private async Task ToggleLiveFavoriteAsync()
    {
        var channel = CurrentChannel;
        if (channel == null) return;

        try
        {
            await _mainViewModel.ToggleFavoriteCommand.ExecuteAsync(channel);
            if (CurrentChannel?.Id == channel.Id)
            {
                IsCurrentChannelFavorite = channel.IsFavorite;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"ToggleLiveFavoriteAsync failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenPremiumUpsell() => PremiumUpsellRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void EnterPiP() => PiPRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleFullScreen()
    {
        LogDebug($"UI Action: ToggleFullScreen clicked (Target={!IsFullScreen})");
        IsFullScreen = !IsFullScreen;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetAudioTrack(int id)
    {
        LogDebug($"UI Action: SetAudioTrack clicked (Id={id})");
        _videoPlayerService.SetAudioTrack(id);
        SelectedAudioTrack = id;
        RememberManualTrackSelection(isAudio: true, id);
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void SetSubtitleTrack(int id)
    {
        LogDebug($"UI Action: SetSubtitleTrack clicked (Id={id})");
        _videoPlayerService.SetSubtitleTrack(id);
        SelectedSubtitleTrack = id;
        RememberManualTrackSelection(isAudio: false, id);
        RestartAutoHideTimer();
    }

    /// <summary>
    /// Canlı yayında kullanıcının şimdiki zamana (live edge) atlaması.
    /// Stream gecikince pozisyon duration'a yaklaşır; bu komut doğrudan live edge'e götürür.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoToLive))]
    private async Task GoToLiveAsync()
    {
        if (!IsLiveContent || Duration <= 0)
        {
            return;
        }

        LogDebug("UI Action: GoToLive clicked");
        // Live edge: duration'dan birkaç saniye geri (tam uca gitmek buffer'ı sıfırlayıp takılabilir).
        var liveEdge = Math.Max(0, Duration - 3);
        await _videoPlayerService.HardSeekAsync(liveEdge);
        Position = liveEdge;
        RestartAutoHideTimer();
    }

    private bool CanGoToLive() => IsLiveContent && Duration > 0;

    /// <summary>
    /// Altyazı hızlı aç/kapat: track seçiliyse kapat (-1), kapalıysa son seçili track'i geri yükle.
    /// </summary>
    [RelayCommand]
    private void ToggleSubtitleTrack()
    {
        if (SelectedSubtitleTrack >= 0)
        {
            SetSubtitleTrack(-1);
        }
        else
        {
            // En son kullanılan altyazı track'ini geri yükle; yoksa ilk可用 track'i seç.
            var key = BuildTrackPreferenceKey();
            var trackId = -1;
            if (!string.IsNullOrWhiteSpace(key) &&
                _trackSelectionsByContent.TryGetValue(key, out var snapshot) &&
                snapshot.SubtitleTrackId.HasValue)
            {
                trackId = snapshot.SubtitleTrackId.Value;
            }
            else
            {
                var firstAvailable = SubtitleTracks.FirstOrDefault(t => t.Id >= 0);
                trackId = firstAvailable?.Id ?? -1;
            }

            if (trackId >= 0)
            {
                SetSubtitleTrack(trackId);
            }
        }
    }

    internal void RaiseTrackSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedAudioTrackName));
        OnPropertyChanged(nameof(SelectedSubtitleTrackName));
    }

    internal (bool AudioApplied, bool SubtitleApplied) TryApplyRememberedTrackSelection(
        IReadOnlyList<TrackOption> audioTracks,
        IReadOnlyList<TrackOption> subtitleTracks)
    {
        var key = BuildTrackPreferenceKey();
        if (string.IsNullOrWhiteSpace(key) || !_trackSelectionsByContent.TryGetValue(key, out var snapshot))
        {
            return (false, false);
        }

        var audioApplied = false;
        if (snapshot.AudioTrackId.HasValue)
        {
            var audio = FindRememberedTrack(audioTracks, snapshot.AudioTrackId.Value, snapshot.AudioTrackName, snapshot.AudioLanguageCode);
            if (audio != null)
            {
                _videoPlayerService.SetAudioTrack(audio.Id);
                SelectedAudioTrack = audio.Id;
                audioApplied = true;
            }
        }

        var subtitleApplied = false;
        if (snapshot.SubtitleTrackId.HasValue)
        {
            if (snapshot.SubtitleTrackId.Value < 0)
            {
                _videoPlayerService.SetSubtitleTrack(-1);
                SelectedSubtitleTrack = -1;
                subtitleApplied = true;
            }
            else
            {
                var subtitle = FindRememberedTrack(subtitleTracks, snapshot.SubtitleTrackId.Value, snapshot.SubtitleTrackName, snapshot.SubtitleLanguageCode);
                if (subtitle != null)
                {
                    _videoPlayerService.SetSubtitleTrack(subtitle.Id);
                    SelectedSubtitleTrack = subtitle.Id;
                    subtitleApplied = true;
                }
            }
        }

        return (audioApplied, subtitleApplied);
    }

    private void RememberManualTrackSelection(bool isAudio, int id)
    {
        var key = BuildTrackPreferenceKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!_trackSelectionsByContent.TryGetValue(key, out var snapshot))
        {
            snapshot = new TrackSelectionSnapshot();
            _trackSelectionsByContent[key] = snapshot;
        }

        var option = isAudio
            ? AudioTracks.FirstOrDefault(t => t.Id == id)
            : SubtitleTracks.FirstOrDefault(t => t.Id == id);

        if (isAudio)
        {
            snapshot.AudioTrackId = id;
            snapshot.AudioTrackName = option?.Name;
            snapshot.AudioLanguageCode = option?.LanguageCode ?? ExtractTrackLanguageCode(option?.Name);
        }
        else
        {
            snapshot.SubtitleTrackId = id;
            snapshot.SubtitleTrackName = option?.Name;
            snapshot.SubtitleLanguageCode = id >= 0 ? option?.LanguageCode ?? ExtractTrackLanguageCode(option?.Name) : null;
        }

        RaiseTrackSelectionPropertiesChanged();
        _ = PersistTrackLanguagePreferenceAsync(isAudio, id, option);
    }

    private async Task PersistTrackLanguagePreferenceAsync(bool isAudio, int id, TrackOption? option)
    {
        try
        {
            var settings = _settingsService.Settings;
            var languageCode = option?.LanguageCode ?? ExtractTrackLanguageCode(option?.Name);

            if (isAudio)
            {
                if (!string.IsNullOrWhiteSpace(languageCode))
                {
                    settings.PreferredAudioLanguage = languageCode!;
                    await _settingsService.SaveAsync();
                }
                return;
            }

            settings.SubtitleEnabled = id >= 0;
            if (id >= 0 && !string.IsNullOrWhiteSpace(languageCode))
            {
                settings.SubtitleLanguage = languageCode!;
            }

            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            LogDebug($"PersistTrackLanguagePreferenceAsync failed: {ex.Message}");
        }
    }

    private string? BuildTrackPreferenceKey()
    {
        var channel = CurrentChannel;
        if (channel == null)
        {
            return null;
        }

        if (channel.Type == ChannelType.Series && !string.IsNullOrWhiteSpace(CurrentEpisodeIdentity))
        {
            return $"series:{channel.PlaylistId}:{channel.Id}:{CurrentEpisodeIdentity}";
        }

        if (channel.Id > 0)
        {
            return $"{channel.Type}:{channel.PlaylistId}:{channel.Id}";
        }

        return !string.IsNullOrWhiteSpace(channel.StreamUrl)
            ? $"{channel.Type}:url:{channel.StreamUrl.Trim()}"
            : null;
    }

    private static TrackOption? FindRememberedTrack(
        IReadOnlyList<TrackOption> tracks,
        int rememberedId,
        string? rememberedName,
        string? rememberedLanguageCode)
    {
        var exactId = tracks.FirstOrDefault(t => t.Id == rememberedId);
        if (exactId != null && TrackLabelsEquivalent(exactId.Name, rememberedName))
        {
            return exactId;
        }

        if (!string.IsNullOrWhiteSpace(rememberedName))
        {
            var exactName = tracks.FirstOrDefault(t => TrackLabelsEquivalent(t.Name, rememberedName));
            if (exactName != null)
            {
                return exactName;
            }
        }

        if (!string.IsNullOrWhiteSpace(rememberedLanguageCode))
        {
            return tracks.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, rememberedLanguageCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ExtractTrackLanguageCode(t.Name), rememberedLanguageCode, StringComparison.OrdinalIgnoreCase));
        }

        return exactId;
    }

    private static bool TrackLabelsEquivalent(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    internal static string? ExtractTrackLanguageCode(string? trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName))
        {
            return null;
        }

        var normalized = trackName.Trim();
        var match = Regex.Match(normalized, @"(?:\(|\[|\b)(tr|en|de|fr|es|it|pt|ru|ar|nl)(?:\)|\]|\b)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.ToLowerInvariant();
        }

        var lower = normalized.ToLowerInvariant();
        var languageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["turkish"] = "tr",
            ["türkçe"] = "tr",
            ["turkce"] = "tr",
            ["english"] = "en",
            ["ingilizce"] = "en",
            ["deutsch"] = "de",
            ["german"] = "de",
            ["almanca"] = "de",
            ["french"] = "fr",
            ["français"] = "fr",
            ["fransızca"] = "fr",
            ["spanish"] = "es",
            ["español"] = "es",
            ["ispanyolca"] = "es",
            ["italian"] = "it",
            ["italiano"] = "it",
            ["italyanca"] = "it",
            ["portuguese"] = "pt",
            ["português"] = "pt",
            ["portekizce"] = "pt",
            ["russian"] = "ru",
            ["русский"] = "ru",
            ["rusça"] = "ru",
            ["arabic"] = "ar",
            ["العربية"] = "ar",
            ["arapça"] = "ar",
            ["dutch"] = "nl",
            ["nederlands"] = "nl",
            ["flemenkçe"] = "nl"
        };

        foreach (var pair in languageNames)
        {
            if (lower.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    [RelayCommand]
    private void SetPlaybackSpeed(float speed)
    {
        var normalizedRate = NormalizePlaybackRate(speed);
        _videoPlayerService.PlaybackRate = normalizedRate;
        CurrentPlaybackRate = normalizedRate;
        RestartAutoHideTimer();
    }

    public void UpdateSeekPreview(double positionSeconds)
    {
        if (IsLiveContent ||
            !double.IsFinite(positionSeconds) ||
            !double.IsFinite(Duration) ||
            Duration <= 0)
        {
            ClearSeekPreview();
            return;
        }

        var clampedPosition = Math.Clamp(positionSeconds, 0, Duration);
        SeekPreviewPositionText =
            TimeSpan.FromSeconds(clampedPosition).ToString(@"hh\:mm\:ss");
        IsSeekPreviewActive = true;
    }

    public void ClearSeekPreview()
    {
        IsSeekPreviewActive = false;
        SeekPreviewPositionText = string.Empty;
    }

    private void ResetPlaybackRate()
    {
        _videoPlayerService.PlaybackRate = 1.0f;
        CurrentPlaybackRate = 1.0f;
    }

    private static float NormalizePlaybackRate(float requestedRate)
    {
        foreach (var supportedRate in SupportedPlaybackRates)
        {
            if (Math.Abs(requestedRate - supportedRate) < 0.001f)
            {
                return supportedRate;
            }
        }

        return 1.0f;
    }

    private bool CanClosePlayer() => !IsClosingPlayer;

    [RelayCommand(CanExecute = nameof(CanClosePlayer))]
    private async Task ClosePlayer()
    {
        await _exitGate.WaitAsync();

        try
        {
            if (IsClosingPlayer)
                return;

            IsClosingPlayer = true;

            Interlocked.Increment(ref _playRequestVersion);

            CancelResumeDialog();

            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _watchHistoryTimer.Stop();

        SetMobilePanelState(MobilePanelState.None);
        IsVisible = false;

        var historySnapshot = EpisodeNavigator.CreatePlaybackExitSnapshot();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _videoPlayerService.EndSessionAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            LogDebug($"Player shutdown failed: {ex.Message}");

            try { _videoPlayerService.Stop(); }
            catch { /* Best effort fallback */ }
        }

        try
        {
            await EpisodeNavigator
                .FlushPlaybackExitSnapshotAsync(historySnapshot)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            LogDebug($"Exit history flush failed: {ex.Message}");
        }

        ResetPlayerAfterExit();

        _dispatcherService.Invoke(() =>
            CloseRequested?.Invoke(this, EventArgs.Empty));

        _ = _contentDownloadService.CleanupPlaybackCacheAsync();
        }
        finally
        {
            IsClosingPlayer = false;
            _exitGate.Release();
        }
    }

    private void ResetPlayerAfterExit()
    {
        CurrentChannel = null;
        CurrentProgram = null;

        IsPlaying = false;
        IsBuffering = false;
        BufferingProgress = 0;

        Position = 0;
        BufferedPosition = 0;
        Duration = 0;
        PositionText = "00:00:00";
        DurationText = "00:00:00";
        ResetPlaybackRate();
        RemainingTime = string.Empty;

        IsPiPMode = false;
        IsFullScreen = false;
        IsLocked = false;

        _livePauseRequiresHardRestart = false;
        _lastLiveProgressAtUtc = DateTime.MinValue;
        _lastLivePositionEventAtUtc = DateTime.MinValue;
    }

    [RelayCommand]
    private void PlayNextLiveChannel()
    {
        LogDebug("UI Action: PlayNextLiveChannel clicked");
        NextLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PlayPreviousLiveChannel()
    {
        LogDebug("UI Action: PlayPreviousLiveChannel clicked");
        PreviousLiveChannelRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void UserInteraction()
    {
        if (IsPiPMode)
            IsPiPControlsForceVisible = true;
        RestartAutoHideTimer();
    }

    [RelayCommand]
    private void OpenEpisodes()
    {
        LogDebug("UI Action: OpenEpisodes clicked");
        if (!IsSeriesContent) return;
        if (EpisodeSeasons.Count == 0) return;

        OpenChildPanel(MobilePanelState.Episodes);
        RestartAutoHideTimer();
    }

    // ── EPG Panel Commands ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleEpgPanel()
    {
        LogDebug("UI Action: ToggleEpgPanel clicked");
        if (IsEpgPanelOpen)
        {
            SetMobilePanelState(MobilePanelState.None);
            return;
        }

        // Diğer panel/kilitleri kapat
        SetMobilePanelState(MobilePanelState.Epg);

        await LoadEpgPanelAsync();
    }

    /// <summary>
    /// Canlı kanal listesini yükler ve EPG verilerini hesaplar.
    /// Her kanal için PixelLeft/PixelWidth önceden hesaplanır; AXAML'de saf binding.
    /// </summary>
    internal async Task LoadEpgPanelAsync()
    {
        if (LiveChannelsLoader == null)
        {
            LogDebug("EPG Panel: LiveChannelsLoader is null, cannot load channels.");
            return;
        }

        IsEpgLoading = true;
        EpgFocusRowIndex = -1;
        IsEpgUpdateRequired = false;
        EpgStatusMessage = string.Empty;
        EpgRows.Clear();

        try
        {
            var channels = await LiveChannelsLoader();
            if (channels == null || channels.Count == 0)
            {
                IsEpgUpdateRequired = true;
                EpgStatusMessage = _localizationService.GetString("Player.Epg.NoLiveChannelsInGroup");
                IsEpgLoading = false;
                return;
            }

            var windowStart = DateTime.Now.AddHours(-EpgPastHours);
            var windowEnd   = DateTime.Now.AddHours(EpgFutureHours);
            var nowUtc      = DateTime.UtcNow;

            // EPG ID'lerini topla (TvgId → fallback Name)
            var epgIds = channels
                .SelectMany(c => new[]
                {
                    c.TvgId,
                    c.TvgName,
                    c.Name,
                    c.Id > 0 ? c.Id.ToString() : null
                })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .OfType<string>()
                .Distinct()
                .ToList();

            // Tek sorguda tüm kanalların programlarını çek
            var programsMap = await _epgService.GetProgramsBulkAsync(epgIds, windowStart, windowEnd);
            var maxProgramEndUtc = await _epgService.GetMaxProgramEndTimeAsync();

            if (programsMap.Count == 0 || maxProgramEndUtc == null || maxProgramEndUtc.Value <= DateTime.UtcNow)
            {
                IsEpgUpdateRequired = true;
                EpgStatusMessage = _localizationService.GetString("Player.Epg.UpdateRequired");
            }

            var rows = new List<EpgPanelRow>(channels.Count);
            var focusRowIndex = -1;
            foreach (var channel in channels)
            {
                List<EpgProgram>? programs = null;
                foreach (var epgId in new[]
                {
                    channel.TvgId,
                    channel.TvgName,
                    channel.Name,
                    channel.Id > 0 ? channel.Id.ToString() : null
                })
                {
                    if (!string.IsNullOrWhiteSpace(epgId) && programsMap.TryGetValue(epgId, out programs))
                        break;
                }

                programs ??= [];

                var displayPrograms = MergeAdjacentSameTitlePrograms(programs);

                var blocks = displayPrograms
                    .Select(p =>
                    {
                        var pStart  = p.StartTime.ToLocalTime();
                        var pEnd    = p.EndTime.ToLocalTime();
                        var clipped = pStart < windowStart;
                        var leftMin = Math.Max(0, (pStart - windowStart).TotalMinutes);
                        var widthMin = clipped
                            ? (pEnd - windowStart).TotalMinutes
                            : (pEnd - pStart).TotalMinutes;

                        return new EpgProgramBlock
                        {
                            Program          = p,
                            PixelLeft        = leftMin * EpgPxPerMinute,
                            PixelWidth       = Math.Max(2, widthMin * EpgPxPerMinute),
                            IsCurrentProgram = p.IsNowPlaying,
                            IsClippedLeft    = clipped,
                            IsPast           = p.EndTime < nowUtc
                        };
                    })
                    .OrderBy(b => b.PixelLeft)
                    .ToList();

                var isCurrentChannel = CurrentChannel?.Id == channel.Id;
                if (isCurrentChannel)
                    focusRowIndex = rows.Count;

                rows.Add(new EpgPanelRow
                {
                    Channel          = channel,
                    Blocks           = blocks,
                    IsCurrentChannel = isCurrentChannel
                });
            }

            // UI thread'e taşı
            _dispatcherService.BeginInvoke(() =>
            {
                foreach (var row in rows)
                    EpgRows.Add(row);

                EpgFocusRowIndex = focusRowIndex;
            });
        }
        catch (Exception ex)
        {
            LogDebug($"EPG Panel: Load failed — {ex.Message}");
        }
        finally
        {
            IsEpgLoading = false;
        }
    }

    private static List<EpgProgram> MergeAdjacentSameTitlePrograms(IEnumerable<EpgProgram> programs)
    {
        var ordered = programs
            .Where(p => p.EndTime > p.StartTime)
            .OrderBy(p => p.StartTime)
            .ThenBy(p => p.EndTime)
            .ToList();

        if (ordered.Count <= 1)
            return ordered;

        var merged = new List<EpgProgram>(ordered.Count);
        foreach (var program in ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(CloneEpgProgram(program));
                continue;
            }

            var previous = merged[^1];
            if (CanMergeEpgPrograms(previous, program))
            {
                previous.EndTime = program.EndTime > previous.EndTime ? program.EndTime : previous.EndTime;
                previous.Description ??= program.Description;
                previous.Category ??= program.Category;
                previous.IconUrl ??= program.IconUrl;
                continue;
            }

            merged.Add(CloneEpgProgram(program));
        }

        return merged;
    }

    private static bool CanMergeEpgPrograms(EpgProgram previous, EpgProgram current)
    {
        if (!string.Equals(NormalizeEpgPanelTitle(previous.Title), NormalizeEpgPanelTitle(current.Title), StringComparison.Ordinal))
            return false;

        if (!string.Equals(previous.ChannelId, current.ChannelId, StringComparison.OrdinalIgnoreCase))
            return false;

        var gap = current.StartTime - previous.EndTime;
        return gap.TotalMinutes <= 1;
    }

    private static string NormalizeEpgPanelTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        return string.Join(' ', title.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static EpgProgram CloneEpgProgram(EpgProgram source)
        => new()
        {
            Id = source.Id,
            ChannelId = source.ChannelId,
            Title = source.Title,
            Description = source.Description,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            Category = source.Category,
            IconUrl = source.IconUrl
        };

    partial void OnIsEpgPanelOpenChanged(bool value)
    {
        LogDebug($"UI State: IsEpgPanelOpen={value}");
        if (value)
        {
            _autoHideTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            IsVisible = true;
            return;
        }
        EpgRows.Clear();
        EpgFocusRowIndex = -1;
        IsEpgUpdateRequired = false;
        EpgStatusMessage = string.Empty;
        IsLocked = false;
        PlaybackController.CancelSeekBufferShieldSuppression();
        RestartAutoHideTimer();
    }

    // ────────────────────────────────────────────────────────────────────────

    // ── Resume Dialog ───────────────────────────────────────────────────────
    private TaskCompletionSource<bool>? _resumeDialogTcs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveMobilePanelState))]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsMobileDetailPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsBottomControlsVisible))]
    [NotifyPropertyChangedFor(nameof(IsMobileCompactControlsVisible))]
    private bool _isResumeDialogVisible;
    [ObservableProperty] private string _resumePositionText = string.Empty;
    [ObservableProperty] private bool _isPremiumResume;

    partial void OnIsResumeDialogVisibleChanged(bool value)
    {
        if (value)
        {
            EnsureResumePositionText();
        }
    }

    partial void OnResumePositionTextChanged(string value)
    {
        if (IsResumeDialogVisible && string.IsNullOrWhiteSpace(value))
        {
            EnsureResumePositionText();
        }
    }

    private void EnsureResumePositionText()
    {
        if (!IsResumeDialogVisible || !string.IsNullOrWhiteSpace(ResumePositionText))
        {
            return;
        }

        ResumePositionText = FormatResumePosition(_oldResumePosition);
    }

    private static string FormatResumePosition(double positionSeconds)
    {
        if (double.IsNaN(positionSeconds) || double.IsInfinity(positionSeconds) || positionSeconds <= 0)
        {
            return "00:00:00";
        }

        return TimeSpan.FromSeconds(positionSeconds).ToString(@"hh\:mm\:ss");
    }

    public Task<bool> ShowResumeDialogAsync(double positionSeconds)
    {
        // A new dialog replaces any older unanswered dialog. This prevents an old
        // selection flow from being completed after the user has already selected
        // a different content item.
        CancelResumeDialog();

        _oldResumePosition = positionSeconds;
        ResumePositionText = FormatResumePosition(positionSeconds);
        IsPremiumResume = _licenseService.IsFeatureAvailable(Noctra.Services.LicenseService.Features.ResumePlayback);
        IsResumeDialogVisible = true;
        _resumeDialogTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _resumeDialogTcs.Task;
    }

    public async Task<bool> ShowResumeDialogAsync(double positionSeconds, CancellationToken cancellationToken)
    {
        var dialogTask = ShowResumeDialogAsync(positionSeconds);
        using var registration = cancellationToken.Register(CancelResumeDialog);
        return await dialogTask.ConfigureAwait(false);
    }

    public void SetResumePosition(double seconds)
    {
        _lastPausedPosition = seconds;
        _lastPausedTimeMs = (long)(seconds * 1000);
    }

    public void CancelResumeDialog()
    {
        var pendingDialog = _resumeDialogTcs;
        var hadActiveDialog = pendingDialog is not null || IsResumeDialogVisible;
        _resumeDialogTcs = null;

        if (IsResumeDialogVisible)
        {
            IsResumeDialogVisible = false;
        }

        pendingDialog?.TrySetCanceled();
        ResumePositionText = string.Empty;

        if (hadActiveDialog)
        {
            _oldResumePosition = 0;
        }
    }

    internal bool _isStartingOver;
    internal double _oldResumePosition;

    private void CompleteResumeDialog(bool resumeFromSavedPosition)
    {
        _isStartingOver = !resumeFromSavedPosition;

        var pendingDialog = _resumeDialogTcs;
        _resumeDialogTcs = null;
        IsResumeDialogVisible = false;
        ResumePositionText = string.Empty;

        pendingDialog?.TrySetResult(resumeFromSavedPosition);
    }

    [RelayCommand]
    private void ResumeFromPosition()
    {
        IsPremiumResume = _licenseService.IsFeatureAvailable(Noctra.Services.LicenseService.Features.ResumePlayback);
        if (!IsPremiumResume)
        {
            PremiumUpsellRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        CompleteResumeDialog(resumeFromSavedPosition: true);
    }

    [RelayCommand]
    private void StartFromBeginning()
    {
        CompleteResumeDialog(resumeFromSavedPosition: false);
    }

    [RelayCommand]
    private void ReturnFromResumeDialog()
    {
        CancelResumeDialog();
    }

    // ── Property / State Changed Interceptions ──────────────────────────────
    partial void OnCurrentChannelChanged(Channel? value)
    {
        IsCurrentChannelFavorite = value?.IsFavorite ?? false;
        DownloadStatusMessage = string.Empty;
        IsDownloadInProgress = false;
        Interlocked.Exchange(ref _isDownloadActionRunning, 0);
        IsDownloadedPlayback = PlayerEpisodeNavigator.LooksLikeDownloadedPlaybackStreamUrl(value?.StreamUrl);

        if (value != null)
        {
            PlaybackController.CancelSeekBufferShieldSuppression();
            IsLiveContent = value.Type == ChannelType.Live;
            _livePauseRequiresHardRestart = false;
            _isIntentionallyPaused = false;
            _lastLiveProgressAtUtc = DateTime.UtcNow;
            _lastLivePositionEventAtUtc = DateTime.UtcNow;
            _liveRecoveryWindowStartUtc = DateTime.MinValue;
            _isPlaybackEnded = false;
            PlaybackController.ResetSeekInteractionState();
            Position = 0;
            BufferedPosition = 0;
            PositionText = "00:00:00";
            Duration = 0;
            DurationText = "00:00:00";
            RemainingTime = IsLiveContent ? "00:00:00" : "-00:00:00";
        }

        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(HasCurrentProgramInfo));
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        RaiseInfoPanelMetadataChanged();
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(CanShowGoToLiveButton));
        GoToLiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentProgramChanged(EpgProgram? value)
    {
        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(HasCurrentProgramInfo));
    }

    partial void OnStreamQualityChanged(StreamQualityInfo? value)
    {
        OnPropertyChanged(nameof(HasTopQualityBadgesReady));
        OnPropertyChanged(nameof(QualityResolutionText));
        OnPropertyChanged(nameof(QualityFpsText));
        OnPropertyChanged(nameof(QualityVideoCodecText));
        OnPropertyChanged(nameof(QualityVideoBitrateText));
        OnPropertyChanged(nameof(QualityAudioText));
        QualityMonitor.UpdateStreamInfoFromQuality();
    }

    partial void OnIsLiveContentChanged(bool value)
    {
        UpdateOverlaySecondaryText();
        OnPropertyChanged(nameof(IsBufferShieldVisible));
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        OnPropertyChanged(nameof(CanShowGoToLiveButton));
        RaiseInfoPanelMetadataChanged();
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
        GoToLiveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSeriesContentChanged(bool value)
    {
        RaiseInfoPanelMetadataChanged();
        SettingsAdapter.OnLanguageChanged();
    }

    partial void OnIsDownloadedPlaybackChanged(bool value)
    {
        if (value)
        {
            IsInfoPanelOpen = false;
        }
        OnPropertyChanged(nameof(CanShowDownloadButton));
        OnPropertyChanged(nameof(CanShowInfoButton));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDownloadInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(DownloadButtonText));
        OnPropertyChanged(nameof(CanDownloadCurrentContent));
        DownloadCurrentContentCommand.NotifyCanExecuteChanged();
    }

    partial void OnVolumeChanged(int value)
    {
        if (_isUpdatingFromService)
        {
            return;
        }

        _videoPlayerService.Volume = value;
        if (value > 0)
        {
            _volumeBeforeMute = value;
        }

        if (value > 0 && IsMuted)
        {
            IsMuted = false;
        }
        else if (value == 0 && !IsMuted)
        {
            IsMuted = true;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        _videoPlayerService.IsMuted = value;
    }

    partial void OnIsLockedChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsAudioSettingsOpenChanged(bool value)
    {
        LogDebug($"UI State: IsAudioSettingsOpen={value}");
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsQualitySettingsOpenChanged(bool value)
    {
        LogDebug($"UI State: IsQualitySettingsOpen={value}");
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsInfoPanelOpenChanged(bool value)
    {
        LogDebug($"UI State: IsInfoPanelOpen={value}");
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsBufferingChanged(bool value)
    {
        LogDebug($"UI State: IsBuffering={value}");
        OnPropertyChanged(nameof(IsBufferShieldVisible));

        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsNextEpisodePromptVisibleChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsEpisodesPanelOpenChanged(bool value)
    {
        LogDebug($"UI State: IsEpisodesPanelOpen={value}");
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        PlaybackController.CancelSeekBufferShieldSuppression();
        RestartAutoHideTimer();
    }

    partial void OnIsDraggingChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    partial void OnIsResizingChanged(bool value)
    {
        if (value)
        {
            _autoHideTimer.Change(Timeout.Infinite, Timeout.Infinite);
            IsVisible = true;
            return;
        }
        RestartAutoHideTimer();
    }

    // ── Infrastructure Event Bridge ──────────────────────────────────────────
    private void OnVideoPlayerServicePlayingChanged(object? s, bool playing)
        => PlaybackController.OnVideoPlayerServicePlayingChanged(s, playing);

    private void OnVideoPlayerServicePlaybackEnded(object? sender, EventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            if (_isContentTransitioning) return;

            _isPlaybackEnded = true;

            if (SleepTimerMode == SleepTimerOption.EndOfEpisode)
            {
                _ = Task.Delay(1500).ContinueWith(_ =>
                    _dispatcherService.BeginInvoke(OverlayManager.TriggerSleepShutdown));
            }

            var duration = _videoPlayerService.Duration;
            var currentPos = Position;
            
            bool isPrematureEnd = (IsLiveContent) || (!IsLiveContent && duration > 0 && (duration - currentPos) > 10);

            if (isPrematureEnd)
            {
                var now = DateTime.UtcNow;
                if (now - _lastPrematureEndRecoveryUtc > TimeSpan.FromSeconds(30))
                    _prematureEndRecoveryCount = 0;

                if (_prematureEndRecoveryCount >= MaxPrematureEndRecoveries)
                {
                    LogDebug($"VM: PREMATURE END recovery limit reached ({MaxPrematureEndRecoveries}). Giving up.");
                    ConnectionStatus = _localizationService.GetString("Player.Status.Unstable");
                    return;
                }

                if (now - _lastPrematureEndRecoveryUtc < PrematureEndRecoveryCooldown)
                {
                    LogDebug("VM: PREMATURE END cooldown active, skipping recovery.");
                    return;
                }

                _prematureEndRecoveryCount++;
                _lastPrematureEndRecoveryUtc = now;
                var lastValid = _lastKnownValidPosition > 1 ? _lastKnownValidPosition : currentPos;
                LogDebug($"VM: PREMATURE END DETECTED. Live: {IsLiveContent}, Pos/Dur: {currentPos}/{duration}s (Valid: {lastValid}). Suspected server truncation. Attempt {_prematureEndRecoveryCount}/{MaxPrematureEndRecoveries}");
                
                _ = StallDetector.AutoRecoverPrematureEndAsync(lastValid);
                return;
            }
            
            EpisodeNavigator.TryShowNextEpisodePromptAtEnd();
        });
    }

    private void OnVideoPlayerServiceQualityDetected(object? s, StreamQualityInfo quality)
        => QualityMonitor.OnVideoPlayerServiceQualityDetected(s, quality);

    private void OnVideoPlayerServiceBufferingChanged(object? s, float progress)
        => PlaybackController.OnVideoPlayerServiceBufferingChanged(s, progress);

    private void OnVideoPlayerServiceErrorOccurred(object? s, string errorMessage)
    {
        _dispatcherService.Invoke(() =>
        {
            PlayerLoadingWarningMessage = string.Empty;
            ConnectionStatus = errorMessage;
            IsBuffering = true;
            BufferingProgress = 0;
        });
    }

    private void OnVideoPlayerServicePositionChanged(object? s, double pos)
        => PlaybackController.OnVideoPlayerServicePositionChanged(s, pos);

    private void OnVideoPlayerServiceVolumeChanged(object? s, int vol)
        => PlaybackController.OnVideoPlayerServiceVolumeChanged(s, vol);

    private void OnVideoPlayerServiceSubtitleTextChanged(object? s, string? text)
        => _dispatcherService.BeginInvoke(() => CurrentSubtitleText = text ?? string.Empty);

    private void OnNetworkStatusChanged(object? sender, string status)
        => StallDetector.OnNetworkStatusChanged(sender, status);

    private void OnSettingsChanged()
        => SettingsAdapter.OnSettingsChanged();

    private void OnLanguageChanged()
        => SettingsAdapter.OnLanguageChanged();

    public void SetCurrentEpisode(Episode? episode, Episode? nextEpisode = null, Series? series = null)
        => EpisodeNavigator.SetCurrentEpisode(episode, nextEpisode, series);

    public Task PlayChannelAsync(Channel channel, double? startPosition = null, int? existingRequestVersion = null)
        => PlaybackController.PlayChannelAsync(channel, startPosition, existingRequestVersion);

    private static string FormatBitrate(int bitrateKbps)
    {
        if (bitrateKbps >= 1_000) return $"{(bitrateKbps / 1_000.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} Mbps";
        return $"{bitrateKbps} Kbps";
    }

    internal void ApplyVideoFillMode()
    {
        try
        {
            var aspectRatio = VideoFillMode switch
            {
                FillMode.Fill => "16:9",
                FillMode.Stretch => "16:9",
                FillMode.Original => (StreamQuality != null && StreamQuality.Width > 0 && StreamQuality.Height > 0)
                    ? $"{StreamQuality.Width}:{StreamQuality.Height}"
                    : null,
                _ => null
            };
            _videoPlayerService.SetVideoLayout(
                aspectRatio,
                VideoFillMode == FillMode.Fill ? "16:9" : null);
            
            LogDebug($"VM: VideoFillMode applied: {VideoFillMode}");
        }
        catch (Exception ex)
        {
            LogDebug($"VM: Error applying VideoFillMode: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_videoPlayerService != null)
        {
            _videoPlayerService.PlayingChanged -= OnVideoPlayerServicePlayingChanged;
            _videoPlayerService.PlaybackEnded -= OnVideoPlayerServicePlaybackEnded;
            _videoPlayerService.QualityDetected -= OnVideoPlayerServiceQualityDetected;
            _videoPlayerService.BufferingChanged -= OnVideoPlayerServiceBufferingChanged;
            _videoPlayerService.ErrorOccurred -= OnVideoPlayerServiceErrorOccurred;
            _videoPlayerService.PositionChanged -= OnVideoPlayerServicePositionChanged;
            _videoPlayerService.VolumeChanged -= OnVideoPlayerServiceVolumeChanged;
            _videoPlayerService.SubtitleTextChanged -= OnVideoPlayerServiceSubtitleTextChanged;
        }

        if (_licenseService != null)
        {
            _licenseService.SubscriptionChanged -= OnLicenseServiceSubscriptionChanged;
        }

        _autoHideTimer?.Dispose();
        _clockTimer?.Dispose();
        _watchHistoryTimer?.Dispose();
        _unreachableWarningTimer?.Dispose();

        _sleepCountdownCts?.Cancel();
        _sleepCountdownCts?.Dispose();
        _sleepCountdownCts = null;

        _lockIndicatorVisibilityCts?.Cancel();
        _lockIndicatorVisibilityCts?.Dispose();
        _lockIndicatorVisibilityCts = null;

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }
        if (_networkService != null)
        {
            _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        }
        _localizationService.LanguageChanged -= OnLanguageChanged;
    }
}
