using System;
using System.Linq;
using Android.Content;
using Android.Hardware.Display;
using Android.Media;
using Android.Util;
using Android.Views;
using AndroidX.Media3.Common;

namespace Noctra.Android.Services;

internal readonly record struct AndroidPlaybackCapabilities(
    bool DecoderSupportsHevcMain10,
    bool DecoderSupportsAv1Main10,
    bool DecoderSupportsDolbyVision,
    bool DecoderSupportsVp9Profile2,
    bool DisplaySupportsHdr10,
    bool DisplaySupportsHdr10Plus,
    bool DisplaySupportsHlg,
    bool DisplaySupportsDolbyVision)
{
    internal bool IsDisplayHdrCapable =>
        DisplaySupportsHdr10 || DisplaySupportsHdr10Plus || DisplaySupportsHlg || DisplaySupportsDolbyVision;

    internal bool HasAnyHdrDecoder =>
        DecoderSupportsHevcMain10 || DecoderSupportsAv1Main10 || DecoderSupportsDolbyVision || DecoderSupportsVp9Profile2;

    public override string ToString() =>
        $"Decoders=[HEVC-10bit:{DecoderSupportsHevcMain10}, AV1-10bit:{DecoderSupportsAv1Main10}, DV:{DecoderSupportsDolbyVision}, VP9-P2:{DecoderSupportsVp9Profile2}] " +
        $"Display=[HDR10:{DisplaySupportsHdr10}, HDR10+:{DisplaySupportsHdr10Plus}, HLG:{DisplaySupportsHlg}, DV:{DisplaySupportsDolbyVision}, IsHdrCapable:{IsDisplayHdrCapable}]";
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
    private static AndroidPlaybackCapabilities? _cachedCapabilities;
    private static readonly object Lock = new();

    internal static AndroidPlaybackCapabilities GetCapabilities(Context context)
    {
        lock (Lock)
        {
            if (_cachedCapabilities.HasValue)
            {
                return _cachedCapabilities.Value;
            }

            var capabilities = DetectCapabilities(context);
            _cachedCapabilities = capabilities;
            Log.Info(Tag, $"Detected playback capabilities: {capabilities}");
            return capabilities;
        }
    }

    private static AndroidPlaybackCapabilities DetectCapabilities(Context context)
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

        var hevcMain10 = false;
        var av1Main10 = false;
        var dolbyVision = false;
        var vp9Profile2 = false;

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
                        if (CheckCodecProfile(info, "video/hevc", (int)MediaCodecProfileType.Hevcprofilemain10, 4096 /* HDR10 */, 8192 /* HDR10+ */))
                        {
                            hevcMain10 = true;
                        }
                    }

                    if (!av1Main10 && supportedTypes.Any(t => string.Equals(t, "video/av01", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CheckCodecProfile(info, "video/av01", (int)MediaCodecProfileType.Av1profilemain10, 4096 /* HDR10 */, 8192 /* HDR10+ */))
                        {
                            av1Main10 = true;
                        }
                    }

                    if (!vp9Profile2 && supportedTypes.Any(t => string.Equals(t, "video/x-vnd.on2.vp9", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (CheckCodecProfile(info, "video/x-vnd.on2.vp9", (int)MediaCodecProfileType.Vp9profile2, (int)MediaCodecProfileType.Vp9profile3, 4096 /* HDR */, 8192 /* HDR */))
                        {
                            vp9Profile2 = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Failed to query MediaCodec decoder capabilities: {ex.Message}");
        }

        return new AndroidPlaybackCapabilities(
            DecoderSupportsHevcMain10: hevcMain10,
            DecoderSupportsAv1Main10: av1Main10,
            DecoderSupportsDolbyVision: dolbyVision,
            DecoderSupportsVp9Profile2: vp9Profile2,
            DisplaySupportsHdr10: displayHdr10,
            DisplaySupportsHdr10Plus: displayHdr10Plus,
            DisplaySupportsHlg: displayHlg,
            DisplaySupportsDolbyVision: displayDolbyVision);
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

        var errorCode = error.ErrorCode;
        var fullMessage = $"{error.Message} {error.Cause?.Message}".Trim();

        var isDecoderError = errorCode is PlaybackException.ErrorCodeDecoderInitFailed
            or PlaybackException.ErrorCodeDecoderQueryFailed
            or PlaybackException.ErrorCodeParsingManifestUnsupported;

        var mentionsDolbyVision = fullMessage.Contains("video/dolby-vision", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvhe", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dvh1", StringComparison.OrdinalIgnoreCase) ||
                                  fullMessage.Contains("dolby", StringComparison.OrdinalIgnoreCase);

        var mentionsCapabilityExceeded = fullMessage.Contains("NO_EXCEEDS_CAPABILITIES", StringComparison.OrdinalIgnoreCase) ||
                                         fullMessage.Contains("NO_UNSUPPORTED_TYPE", StringComparison.OrdinalIgnoreCase) ||
                                         fullMessage.Contains("Decoder init failed", StringComparison.OrdinalIgnoreCase) ||
                                         fullMessage.Contains("MediaCodecVideoRenderer", StringComparison.OrdinalIgnoreCase);

        if (mentionsDolbyVision && !capabilities.DecoderSupportsDolbyVision)
        {
            return PlaybackErrorClassification.DolbyVisionUnsupported;
        }

        if (isDecoderError || mentionsCapabilityExceeded)
        {
            if (mentionsDolbyVision)
            {
                return PlaybackErrorClassification.DolbyVisionUnsupported;
            }

            return PlaybackErrorClassification.UnsupportedCodec;
        }

        return PlaybackErrorClassification.Generic;
    }
}
