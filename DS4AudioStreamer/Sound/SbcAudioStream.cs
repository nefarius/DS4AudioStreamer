using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using SharpSBC;

using static SharpSampleRate.SampleRate;

using ChannelMode = SharpSBC.ChannelMode;

namespace DS4AudioStreamer.Sound;

public class SbcAudioStream : IDisposable
{
    private const int STREAM_SAMPLE_RATE = 32000;
    private const int CHANNEL_COUNT = 2;

    private readonly CircularBuffer<byte> _audioData;

    private readonly WasapiCapture _captureDevice;

    private readonly SbcEncoder _encoder;
    private readonly byte[] _reformattedAudioBuffer;
    private readonly double _resampleRatio;

    private readonly IntPtr _resamplerState;
    private readonly byte[] _sbcPostBuffer;
    private readonly byte[] _sbcPreBuffer;
    private byte[] _resampledAudioBuffer;

    // new stuff below
    
    private const int CaptureBufferMilliseconds = 30;
    private const int CaptureBufferMaxCapacity = 5;
    private Channel<WaveInEventArgs> _captureBuffer;
    private Task _resamplingTask;
    private CancellationTokenSource _cts;

    public SbcAudioStream()
    {
        // Encoder
        _encoder = new SbcEncoder(
            STREAM_SAMPLE_RATE,
            SubBandCount.Sb8,
            48,
            ChannelMode.JointStereo,
            AllocationMode.Snr,
            BlockCount.Blk16
        );

        _sbcPreBuffer = new byte[_encoder.CodeSize];
        _sbcPostBuffer = new byte[_encoder.FrameSize];

        // Capture Device
        MMDevice? device = new MMDeviceEnumerator()
            .GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        
        _captureDevice = new BufferedLoopbackCapture(device, CaptureBufferMilliseconds);
        _captureDevice.DataAvailable += OnAudioCaptured;
        //_captureDevice.DataAvailable += CaptureDeviceOnDataAvailable;
        
        Console.WriteLine(_captureDevice.WaveFormat);
        
        long maxBufferSize = _captureDevice.WaveFormat.GetMaxBufferSize(CaptureBufferMilliseconds);
        Console.WriteLine($"Max expected buffer size: {maxBufferSize} bytes for {CaptureBufferMilliseconds} ms");
        
        _captureBuffer = Channel.CreateBounded<WaveInEventArgs>(
            new BoundedChannelOptions(CaptureBufferMaxCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        // Resampler
        _resamplerState = src_new(Quality.SRC_SINC_BEST_QUALITY, CHANNEL_COUNT, out int error);
        _resampleRatio = STREAM_SAMPLE_RATE / (double)_captureDevice.WaveFormat.SampleRate;
        if (IntPtr.Zero == _resamplerState)
        {
            throw new Exception(src_strerror(error));
        }

        // Buffers
        int bufferSize = _captureDevice.WaveFormat.ConvertLatencyToByteSize(CaptureBufferMilliseconds);
        Console.WriteLine($"Buffer size: {bufferSize} bytes for {CaptureBufferMilliseconds} ms");
        // Assume max input frame count from WASAPI
        int frameCount = bufferSize / _captureDevice.WaveFormat.BlockAlign;
        // Worst-case: output may require more space due to upsampling
        int maxOutputFrames = (int)(frameCount * _resampleRatio) + 32; // + some margin
        
        _resampledAudioBuffer = new byte[maxOutputFrames * CHANNEL_COUNT * sizeof(float)];
        _reformattedAudioBuffer = new byte[maxOutputFrames * CHANNEL_COUNT * sizeof(short)];
        _audioData = new CircularBuffer<byte>(maxOutputFrames * CHANNEL_COUNT * sizeof(short));
        
        SbcAudioData = new CircularBuffer<byte>(bufferSize);
        
        _cts = new CancellationTokenSource();
        
        _resamplingTask = Task.Factory.StartNew(
            async () => await ResampleAsync(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        ).Unwrap();
    }

    private async Task ResampleAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            WaveInEventArgs args = await _captureBuffer.Reader.ReadAsync(_cts.Token);

            Debug.WriteLine($"Bytes recorded: {args.BytesRecorded} bytes");
        }
    }   

    public CircularBuffer<byte> SbcAudioData { get; }

    public int FrameSize => (int)_encoder.FrameSize;

    public int CurrentFrameCount => SbcAudioData.CurrentLength / FrameSize;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _cts.Cancel();
        _cts.Dispose();
        _captureDevice.Dispose();
        _encoder.Dispose();
    }

