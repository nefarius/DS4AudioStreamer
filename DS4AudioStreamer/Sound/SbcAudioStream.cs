using System.Diagnostics;
using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using SharpSBC;

using static SharpSampleRate.SampleRate;

using ChannelMode = SharpSBC.ChannelMode;

namespace DS4AudioStreamer.Sound;

public class SbcAudioStream : IDisposable
{
    // target sample rate
    private const int STREAM_SAMPLE_RATE = 32000;

    // target channel count for speaker or headset (stereo)
    private const int CHANNEL_COUNT = 2;

    private const int CaptureBufferMilliseconds = 100;

    private readonly WasapiCapture _captureDevice;
    private readonly byte[] _dstSbcBlock;
    private readonly SbcEncoder _encoder;
    private readonly double _resampleRatio;
    private readonly IntPtr _resamplerState;

    public SbcAudioStream()
    {
        // SBC Encoder setting compatible with DS4 variants
        _encoder = new SbcEncoder(
            STREAM_SAMPLE_RATE,
            SubBandCount.Sb8,
            48,
            ChannelMode.JointStereo,
            AllocationMode.Snr,
            BlockCount.Blk16
        );

        _dstSbcBlock = new byte[_encoder.CodeSize];

        // Capture Device (default output device)
        MMDevice? device = new MMDeviceEnumerator()
            .GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        Console.WriteLine($"Default output device: {device.FriendlyName}");
        
        _captureDevice = new BufferedLoopbackCapture(device, CaptureBufferMilliseconds);
        _captureDevice.DataAvailable += OnAudioCaptured;

        // Resampler
        _resamplerState = src_new(Quality.SRC_SINC_BEST_QUALITY, CHANNEL_COUNT, out int error);
        if (IntPtr.Zero == _resamplerState)
        {
            throw new Exception(src_strerror(error));
        }

        _resampleRatio = STREAM_SAMPLE_RATE / (double)_captureDevice.WaveFormat.SampleRate;

        // Buffers
        int bufferSize = _captureDevice.WaveFormat.ConvertLatencyToByteSize(32);

        Console.WriteLine($"SBC Buffer size: {bufferSize}");

        // TODO: fix size calculation
        SbcAudioData = new CircularBuffer<byte>(bufferSize);
    }

    /// <summary>
    ///     SBC-encoded audio ring buffer.
    /// </summary>
    public CircularBuffer<byte> SbcAudioData { get; }

    /// <summary>
    ///     SBC output block size.
    /// </summary>
    public int FrameSize => (int)_encoder.FrameSize;

    /// <summary>
    ///     Number of encoded frames available.
    /// </summary>
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

    /// <summary>
    ///     Invoked when PCM captured data is available for processing.
    /// </summary>
    private unsafe void OnAudioCaptured(object? sender, WaveInEventArgs e)
    {
        WasapiCapture? device = sender as WasapiCapture;
        ArgumentNullException.ThrowIfNull(device);
        int inFrameCount = e.GetNumFrames(device);

        // smaller when downsampling, larger when upsampling
        int outFrameCount = (int)Math.Ceiling(inFrameCount * _resampleRatio);
        int inSamples = inFrameCount * CHANNEL_COUNT;
        int outSamples = outFrameCount * CHANNEL_COUNT;

        short* finalShortFormat = stackalloc short[outSamples];
        int finalBufferBytes = outSamples * sizeof(short);

        fixed (float* pBuffer = MemoryMarshal.Cast<byte, float>(e.Buffer))
        {
            if (STREAM_SAMPLE_RATE != _captureDevice.WaveFormat.SampleRate)
            {
                // resample and convert
                float* dataIn = stackalloc float[inSamples];
                float* dataOut = stackalloc float[outSamples];

                Buffer.MemoryCopy(pBuffer, dataIn, inSamples * sizeof(float), inSamples * sizeof(float));

                SRC_DATA convert = new()
                {
                    data_in = dataIn,
                    data_out = dataOut,
                    input_frames = inFrameCount,
                    output_frames = outFrameCount,
                    src_ratio = _resampleRatio
                };

                int res = src_process(_resamplerState, &convert);

                if (res != 0)
                {
                    throw new Exception(src_strerror(res));
                }

                if (convert.input_frames != convert.input_frames_used)
                {
                    // TODO: error handling
                    Debug.WriteLine("Not all frames used (?)");
                }

                int convertedSamples = convert.output_frames_gen * CHANNEL_COUNT;
                src_float_to_short_array(dataOut, finalShortFormat, convertedSamples);
            }
            else
            {
                // convert only
                src_float_to_short_array(pBuffer, finalShortFormat, inSamples);
            }
        }

        ulong remainingBytes = (ulong)finalBufferBytes;

        // SBC encoding loop
        while (remainingBytes > 0)
        {
            ulong inBlockSizeBytes = Math.Min(remainingBytes, _encoder.CodeSize);
            ulong startOffsetBytes = (ulong)finalBufferBytes - remainingBytes;
            byte* srcBlock = (byte*)finalShortFormat + (int)startOffsetBytes;

            fixed (byte* pDstSbcBlock = _dstSbcBlock)
            {
                _encoder.Encode(srcBlock, pDstSbcBlock, inBlockSizeBytes, out ulong sbcEncodedBytes);

                if (sbcEncodedBytes == 0)
                {
                    Debug.WriteLine("Not encoded");
                }
                else
                {
                    // write the new encoded block at the end of the ring buffer
                    SbcAudioData.CopyFrom(_dstSbcBlock, (int)sbcEncodedBytes);
                }
            }

            remainingBytes -= inBlockSizeBytes;
        }
    }
}