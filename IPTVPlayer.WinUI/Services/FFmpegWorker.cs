using global::FFmpeg.AutoGen;
using global::System;
using global::System.Runtime.InteropServices;

namespace IPTVPlayer.WinUI.Services;

public unsafe class FFmpegWorker : IDisposable
{
    private AVFormatContext* _formatContext;
    private AVCodecContext* _videoCodecContext;
    private AVFrame* _videoFrame;
    private AVFrame* _rgbFrame;
    private AVPacket* _packet;
    private SwsContext* _swsContext;
    private byte* _videoBuffer;
    private int _videoStreamIndex = -1;

    public int Width => _videoCodecContext != null ? _videoCodecContext->width : 0;
    public int Height => _videoCodecContext != null ? _videoCodecContext->height : 0;
    public double Duration { get; private set; }

    public void Open(string url)
    {
        fixed (AVFormatContext** pFormatContext = &_formatContext)
        {
            if (global::FFmpeg.AutoGen.ffmpeg.avformat_open_input(pFormatContext, url, null, null) < 0)
                throw new Exception("Could not open input.");
        }

        if (global::FFmpeg.AutoGen.ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            throw new Exception("Could not find stream information.");

        for (int i = 0; i < (int)_formatContext->nb_streams; i++)
        {
            if (_formatContext->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                _videoStreamIndex = i;
                break;
            }
        }

        if (_videoStreamIndex == -1)
            throw new Exception("Could not find a video stream.");

        AVCodecParameters* codecParameters = _formatContext->streams[_videoStreamIndex]->codecpar;
        AVCodec* codec = global::FFmpeg.AutoGen.ffmpeg.avcodec_find_decoder(codecParameters->codec_id);
        if (codec == null)
            throw new Exception("Unsupported codec.");

        _videoCodecContext = global::FFmpeg.AutoGen.ffmpeg.avcodec_alloc_context3(codec);
        if (global::FFmpeg.AutoGen.ffmpeg.avcodec_parameters_to_context(_videoCodecContext, codecParameters) < 0)
            throw new Exception("Could not copy codec parameters to context.");

        if (global::FFmpeg.AutoGen.ffmpeg.avcodec_open2(_videoCodecContext, codec, null) < 0)
            throw new Exception("Could not open codec.");

        _videoFrame = global::FFmpeg.AutoGen.ffmpeg.av_frame_alloc();
        _rgbFrame = global::FFmpeg.AutoGen.ffmpeg.av_frame_alloc();
        _packet = global::FFmpeg.AutoGen.ffmpeg.av_packet_alloc();

        int numBytes = global::FFmpeg.AutoGen.ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, _videoCodecContext->width, _videoCodecContext->height, 1);
        _videoBuffer = (byte*)global::FFmpeg.AutoGen.ffmpeg.av_malloc((ulong)numBytes);

        // FFmpeg.AutoGen often uses byte_ptrArray4 for image functions
        byte_ptrArray4 data = new byte_ptrArray4();
        int_array4 linesize = new int_array4();
        global::FFmpeg.AutoGen.ffmpeg.av_image_fill_arrays(ref data, ref linesize, _videoBuffer, AVPixelFormat.AV_PIX_FMT_BGRA, _videoCodecContext->width, _videoCodecContext->height, 1);
        
        // Use direct pointer arithmetic to set rgbFrame->data and linesize
        // Since AVFrame.data is byte_ptrArray8, we can use a pointer to that type
        byte_ptrArray8* dstData = (byte_ptrArray8*)_rgbFrame;
        int_array8* dstLinesize = (int_array8*)((byte*)_rgbFrame + 64);
        
        // byte_ptrArray4 cannot be converted to byte_ptrArray8 directly, so we copy manually via pointers
        byte** srcDataPtr = (byte**)&data;
        int* srcLinesizePtr = (int*)&linesize;
        byte** targetDataPtr = (byte**)dstData;
        int* targetLinesizePtr = (int*)dstLinesize;

        for (int i = 0; i < 4; i++)
        {
            targetDataPtr[i] = srcDataPtr[i];
            targetLinesizePtr[i] = srcLinesizePtr[i];
        }

        _swsContext = global::FFmpeg.AutoGen.ffmpeg.sws_getContext(
            _videoCodecContext->width, _videoCodecContext->height, _videoCodecContext->pix_fmt,
            _videoCodecContext->width, _videoCodecContext->height, AVPixelFormat.AV_PIX_FMT_BGRA,
            global::FFmpeg.AutoGen.ffmpeg.SWS_BILINEAR, null, null, null);

        Duration = _formatContext->duration / (double)global::FFmpeg.AutoGen.ffmpeg.AV_TIME_BASE;
    }

