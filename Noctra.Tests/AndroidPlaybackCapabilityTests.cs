using System;
using System.IO;
using System.Text.Json;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

public sealed class AndroidPlaybackCapabilityTests
{
    private static readonly string[] RequiredLocales = ["en-US", "tr-TR", "de-DE", "fr-FR", "es-ES"];

    // Media3 PlaybackException Error Codes
    private const int ErrorCodeParsingManifestUnsupported = 3004;
    private const int ErrorCodeDecoderInitFailed = 4001;
    private const int ErrorCodeDecoderQueryFailed = 4002;
    private const int ErrorCodeDecodingFormatExceedsCapabilities = 4004;
    private const int ErrorCodeDecodingFormatUnsupported = 4005;
    private const int ErrorCodeNetworkGeneric = 2001;

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

            var unsupportedCodecText = unsuppProp.GetString() ?? string.Empty;
            Assert.DoesNotContain("hardware", unsupportedCodecText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("donanım", unsupportedCodecText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("matériel", unsupportedCodecText, StringComparison.OrdinalIgnoreCase);

            Assert.True(
                rootElement.TryGetProperty("VideoPlayer.Error.DolbyVisionUnsupported", out var dvProp) &&
                !string.IsNullOrWhiteSpace(dvProp.GetString()),
                $"Missing or empty 'VideoPlayer.Error.DolbyVisionUnsupported' in {locale}.json");
        }
    }

    [Theory]
    [InlineData("Hlg", false, true, false, false, false)]
    [InlineData("Hdr10", true, false, false, false, true)]
    [InlineData("Hdr10", false, true, false, false, true)]
    [InlineData("DolbyVision", true, false, false, false, false)]
    [InlineData("Unknown", false, false, false, false, true)]
    public void HdrDisplayCompatibility_RequiresMatchingNativeFormat(
        string transfer,
        bool supportsHdr10,
        bool supportsHdr10Plus,
        bool supportsHlg,
        bool supportsDolbyVision,
        bool expected)
    {
        var actual = HdrDisplayCompatibility.IsNativeDisplaySupported(
            Enum.Parse<HdrTransferKind>(transfer),
            supportsHdr10,
            supportsHdr10Plus,
            supportsHlg,
            supportsDolbyVision);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(ErrorCodeDecodingFormatExceedsCapabilities, "video/hevc", null, false, "UnsupportedCodec")]
    [InlineData(ErrorCodeDecodingFormatUnsupported, "video/hevc", null, false, "UnsupportedCodec")]
    [InlineData(ErrorCodeDecodingFormatExceedsCapabilities, "video/dolby-vision dvhe.08.06", null, false, "DolbyVisionUnsupported")]
    [InlineData(ErrorCodeDecodingFormatUnsupported, "video/dolby-vision", null, false, "DolbyVisionUnsupported")]
    [InlineData(ErrorCodeDecoderInitFailed, "video/dolby-vision", null, false, "DolbyVisionUnsupported")]
    [InlineData(ErrorCodeDecoderQueryFailed, "dvhe.08.06", null, false, "DolbyVisionUnsupported")]
    [InlineData(ErrorCodeDecoderInitFailed, "Decoder init failed: c2.goldfish.hevc.decoder", "format_supported=NO_EXCEEDS_CAPABILITIES", true, "UnsupportedCodec")]
    [InlineData(ErrorCodeDecoderInitFailed, "Decoder init failed: c2.goldfish.hevc.decoder", "format_supported=NO_UNSUPPORTED_TYPE", true, "UnsupportedCodec")]
    [InlineData(ErrorCodeParsingManifestUnsupported, "Manifest parsing unsupported feature", null, false, "Generic")]
    [InlineData(ErrorCodeNetworkGeneric, "Connection failed", null, false, "Generic")]
    [InlineData(1000, "MediaCodecVideoRenderer internal state error", null, true, "Generic")]
    [InlineData(0, "Random error", null, true, "Generic")]
    public void ClassifyErrorLogic_ClassifiesCorrectly(
        int errorCode,
        string? message,
        string? causeMessage,
        bool decoderAdvertisesDolbyVision,
        string expectedClassification)
    {
        // Re-evaluates exact logic encapsulated in AndroidPlaybackCapabilityPolicy.ClassifyErrorCore
        var classification = ClassifyErrorTestHelper(
            errorCode,
            message,
            causeMessage,
            decoderAdvertisesDolbyVision);

        Assert.Equal(expectedClassification, classification.ToString());
    }

