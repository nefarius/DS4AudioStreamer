using System.Diagnostics;
using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using SharpSBC;

using static SharpSampleRate.SampleRate;

using ChannelMode = SharpSBC.ChannelMode;

namespace DS4AudioStreamer.Sound;

/// <summary>
///     Represents a stream of SBC (Low Complexity Subband Codec) encoded audio data, specifically tailored for use with
///     DS4-compatible devices.
/// </summary>
public class SbcAudioStream : IDisposable
{
    // target sample rate
    private const int SbcSampleRate = 32_000;

    // target channel count for speaker or headset (stereo)
    private const int SbcChannelCount = 2;

    private readonly WasapiCapture _captureDevice;
    private readonly SbcEncoder _encoder;
    private readonly double _resampleRatio;
    private readonly IntPtr _resamplerState;
    private readonly byte[] _sbcPostBuffer;
    private readonly byte[] _sbcPreBuffer;
    private readonly CircularBuffer<byte> _sourceAudioBuffer;

    /// <summary>
    ///     Represents an audio stream using SBC (Subband Coding) encoding and captures audio data from a WASAPI capture
    ///     device.
    /// </summary>
    public SbcAudioStream()
    {
        // SBC Encoder setting compatible with DS4 variants
        _encoder = new SbcEncoder(
            SbcSampleRate,
            SubBandCount.Sb8,
            48,
            ChannelMode.JointStereo,
            AllocationMode.Snr,
            BlockCount.Blk16
        );

        _sbcPreBuffer = new byte[_encoder.CodeSize];
        _sbcPostBuffer = new byte[_encoder.FrameSize];

        // Capture Device (default output device)
        MMDevice? device = new MMDeviceEnumerator()
            .GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        Console.WriteLine($"Default output device: {device.FriendlyName}");

        _captureDevice = new BufferedLoopbackCapture(device);
        _captureDevice.DataAvailable += OnAudioCaptured;

        // Resampler
        _resamplerState = src_new(Quality.SRC_SINC_BEST_QUALITY, SbcChannelCount, out int error);
        if (IntPtr.Zero == _resamplerState)
        {
            throw new Exception(src_strerror(error));
        }

        _resampleRatio = SbcSampleRate / (double)_captureDevice.WaveFormat.SampleRate;

        // Buffers
        int bufferSize = _captureDevice.WaveFormat.ConvertLatencyToByteSize(32);

        Console.WriteLine($"SBC Buffer size: {bufferSize}");

        // TODO: fix size calculation
        _sourceAudioBuffer = new CircularBuffer<byte>(bufferSize);
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

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _captureDevice.Dispose();
        _encoder.Dispose();
    }

    public event EventHandler<EventArgs>? SbcAudioFramesAvailable;

    public void Start()
    {
        _captureDevice.StartRecording();
    }

    public void Stop()
    {
        _captureDevice.StopRecording();
    }

    /// <summary>
    ///     Invoked when PCM captured data is available for processing.
    /// </summary>
    /// <remarks>
    ///     This is expected to be invoked roughly every 10 milliseconds with a fixed buffer size,
    ///     except for silence.
    ///     Behavior might change if a different capture device is used.
    /// </remarks>
    private unsafe void OnAudioCaptured(object? sender, WaveInEventArgs e)
    {
        WasapiCapture? device = sender as WasapiCapture;
        ArgumentNullException.ThrowIfNull(device);
        int inFrameCount = e.GetNumFrames(device);

        // smaller when downsampling, larger when upsampling
        int outFrameCount = (int)Math.Ceiling(inFrameCount * _resampleRatio);
        int inSamples = inFrameCount * SbcChannelCount;
        int outSamples = outFrameCount * SbcChannelCount;

        short* finalShortFormat = stackalloc short[outSamples];
        int finalSampleCount = 0;

        fixed (float* pBuffer = MemoryMarshal.Cast<byte, float>(e.Buffer))
        {
            if (SbcSampleRate != _captureDevice.WaveFormat.SampleRate)
            {
                // resample and convert
                float* dataIn = stackalloc float[inSamples];
                float* dataOut = stackalloc float[outSamples];

                // copy so both (in and out) memory regions are on the stack
                Buffer.MemoryCopy(
                    pBuffer,
                    dataIn,
                    inSamples * sizeof(float),
                    inSamples * sizeof(float)
                );

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

                // plausibility check
                if (convert.input_frames != convert.input_frames_used)
                {
                    // TODO: error handling
                    Debug.WriteLine("Not all frames used (?)");
                }

                // downlsampling can decrease the output frame count, upsampling could increase it
                int convertedSamples = convert.output_frames_gen * SbcChannelCount;
                src_float_to_short_array(dataOut, finalShortFormat, convertedSamples);
                finalSampleCount = convertedSamples;
            }
            else
            {
                // convert only
                src_float_to_short_array(pBuffer, finalShortFormat, inSamples);
                finalSampleCount = inSamples;
            }
        }

        Span<short> shortSpan = new(finalShortFormat, outSamples);
        Span<byte> byteSpan = MemoryMarshal.AsBytes(shortSpan);

        // managed instance of our resampled and converted audio stream
        _sourceAudioBuffer.CopyFrom(byteSpan.ToArray(), finalSampleCount * sizeof(short));

        // SBC encoding - we must have enough samples buffered to fill at least one block
        while (_sourceAudioBuffer.CurrentLength >= (int)_encoder.CodeSize)
        {
            // pop block from the ring buffer
            _sourceAudioBuffer.CopyTo(_sbcPreBuffer, (int)_encoder.CodeSize);

            _encoder.Encode(_sbcPreBuffer, _sbcPostBuffer, _encoder.CodeSize, out ulong length);

            if (length == 0)
            {
                // TODO: error handling
                Console.WriteLine("Not encoded");
            }
            else
            {
                // push frame to final buffer
                SbcAudioData.CopyFrom(_sbcPostBuffer, (int)length);
            }
        }

        if (CurrentFrameCount > 0)
        {
            SbcAudioFramesAvailable?.Invoke(this, EventArgs.Empty);
        }
    }
}