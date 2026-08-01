using System;
using System.IO;
using System.Linq;

namespace Noctra.Tests;

public sealed class MobileStretchOverscrollSourceTests
{
    [Fact]
    public void StretchController_UsesViewportContentTransformAndTouchGuards()
    {
        var source = ReadProjectFile(
            "Noctra.Mobile", "Behaviors", "MobileStretchOverscrollController.cs");

        Assert.Contains("ScrollContentPresenter", source, StringComparison.Ordinal);
        Assert.Contains("TransformGroup", source, StringComparison.Ordinal);
        Assert.Contains("ScaleTransform", source, StringComparison.Ordinal);
        Assert.Contains("TranslateTransform", source, StringComparison.Ordinal);
        Assert.Contains("PointerType.Touch or PointerType.Pen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerWheelChangedEvent", source, StringComparison.Ordinal);
        Assert.Contains("IsExcludedGestureSource", source, StringComparison.Ordinal);
        Assert.Contains("PointerCaptureLostEvent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StretchController_ReplacesGlobalEdgeOverlayInMainView()
    {
        var mainView = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");
        Assert.Contains("MobileStretchOverscrollController", mainView, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileScrollEdgeFeedbackController", mainView, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(
                FindRepositoryRoot(),
                "Noctra.Mobile",
                "Behaviors",
                "MobileScrollEdgeFeedbackController.cs")));
    }

    [Fact]
    public void OverscrollPhysics_UsesBoundedNonLinearResistanceAndSpring()
    {
        var source = ReadProjectFile(
            "Noctra.Mobile", "Behaviors", "MobileOverscrollPhysics.cs");

        Assert.Contains("Math.Exp", source, StringComparison.Ordinal);
        Assert.Contains("GetMaximumTranslation", source, StringComparison.Ordinal);
        Assert.Contains("GetSpringRemaining", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StretchController_UsesSpringSynchronizedNonInteractiveEdgeGlow()
    {
        var source = ReadProjectFile(
            "Noctra.Mobile", "Behaviors", "MobileStretchOverscrollController.cs");
        var zIndexes = ReadProjectFile(
            "Noctra.Mobile", "Controls", "MobileZIndex.cs");

        Assert.Contains("LinearGradientBrush", source, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("UpdateGlow", source, StringComparison.Ordinal);
        Assert.Contains("GetGlowOpacity", source, StringComparison.Ordinal);
        Assert.Contains("GetGlowDepth", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.Contains(
            "ShellEdgeFeedback = 46_000",
            zIndexes,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StretchController_AnimatesGlowDepthWithRenderTransform()
    {
        var source = ReadProjectFile(
            "Noctra.Mobile", "Behaviors", "MobileStretchOverscrollController.cs");

        Assert.Contains(
            "RenderTransform = new ScaleTransform(1, 0)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("glowScale.ScaleY", source, StringComparison.Ordinal);
        Assert.DoesNotContain("activeGlow.Height = depth", source, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
