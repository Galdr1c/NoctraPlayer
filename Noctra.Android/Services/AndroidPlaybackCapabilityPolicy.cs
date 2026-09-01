using System;
using System.Linq;
using Android.Content;
using Android.Hardware.Display;
using Android.Media;
using Android.Util;
using Android.Views;
using AndroidX.Media3.Common;

namespace Noctra.Android.Services;

internal readonly record struct AndroidDecoderCapabilities(
    bool SupportsHevcMain10,
    bool SupportsAv1Main10,
    bool AdvertisesDolbyVision,
    bool SupportsVp9HighBitDepth)
{
    public override string ToString() =>
        $"HEVC-10bit:{SupportsHevcMain10}, AV1-10bit:{SupportsAv1Main10}, DV:{AdvertisesDolbyVision}, VP9-HBD:{SupportsVp9HighBitDepth}";
}

internal readonly record struct AndroidDisplayCapabilities(
    bool SupportsHdr10,
    bool SupportsHdr10Plus,
    bool SupportsHlg,
    bool SupportsDolbyVision)
{
    internal bool IsHdrCapable =>
        SupportsHdr10 || SupportsHdr10Plus || SupportsHlg || SupportsDolbyVision;

    public override string ToString() =>
        $"HDR10:{SupportsHdr10}, HDR10+:{SupportsHdr10Plus}, HLG:{SupportsHlg}, DV:{SupportsDolbyVision}, IsHdrCapable:{IsHdrCapable}";
}

internal readonly record struct AndroidPlaybackCapabilities(
    AndroidDecoderCapabilities Decoders,
    AndroidDisplayCapabilities Display)
{
    internal bool DecoderSupportsHevcMain10 => Decoders.SupportsHevcMain10;
    internal bool DecoderSupportsAv1Main10 => Decoders.SupportsAv1Main10;
    internal bool DecoderAdvertisesDolbyVision => Decoders.AdvertisesDolbyVision;
    internal bool DecoderSupportsVp9HighBitDepth => Decoders.SupportsVp9HighBitDepth;

    internal bool DisplaySupportsHdr10 => Display.SupportsHdr10;
    internal bool DisplaySupportsHdr10Plus => Display.SupportsHdr10Plus;
    internal bool DisplaySupportsHlg => Display.SupportsHlg;
    internal bool DisplaySupportsDolbyVision => Display.SupportsDolbyVision;
    internal bool IsDisplayHdrCapable => Display.IsHdrCapable;

    public override string ToString() =>
        $"Decoders=[{Decoders}] Display=[{Display}]";
}

internal enum PlaybackErrorClassification
{
    Generic,
    UnsupportedCodec,
    DolbyVisionUnsupported,
}

internal static class AndroidPlaybackCapabilityPolicy
{
    private const string Tag = "NoctraPlaybackCaps";

    // MediaCodec profile constants for 10-bit and HDR profiles
    private const int HevcProfileMain10 = (int)MediaCodecProfileType.Hevcprofilemain10; // 2
    private const int HevcProfileMain10Hdr10 = 4096;
    private const int HevcProfileMain10Hdr10Plus = 8192;

    private const int Av1ProfileMain10 = (int)MediaCodecProfileType.Av1profilemain10; // 2
    private const int Av1ProfileMain10Hdr10 = 4096;
    private const int Av1ProfileMain10Hdr10Plus = 8192;

    private const int Vp9Profile2 = (int)MediaCodecProfileType.Vp9profile2; // 4 (10-bit / 12-bit)
    private const int Vp9Profile3 = (int)MediaCodecProfileType.Vp9profile3; // 8 (10-bit / 12-bit 4:2:2/4:4:4)
    private const int Vp9Profile2Hdr = 4096;
    private const int Vp9Profile3Hdr = 8192;
    private const int Vp9Profile2Hdr10Plus = 16384;
    private const int Vp9Profile3Hdr10Plus = 32768;

