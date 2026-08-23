using System.Globalization;
using Noctra.Avalonia.Converters;
using Noctra.Avalonia.Localization;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class DownloadTelemetryBindingTests
{
    [Fact]
    public void DesktopTelemetryConvertersFormatChangingScalarValues()
    {
        LocalizationSource.Instance.Initialize(new LocalizationService());
        var speedConverter = new DownloadSpeedTextConverter();
        var firstSpeed = speedConverter.Convert(
            1024d,
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture)?.ToString();
        var secondSpeed = speedConverter.Convert(
            2048d,
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture)?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(firstSpeed));
        Assert.NotEqual("-", firstSpeed);
        Assert.NotEqual(firstSpeed, secondSpeed);

        var etaConverter = new DownloadEtaTextConverter();
        var firstEta = etaConverter.Convert(
            65,
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture)?.ToString();
        var secondEta = etaConverter.Convert(
            35,
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture)?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(firstEta));
        Assert.NotEqual(firstEta, secondEta);
    }

    [Fact]
    public void DesktopAndMobileBindTelemetryConvertersToChangingProperties()
    {
        var desktop = ReadProjectFile("Noctra.Avalonia", "Views", "DownloadsView.axaml");
        var mobile = ReadProjectFile("Noctra.Mobile", "Views", "MobileDownloadsView.axaml");

        AssertTelemetryBindings(desktop);
        AssertTelemetryBindings(mobile);
    }

    private static void AssertTelemetryBindings(string view)
    {
        Assert.Contains(
            "Text=\"{Binding SpeedBytesPerSecond, Converter={StaticResource DownloadSpeedTextConverter}}\"",
            view,
            StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"{Binding EstimatedSecondsRemaining, Converter={StaticResource DownloadEtaTextConverter}}\"",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"{Binding ., Converter={StaticResource DownloadSpeedTextConverter}}\"",
            view,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"{Binding ., Converter={StaticResource DownloadEtaTextConverter}}\"",
            view,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());
        return File.ReadAllText(path);
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
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
