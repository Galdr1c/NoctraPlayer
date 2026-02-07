using global::FFmpeg.AutoGen;
using global::System;
using global::System.Collections.Generic;
using global::System.Threading.Tasks;
using global::System.Runtime.InteropServices;
using global::Windows.Media;
using global::Windows.Media.Audio;
using global::Windows.Media.Render;
using global::Windows.Foundation;

namespace IPTVPlayer.WinUI.Services;

[global::System.Runtime.InteropServices.ComImport]
[global::System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[global::System.Runtime.InteropServices.InterfaceType(global::System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMemoryBufferByteAccess
{
    void GetBuffer([global::System.Runtime.InteropServices.Out] out global::System.IntPtr buffer, [global::System.Runtime.InteropServices.Out] out uint capacity);
}

public class AudioRenderer : global::System.IDisposable
{
    private global::Windows.Media.Audio.AudioGraph? _audioGraph;
    private global::Windows.Media.Audio.AudioFrameInputNode? _frameInputNode;
    private global::Windows.Media.Audio.AudioDeviceOutputNode? _deviceOutputNode;
    private readonly global::System.Collections.Generic.Queue<global::Windows.Media.AudioFrame> _audioQueue = new global::System.Collections.Generic.Queue<global::Windows.Media.AudioFrame>();
    private readonly object _lock = new object();
    private bool _isInit;
    
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; }

    public async global::System.Threading.Tasks.Task InitializeAsync(int sampleRate, int channels)
    {
        if (_isInit) return;
        var settings = new global::Windows.Media.Audio.AudioGraphSettings(global::Windows.Media.Render.AudioRenderCategory.Media);
        var res = await global::Windows.Media.Audio.AudioGraph.CreateAsync(settings);
        if (res.Status != global::Windows.Media.Audio.AudioGraphCreationStatus.Success) return;
        _audioGraph = res.Graph;
        var outRes = await _audioGraph.CreateDeviceOutputNodeAsync();
        if (outRes.Status != global::Windows.Media.Audio.AudioDeviceNodeCreationStatus.Success) return;
        _deviceOutputNode = outRes.DeviceOutputNode;
        var props = new global::Windows.Media.MediaProperties.AudioEncodingProperties 
        { 
            SampleRate = (uint)sampleRate, 
            ChannelCount = (uint)channels, 
            BitsPerSample = 16, 
            Subtype = global::Windows.Media.MediaProperties.MediaEncodingSubtypes.Pcm 
        };
        _frameInputNode = _audioGraph.CreateFrameInputNode(props);
        _frameInputNode.AddOutgoingConnection(_deviceOutputNode);
        _frameInputNode.QuantumStarted += (s, a) => {
            if (IsMuted) return;
            lock (_lock) { if (_audioQueue.Count > 0) { s.AddFrame(_audioQueue.Dequeue()); } }
        };
        _audioGraph.Start();
        _isInit = true;
    }

    public unsafe void QueueAudioFrame(global::System.IntPtr framePtr, int sampleRate, int channels)
    {
        if (!_isInit) return;
        global::FFmpeg.AutoGen.AVFrame* frame = (global::FFmpeg.AutoGen.AVFrame*)framePtr;
        int num = frame->nb_samples;
        int size = num * channels * 2;
        var af = new global::Windows.Media.AudioFrame((uint)size);
        using (var b = af.LockBuffer(global::Windows.Media.AudioBufferAccessMode.Write))
        using (var r = b.CreateReference()) {
            global::System.IntPtr dPtr;
            uint cap;
            ((IMemoryBufferByteAccess)r).GetBuffer(out dPtr, out cap);
            short* outp = (short*)dPtr;
            if (frame->format == (int)global::FFmpeg.AutoGen.AVSampleFormat.AV_SAMPLE_FMT_FLTP) {
                // Read data array at offset 0
                byte** planarData = (byte**)frame; 
                for (int i = 0; i < num; i++) {
                    for (int ch = 0; ch < channels; ch++) {
                        float* dataCh = (float*)planarData[ch];
                        float s = dataCh[i] * (Volume / 100.0f);
                        outp[i * channels + ch] = (short)(global::System.Math.Clamp(s, -1f, 1f) * 32767f);
                    }
                }
            }
        }
        lock (_lock) { _audioQueue.Enqueue(af); while (_audioQueue.Count > 20) if (_audioQueue.TryDequeue(out var old)) old.Dispose(); }
    }

    public void Clear() { lock (_lock) { while (_audioQueue.Count > 0) _audioQueue.Dequeue().Dispose(); } }
    public void Dispose() { Clear(); _frameInputNode?.Dispose(); _deviceOutputNode?.Dispose(); _audioGraph?.Dispose(); }
}
