using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Noctra.Tests;

public sealed class AndroidPlaybackCapabilityTests
{
    private static readonly string[] RequiredLocales = ["en-US", "tr-TR", "de-DE", "fr-FR", "es-ES"];

    [Fact]
    public void LocalizationFiles_ContainRequiredPlaybackErrorKeys()
    {
        var root = FindSolutionRoot();
        var translationsDir = Path.Combine(root, "Noctra.Core", "Localization", "Translations");

        foreach (var locale in RequiredLocales)
        {
            var filePath = Path.Combine(translationsDir, $"{locale}.json");
            Assert.True(File.Exists(filePath), $"Translation file missing for {locale}: {filePath}");

            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var rootElement = doc.RootElement;

            Assert.True(
                rootElement.TryGetProperty("VideoPlayer.Error.UnsupportedCodec", out var unsuppProp) &&
                !string.IsNullOrWhiteSpace(unsuppProp.GetString()),
                $"Missing or empty 'VideoPlayer.Error.UnsupportedCodec' in {locale}.json");

            Assert.True(
                rootElement.TryGetProperty("VideoPlayer.Error.DolbyVisionUnsupported", out var dvProp) &&
                !string.IsNullOrWhiteSpace(dvProp.GetString()),
                $"Missing or empty 'VideoPlayer.Error.DolbyVisionUnsupported' in {locale}.json");
        }
    }

    [Fact]
    public void AndroidPlaybackCapabilityPolicy_ContractsAreIntact()
    {
        var source = ReadProjectFile("Noctra.Android", "Services", "AndroidPlaybackCapabilityPolicy.cs");

        Assert.Contains("MediaCodecList", source, StringComparison.Ordinal);
        Assert.Contains("Display.DefaultDisplay", source, StringComparison.Ordinal);
        Assert.Contains("HdrCapabilities", source, StringComparison.Ordinal);
        Assert.Contains("video/dolby-vision", source, StringComparison.Ordinal);
        Assert.Contains("video/hevc", source, StringComparison.Ordinal);
        Assert.Contains("video/av01", source, StringComparison.Ordinal);
        Assert.Contains("video/x-vnd.on2.vp9", source, StringComparison.Ordinal);
        Assert.Contains("ClassifyError", source, StringComparison.Ordinal);
        Assert.Contains("PlaybackErrorClassification", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidVideoPlayerService_IntegratesCapabilityPolicyAndDiagnostics()
    {
        var source = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("AndroidPlaybackCapabilityPolicy.GetCapabilities", source, StringComparison.Ordinal);
        Assert.Contains("AndroidPlaybackCapabilityPolicy.ClassifyError", source, StringComparison.Ordinal);
        Assert.Contains("VideoPlayer.Error.DolbyVisionUnsupported", source, StringComparison.Ordinal);
        Assert.Contains("VideoPlayer.Error.UnsupportedCodec", source, StringComparison.Ordinal);
        Assert.Contains("NativeHDR=", source, StringComparison.Ordinal);
        Assert.Contains("ToneMapping=", source, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var root = FindSolutionRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindSolutionRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "NoctraPlayer.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate NoctraPlayer.sln.");
    }
}