    // Codec capabilities are static hardware properties per process lifetime
    private static AndroidDecoderCapabilities? _cachedDecoders;
    private static readonly object DecoderLock = new();

    internal static AndroidPlaybackCapabilities GetCapabilities(Context context)
    {
        var decoders = GetDecoderCapabilities();
        var display = DetectDisplayCapabilities(context);
        var capabilities = new AndroidPlaybackCapabilities(decoders, display);
        Log.Info(Tag, $"Current playback capabilities: {capabilities}");
        return capabilities;
    }

    private static AndroidDecoderCapabilities GetDecoderCapabilities()
    {
        lock (DecoderLock)
        {
            if (_cachedDecoders.HasValue)
            {
                return _cachedDecoders.Value;
            }

            var decoders = DetectDecoderCapabilities();
            _cachedDecoders = decoders;
            return decoders;
        }
    }

    // Non-DRM regular playback hardware decoder capability inspection
    private static AndroidDecoderCapabilities DetectDecoderCapabilities()
    {
        var hevcMain10 = false;
        var av1Main10 = false;
        var dolbyVision = false;
        var vp9HighBitDepth = false;

        try
        {
            var codecList = new MediaCodecList(MediaCodecListKind.RegularCodecs);
            var codecInfos = codecList.GetCodecInfos();

            if (codecInfos != null)
            {
                foreach (var info in codecInfos.Where(c => c != null && !c.IsEncoder))
                {
                    var supportedTypes = info.GetSupportedTypes();
                    if (supportedTypes == null || supportedTypes.Length == 0)
                    {
                        continue;
                    }

                    if (supportedTypes.Any(t => string.Equals(t, "video/dolby-vision", StringComparison.OrdinalIgnoreCase)))
                    {
                        dolbyVision = true;
                    }

                    if (!hevcMain10 && supportedTypes.Any(t => string.Equals(t, "video/hevc", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CheckCodecProfile(info, "video/hevc", HevcProfileMain10, HevcProfileMain10Hdr10, HevcProfileMain10Hdr10Plus))
                        {
                            hevcMain10 = true;
                        }
                    }

                    if (!av1Main10 && supportedTypes.Any(t => string.Equals(t, "video/av01", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CheckCodecProfile(info, "video/av01", Av1ProfileMain10, Av1ProfileMain10Hdr10, Av1ProfileMain10Hdr10Plus))
                        {
                            av1Main10 = true;
                        }
                    }

                    if (!vp9HighBitDepth && supportedTypes.Any(t => string.Equals(t, "video/x-vnd.on2.vp9", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CheckCodecProfile(
                            info,
                            "video/x-vnd.on2.vp9",
                            Vp9Profile2,
                            Vp9Profile3,
                            Vp9Profile2Hdr,
                            Vp9Profile3Hdr,
                            Vp9Profile2Hdr10Plus,
                            Vp9Profile3Hdr10Plus))
                        {
                            vp9HighBitDepth = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Failed to query MediaCodec decoder capabilities: {ex.Message}");
        }

        return new AndroidDecoderCapabilities(
            SupportsHevcMain10: hevcMain10,
            SupportsAv1Main10: av1Main10,
            AdvertisesDolbyVision: dolbyVision,
            SupportsVp9HighBitDepth: vp9HighBitDepth);
    }

    private static AndroidDisplayCapabilities DetectDisplayCapabilities(Context context)
    {
        var displayHdr10 = false;
        var displayHdr10Plus = false;
        var displayHlg = false;
        var displayDolbyVision = false;

        try
        {
            var displayManager = (DisplayManager?)context.GetSystemService(Context.DisplayService);
            var display = displayManager?.GetDisplay(Display.DefaultDisplay);
#pragma warning disable CA1422
            var supportedHdrTypes = display?.GetHdrCapabilities()?.GetSupportedHdrTypes() ?? Array.Empty<HdrType>();
#pragma warning restore CA1422

            foreach (var type in supportedHdrTypes)
            {
                switch (type)
                {
                    case HdrType.Hdr10:
                        displayHdr10 = true;
                        break;
                    case HdrType.Hdr10Plus:
                        displayHdr10Plus = true;
                        break;
                    case HdrType.Hlg:
                        displayHlg = true;
                        break;
                    case HdrType.DolbyVision:
                        displayDolbyVision = true;
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Failed to query Display HDR capabilities: {ex.Message}");
        }

        return new AndroidDisplayCapabilities(
            SupportsHdr10: displayHdr10,
            SupportsHdr10Plus: displayHdr10Plus,
            SupportsHlg: displayHlg,
            SupportsDolbyVision: displayDolbyVision);
    }

    private static bool CheckCodecProfile(MediaCodecInfo info, string mimeType, params int[] targetProfiles)
    {
        try
        {
            var capabilities = info.GetCapabilitiesForType(mimeType);
            var profileLevels = capabilities?.ProfileLevels;
            if (profileLevels == null || profileLevels.Count == 0)
            {
                return false;
            }

            foreach (var pl in profileLevels)
            {
                var profile = (int)pl.Profile;
                if (targetProfiles.Contains(profile))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Some vendor drivers throw on specific MIME capability queries.
        }

        return false;
    }

    internal static PlaybackErrorClassification ClassifyError(
        PlaybackException? error,
        AndroidPlaybackCapabilities capabilities)
    {
        if (error == null)
        {
            return PlaybackErrorClassification.Generic;
        }

        return ClassifyErrorCore(
            error.ErrorCode,
            error.Message,
            error.Cause?.Message,
            capabilities.DecoderAdvertisesDolbyVision);
    }

    internal static PlaybackErrorClassification ClassifyErrorCore(
        int errorCode,
        string? message,
        string? causeMessage,
        bool decoderAdvertisesDolbyVision)
    {
        var fullMessage = $"{message} {causeMessage}".Trim();

        // Official Media3 decoder & format capability error codes
        var isFormatUnsupported = errorCode is PlaybackException.ErrorCodeDecodingFormatExceedsCapabilities // 4004
            or PlaybackException.ErrorCodeDecodingFormatUnsupported; // 4005

        var isDecoderSetupError = errorCode is PlaybackException.ErrorCodeDecoderInitFailed // 4001
            or PlaybackException.ErrorCodeDecoderQueryFailed; // 4002

        var mentionsDolbyVision = fullMessage.Contains("video/dolby-vision", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvhe", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvh1", StringComparison.OrdinalIgnoreCase);

        // Fallback checks for legacy/vendor driver diagnostics
        var mentionsCapabilityExceeded = fullMessage.Contains("NO_EXCEEDS_CAPABILITIES", StringComparison.OrdinalIgnoreCase) ||
                                         fullMessage.Contains("NO_UNSUPPORTED_TYPE", StringComparison.OrdinalIgnoreCase);

        if (isFormatUnsupported)
        {
            return mentionsDolbyVision
                ? PlaybackErrorClassification.DolbyVisionUnsupported
                : PlaybackErrorClassification.UnsupportedCodec;
        }

        if (isDecoderSetupError)
        {
            if (mentionsDolbyVision && !decoderAdvertisesDolbyVision)
            {
                return PlaybackErrorClassification.DolbyVisionUnsupported;
            }

            if (mentionsCapabilityExceeded)
            {
                return mentionsDolbyVision
                    ? PlaybackErrorClassification.DolbyVisionUnsupported
                    : PlaybackErrorClassification.UnsupportedCodec;
            }
        }
        else if (mentionsCapabilityExceeded)
        {
            return mentionsDolbyVision
                ? PlaybackErrorClassification.DolbyVisionUnsupported
                : PlaybackErrorClassification.UnsupportedCodec;
        }

        return PlaybackErrorClassification.Generic;
    }
}