    public bool ReadFrame(out IntPtr dataPtr, out int stride, out double pts)
    {
        dataPtr = IntPtr.Zero;
        stride = 0;
        pts = 0;

        while (global::FFmpeg.AutoGen.ffmpeg.av_read_frame(_formatContext, _packet) >= 0)
        {
            if (_packet->stream_index == _videoStreamIndex)
            {
                if (global::FFmpeg.AutoGen.ffmpeg.avcodec_send_packet(_videoCodecContext, _packet) >= 0)
                {
                    if (global::FFmpeg.AutoGen.ffmpeg.avcodec_receive_frame(_videoCodecContext, _videoFrame) >= 0)
                    {
                        // Dereference pointers to byte_ptrArray8/int_array8 to pass as values to sws_scale
                        byte_ptrArray8* srcData = (byte_ptrArray8*)_videoFrame;
                        int_array8* srcLinesize = (int_array8*)((byte*)_videoFrame + 64);
                        byte_ptrArray8* dstData = (byte_ptrArray8*)_rgbFrame;
                        int_array8* dstLinesize = (int_array8*)((byte*)_rgbFrame + 64);

                        global::FFmpeg.AutoGen.ffmpeg.sws_scale(_swsContext, *srcData, *srcLinesize, 0, _videoCodecContext->height, *dstData, *dstLinesize);

                        byte** dstDataPtr = (byte**)dstData;
                        int* dstLinesizePtr = (int*)dstLinesize;

                        dataPtr = (IntPtr)dstDataPtr[0];
                        stride = dstLinesizePtr[0];
                        
                        // PTS Calculation
                        AVRational tb = _formatContext->streams[_videoStreamIndex]->time_base;
                        pts = _videoFrame->best_effort_timestamp * global::FFmpeg.AutoGen.ffmpeg.av_q2d(tb);

                        global::FFmpeg.AutoGen.ffmpeg.av_packet_unref(_packet);
                        return true;
                    }
                }
            }
            global::FFmpeg.AutoGen.ffmpeg.av_packet_unref(_packet);
        }

        return false;
    }

    public void Dispose()
    {
        if (_videoBuffer != null) { byte* p = _videoBuffer; global::FFmpeg.AutoGen.ffmpeg.av_free(p); _videoBuffer = null; }
        if (_rgbFrame != null) fixed (AVFrame** p = &_rgbFrame) global::FFmpeg.AutoGen.ffmpeg.av_frame_free(p);
        if (_videoFrame != null) fixed (AVFrame** p = &_videoFrame) global::FFmpeg.AutoGen.ffmpeg.av_frame_free(p);
        if (_packet != null) fixed (AVPacket** p = &_packet) global::FFmpeg.AutoGen.ffmpeg.av_packet_free(p);
        if (_videoCodecContext != null) fixed (AVCodecContext** p = &_videoCodecContext) global::FFmpeg.AutoGen.ffmpeg.avcodec_free_context(p);
        if (_formatContext != null) fixed (AVFormatContext** p = &_formatContext) global::FFmpeg.AutoGen.ffmpeg.avformat_close_input(p);
        if (_swsContext != null) global::FFmpeg.AutoGen.ffmpeg.sws_freeContext(_swsContext);
    }
}
