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
    private readonly byte[] _resampledAudioBuffer;
    private readonly double _resampleRatio;

    private readonly IntPtr _resamplerState;
    private readonly byte[] _sbcPostBuffer;
    private readonly byte[] _sbcPreBuffer;
    
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
        var device = new MMDeviceEnumerator()
            .GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _captureDevice = new WasapiLoopbackCapture(device);
        _captureDevice.DataAvailable += CaptureDeviceOnDataAvailable;

        // Buffers
        int bufferSize = _captureDevice.WaveFormat.ConvertLatencyToByteSize(32);
        _audioData = new CircularBuffer<byte>(bufferSize);
        SbcAudioData = new CircularBuffer<byte>(bufferSize);
        _resampledAudioBuffer = new byte[bufferSize];
        _reformattedAudioBuffer = new byte[bufferSize];

        // Resampler
        _resamplerState = src_new(Quality.SRC_SINC_BEST_QUALITY, CHANNEL_COUNT, out int error);
        _resampleRatio = STREAM_SAMPLE_RATE / (double)_captureDevice.WaveFormat.SampleRate;
        if (IntPtr.Zero == _resamplerState)
        {
            throw new Exception(src_strerror(error));
        }
    }

    public CircularBuffer<byte> SbcAudioData { get; }

    public int FrameSize => (int)_encoder.FrameSize;

    public int CurrentFrameCount => SbcAudioData.CurrentLength / FrameSize;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        
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

    private unsafe void CaptureDeviceOnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            int sampleCount = e.BytesRecorded / 4;

            int reformatedSampleCount = sampleCount;

            fixed (byte* srcPtr = e.Buffer)
            fixed (byte* resamplePtr = _resampledAudioBuffer)
            fixed (byte* reformatPtr = _reformattedAudioBuffer)
            {
                if (STREAM_SAMPLE_RATE != _captureDevice.WaveFormat.SampleRate)
                {
                    int frames = sampleCount / CHANNEL_COUNT;
                    SRC_DATA convert = new()
                    {
                        data_in = (float*)srcPtr,
                        data_out = (float*)resamplePtr,
                        input_frames = frames,
                        output_frames = frames,
                        src_ratio = _resampleRatio
                    };

                    int res = src_process(_resamplerState, ref convert);

                    if (res != 0)
                    {
                        throw new Exception(src_strerror(res));
                    }

                    if (convert.input_frames != convert.input_frames_used)
                    {
                        Console.WriteLine("Not all frames used (?)");
                    }

                    reformatedSampleCount = convert.output_frames_gen * CHANNEL_COUNT;
                    src_float_to_short_array((float*)resamplePtr, (short*)reformatPtr, reformatedSampleCount);
                }
                else
                {
                    src_float_to_short_array((float*)srcPtr, (short*)reformatPtr, sampleCount);
                }
            }

            _audioData.CopyFrom(_reformattedAudioBuffer, reformatedSampleCount * 2);

            while (_audioData.CurrentLength >= (int)_encoder.CodeSize)
            {
                _audioData.CopyTo(_sbcPreBuffer, (int)_encoder.CodeSize);

                _encoder.Encode(_sbcPreBuffer, _sbcPostBuffer, _encoder.CodeSize, out ulong length);

                if (length == 0)
                {
                    Console.WriteLine("Not encoded");
                }
                else
                {
                    SbcAudioData.CopyFrom(_sbcPostBuffer, (int)length);
                }
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine("Exception: {0}", exception);
        }
    }
}