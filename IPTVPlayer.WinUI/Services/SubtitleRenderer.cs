using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;

namespace IPTVPlayer.WinUI.Services;

public unsafe class SubtitleRenderer : IDisposable
{
    private AVCodecContext* _subtitleCodecContext;
    public event EventHandler<string>? SubtitleTextChanged;

    public void InitializeSubtitleCodec(AVCodecParameters* codecParams)
    {
        var codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
        if (codec == null) return;
        _subtitleCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(_subtitleCodecContext, codecParams);
        if (ffmpeg.avcodec_open2(_subtitleCodecContext, codec, null) < 0) {
            var ctx = _subtitleCodecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _subtitleCodecContext = null;
        }
    }

    public void DecodeSubtitlePacket(AVPacket* packet)
    {
        if (_subtitleCodecContext == null) return;
        AVSubtitle sub;
        int got = 0;
        if (ffmpeg.avcodec_decode_subtitle2(_subtitleCodecContext, &sub, &got, packet) >= 0 && got != 0) {
            string text = ExtractSubtitleText(&sub);
            SubtitleTextChanged?.Invoke(this, text);
            ffmpeg.avsubtitle_free(&sub);
        }
    }

    private string ExtractSubtitleText(AVSubtitle* sub)
    {
        var parts = new List<string>();
        for (uint i = 0; i < sub->num_rects; i++) {
            var rect = sub->rects[i];
            if (rect->type == AVSubtitleType.SUBTITLE_ASS) {
                var ass = Marshal.PtrToStringUTF8((IntPtr)rect->ass);
                if (!string.IsNullOrEmpty(ass)) parts.Add(ass);
            } else if (rect->type == AVSubtitleType.SUBTITLE_TEXT) {
                var text = Marshal.PtrToStringUTF8((IntPtr)rect->text);
                if (!string.IsNullOrEmpty(text)) parts.Add(text);
            }
        }
        return string.Join("\n", parts);
    }
    
    public void Dispose() { if (_subtitleCodecContext != null) { var ctx = _subtitleCodecContext; ffmpeg.avcodec_free_context(&ctx); _subtitleCodecContext = null; } }
}
