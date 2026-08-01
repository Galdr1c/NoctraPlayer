namespace Noctra.Mobile.Controls;

/// <summary>
/// Named z-index contract for the mobile visual tree.
///
/// Keep the numeric bands stable: player-local layers live below 1,000,
/// page sheets use 1,000, shell overlays use 47,000-55,000, and the
/// edge feedback stays below shell overlays at 46,000.
/// XAML should reference these names instead of embedding numeric values.
/// </summary>
public static class MobileZIndex
{
    // Player-local layers (0-999).
    public const int PlayerWatermark = 10;
    public const int PlayerSubtitle = 15;
    public const int PlayerOverlay = 20;
    public const int PlayerControls = 21;
    public const int PlayerBufferShield = 25;
    public const int PlayerMessage = 30;
    public const int PlayerVolumeToast = 40;
    public const int PlayerSeekToast = 41;
    public const int PlayerGestureToast = 43;
    public const int PlayerLockIndicator = 50;
    public const int PlayerNextEpisode = 70;
    public const int PlayerEpgPanel = 90;
    public const int PlayerSheet = 100;
    // Download progress must remain visible when a player sheet is open.
    public const int PlayerDownloadToast = 110;
    public const int PlayerUpsell = 200;

    // View-local selection sheets (kept above page content, below shell overlays).
    public const int SelectionSheet = 1_000;
    public const int PageStatusToast = 1_050;

    // EPG canvas adorners.
    public const int EpgNowLine = 10;
    public const int EpgNowBadge = 11;
    public const int EpgChannelOverlay = 20;

    // Main shell overlays. Keep the order explicit because these share one root Grid.
    public const int ShellCategorySelection = 47_000;
    public const int ShellCardActions = 47_500;
    public const int ShellPlayer = 48_000;
    public const int ShellTransientOverlay = 49_000;
    public const int ShellExitToast = ShellTransientOverlay;
    public const int ShellSearchProgress = ShellTransientOverlay;
    public const int ShellCategoryUndoToast = ShellTransientOverlay;
    public const int ShellCategoryErrorToast = 49_001;
    public const int ShellLegalConsent = 50_000;
    public const int ShellReviewPrompt = 52_000;
    public const int ShellProfiles = 55_000;

    // Root-level decorative feedback sits above normal page content and
    // below shell-owned sheets, player, toasts, and modal overlays. Page-local
    // overlays remain responsible for their own stacking inside a page.
    public const int ShellEdgeFeedback = 46_000;
}
