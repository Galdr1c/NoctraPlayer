using System;
using Android.OS;

namespace Noctra.Android.Services;

internal enum AndroidVideoSurfaceRenderer
{
    SurfaceView,
    TextureView,
}

internal readonly record struct AndroidVideoSurfaceRendererSelection(
    AndroidVideoSurfaceRenderer Renderer,
    string Fallback,
    string? Rule);

internal readonly record struct AndroidVideoSurfaceCompatibilityRule(
    string Manufacturer,
    string Model,
    int? MinimumApi,
    int? MaximumApi,
    string Reason)
{
    internal bool Matches(string manufacturer, string model, int apiLevel)
        => string.Equals(Manufacturer, manufacturer, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(Model, model, StringComparison.OrdinalIgnoreCase) &&
           (!MinimumApi.HasValue || apiLevel >= MinimumApi.Value) &&
           (!MaximumApi.HasValue || apiLevel <= MaximumApi.Value);
}

/// <summary>
/// Central Android video renderer policy. SurfaceView is the production default
/// for every playback kind and colour format. A TextureView rule belongs here
/// only after a reproducible SurfaceView compatibility failure is documented.
/// </summary>
internal static class AndroidVideoSurfaceRendererPolicy
{
    internal const AndroidVideoSurfaceRenderer DefaultRenderer =
        AndroidVideoSurfaceRenderer.SurfaceView;

    // Deliberately empty. Do not add manufacturer/model guesses here. Each rule
    // must cite a reproducible issue and use the narrowest affected API range.
    private static readonly AndroidVideoSurfaceCompatibilityRule[]
        KnownSurfaceViewCompatibilityRules = [];

    internal static AndroidVideoSurfaceRendererSelection Select()
    {
        var manufacturer = Build.Manufacturer ?? string.Empty;
        var model = Build.Model ?? string.Empty;
        var apiLevel = (int)Build.VERSION.SdkInt;

        foreach (var rule in KnownSurfaceViewCompatibilityRules)
        {
            if (rule.Matches(manufacturer, model, apiLevel))
            {
                return new AndroidVideoSurfaceRendererSelection(
                    AndroidVideoSurfaceRenderer.TextureView,
                    "KnownSurfaceViewCompatibilityIssue",
                    rule.Reason);
            }
        }

        return new AndroidVideoSurfaceRendererSelection(
            DefaultRenderer,
            "None",
            null);
    }
}
