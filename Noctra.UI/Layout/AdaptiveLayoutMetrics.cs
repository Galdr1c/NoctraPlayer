namespace Noctra.UI.Layout;

public readonly record struct AdaptiveLayoutMetrics(
    AdaptiveLayoutClass LayoutClass,
    AdaptiveNavigationMode Navigation,
    double PagePadding,
    double ContentSpacing,
    double MinimumCardWidth,
    double MaximumSheetWidth,
    double MinimumTouchTargetSize)
{
    public const double MediumBreakpoint = 600;
    public const double ExpandedBreakpoint = 1024;

    public static AdaptiveLayoutMetrics ForWidth(double width)
    {
        var availableWidth = double.IsFinite(width) ? Math.Max(0, width) : 0;

        if (availableWidth < MediumBreakpoint)
        {
            return new(
                AdaptiveLayoutClass.Compact,
                AdaptiveNavigationMode.Bottom,
                PagePadding: 16,
                ContentSpacing: 12,
                MinimumCardWidth: 148,
                MaximumSheetWidth: availableWidth,
                MinimumTouchTargetSize: 44);
        }

        if (availableWidth < ExpandedBreakpoint)
        {
            return new(
                AdaptiveLayoutClass.Medium,
                AdaptiveNavigationMode.CollapsibleRail,
                PagePadding: 24,
                ContentSpacing: 16,
                MinimumCardWidth: 168,
                MaximumSheetWidth: 560,
                MinimumTouchTargetSize: 44);
        }

        return new(
            AdaptiveLayoutClass.Expanded,
            AdaptiveNavigationMode.Rail,
            PagePadding: 32,
            ContentSpacing: 20,
            MinimumCardWidth: 184,
            MaximumSheetWidth: 640,
            MinimumTouchTargetSize: 44);
    }
}