    public void Start()
    {
        _captureDevice.StartRecording();
    }

    public void WaitUntil(int frameCount)
    {
        while (SbcAudioData.CurrentLength < frameCount * FrameSize)
        {
            Thread.Yield();
        }
    }

    public void Stop()
    {
        _captureDevice.StopRecording();
    }

    private unsafe void OnAudioCaptured(object? sender, WaveInEventArgs e)
    {
        
    }
    
    private unsafe void CaptureDeviceOnDataAvailable(object? sender, WaveInEventArgs e)
    {
        /*if (!_captureBuffer.Writer.TryWrite(e))
        {
            Debug.WriteLine("Capture buffer full, dropping data");
        }*/
        
        try
        {
            WasapiCapture? device = sender as WasapiCapture;
            ArgumentNullException.ThrowIfNull(device);
            int sampleFrames = e.GetSampleFrameCount(device);
            Debug.WriteLine($"Sample frames captured: {sampleFrames}");
            // equal if recording sample rate matches the target rate
            int totalSamples = sampleFrames * CHANNEL_COUNT;
            Debug.WriteLine($"Total samples captured: {totalSamples}");

            if (_resampledAudioBuffer.Length < totalSamples * sizeof(float))
            {
                _resampledAudioBuffer = new byte[totalSamples * sizeof(float)];
            }

            fixed (float* srcPtr = MemoryMarshal.Cast<byte, float>(e.Buffer))
            fixed (float* resamplePtr = MemoryMarshal.Cast<byte, float>(_resampledAudioBuffer))
            fixed (short* reformatPtr = MemoryMarshal.Cast<byte, short>(_reformattedAudioBuffer))
            {
                if (STREAM_SAMPLE_RATE != _captureDevice.WaveFormat.SampleRate)
                {
                    SRC_DATA convert = new()
                    {
                        data_in = srcPtr,
                        data_out = resamplePtr,
                        input_frames = sampleFrames,
                        output_frames = sampleFrames,
                        src_ratio = _resampleRatio
                    };

                    int res = src_process(_resamplerState, &convert);

                    if (res != 0)
                    {
                        throw new Exception(src_strerror(res));
                    }

                    if (convert.input_frames != convert.input_frames_used)
                    {
                        Debug.WriteLine("Not all frames used (?)");
                    }

                    totalSamples = convert.output_frames_gen * CHANNEL_COUNT;
                    src_float_to_short_array(resamplePtr, reformatPtr, totalSamples);
                }
                else
                {
                    src_float_to_short_array(srcPtr, reformatPtr, totalSamples);
                }
            }
            
            Debug.WriteLine($"Total samples converted: {totalSamples}");
            // TODO: buffer sizes bugged!
            return;

            _audioData.CopyFrom(_reformattedAudioBuffer, totalSamples);

            while (_audioData.CurrentLength >= (int)_encoder.CodeSize)
            {
                _audioData.CopyTo(_sbcPreBuffer, (int)_encoder.CodeSize);

                _encoder.Encode(_sbcPreBuffer, _sbcPostBuffer, _encoder.CodeSize, out ulong length);

                if (length == 0)
                {
                    Debug.WriteLine("Not encoded");
                }
                else
                {
                    SbcAudioData.CopyFrom(_sbcPostBuffer, (int)length);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Exception: {0}", exception);
        }
    }
}