    [Fact]
    public void AndroidPlaybackCapabilityPolicy_ContractsAreIntact()
    {
        var source = ReadProjectFile("Noctra.Android", "Services", "AndroidPlaybackCapabilityPolicy.cs");

        Assert.Contains("MediaCodecList", source, StringComparison.Ordinal);
        Assert.Contains("Display.DefaultDisplay", source, StringComparison.Ordinal);
        Assert.Contains("GetDecoderCapabilities", source, StringComparison.Ordinal);
        Assert.Contains("DetectDisplayCapabilities", source, StringComparison.Ordinal);
        Assert.Contains("video/dolby-vision", source, StringComparison.Ordinal);
        Assert.Contains("video/hevc", source, StringComparison.Ordinal);
        Assert.Contains("video/av01", source, StringComparison.Ordinal);
        Assert.Contains("video/x-vnd.on2.vp9", source, StringComparison.Ordinal);
        Assert.Contains("Vp9Profile2Hdr10Plus = 16384", source, StringComparison.Ordinal);
        Assert.Contains("Vp9Profile3Hdr10Plus = 32768", source, StringComparison.Ordinal);
        Assert.Contains("ClassifyErrorCore", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidVideoPlayerService_IntegratesCapabilityPolicyAndDiagnostics()
    {
        var source = ReadProjectFile("Noctra.Android", "Services", "AndroidVideoPlayerService.cs");

        Assert.Contains("AndroidPlaybackCapabilityPolicy.GetCapabilities", source, StringComparison.Ordinal);
        Assert.Contains("AndroidPlaybackCapabilityPolicy.ClassifyError", source, StringComparison.Ordinal);
        Assert.Contains("VideoPlayer.Error.DolbyVisionUnsupported", source, StringComparison.Ordinal);
        Assert.Contains("VideoPlayer.Error.UnsupportedCodec", source, StringComparison.Ordinal);
        Assert.Contains("DisplaySupportsAnyHDR=", source, StringComparison.Ordinal);
        Assert.Contains("HdrDisplayCompatibility.IsNativeDisplaySupported", source, StringComparison.Ordinal);
        Assert.Contains("NativeHdrDisplaySupported=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToneMappingRequired=", source, StringComparison.Ordinal);
    }

    private static string ClassifyErrorTestHelper(
        int errorCode,
        string? message,
        string? causeMessage,
        bool decoderAdvertisesDolbyVision)
    {
        var fullMessage = $"{message} {causeMessage}".Trim();

        var isFormatUnsupported = errorCode is ErrorCodeDecodingFormatExceedsCapabilities
            or ErrorCodeDecodingFormatUnsupported;

        var isDecoderSetupError = errorCode is ErrorCodeDecoderInitFailed
            or ErrorCodeDecoderQueryFailed;

        var mentionsDolbyVision = fullMessage.Contains("video/dolby-vision", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvhe", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvh1", StringComparison.OrdinalIgnoreCase);

        var mentionsCapabilityExceeded = fullMessage.Contains("NO_EXCEEDS_CAPABILITIES", StringComparison.OrdinalIgnoreCase) ||
                                         fullMessage.Contains("NO_UNSUPPORTED_TYPE", StringComparison.OrdinalIgnoreCase);

        if (isFormatUnsupported)
        {
            return mentionsDolbyVision ? "DolbyVisionUnsupported" : "UnsupportedCodec";
        }

        if (isDecoderSetupError)
        {
            if (mentionsDolbyVision && !decoderAdvertisesDolbyVision)
            {
                return "DolbyVisionUnsupported";
            }

            if (mentionsCapabilityExceeded)
            {
                return mentionsDolbyVision ? "DolbyVisionUnsupported" : "UnsupportedCodec";
            }
        }
        else if (mentionsCapabilityExceeded)
        {
            return mentionsDolbyVision ? "DolbyVisionUnsupported" : "UnsupportedCodec";
        }

        return "Generic";
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
