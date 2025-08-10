using DS4Windows;

using FFT.CRC;

namespace DS4AudioStreamer.Sound;

public class HidAudioRouterWorker : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HidDevice _hidDevice;

    private readonly byte[] _outputBuffer = new byte[640];

    private readonly SbcAudioStream _stream;

    private readonly Thread _workerThread;

    public HidAudioRouterWorker(
        HidDevice hidDevice
    )
    {
        _hidDevice = hidDevice;

        ArgumentNullException.ThrowIfNull(hidDevice.SafeReadHandle);

        // TODO: this does nothing depending on which WinAPI we use on the device
        NativeMethods.HidD_SetNumInputBuffers(hidDevice.SafeReadHandle.DangerousGetHandle(), 2);

        _stream = new SbcAudioStream();
        _workerThread = new Thread(_worker);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _cts.Cancel();
        _workerThread.Join();
        _cts.Dispose();
        _hidDevice.Dispose();
        _stream.Dispose();
    }

    private unsafe void _worker()
    {
        byte btHeader = 0xa2;
        ReadOnlySpan<byte> btHeaderSpan = new(&btHeader, 1);
        ushort lilEndianCounter = 0;
        using AutoResetEvent framesAvailableEvent = new(false);

        _stream.SbcAudioFramesAvailable += OnSbcAudioFramesAvailable;

        while (!_cts.IsCancellationRequested)
        {
            if (!framesAvailableEvent.WaitOne(TimeSpan.FromMilliseconds(MaxFramesAvailableWaitMilliseconds)))
            {
                continue;
            }

            CircularBuffer<byte> audioData = _stream.SbcAudioData;
            int frameSize = _stream.FrameSize;

            while (_stream.CurrentFrameCount >= 2)
            {
                int framesAvailable, size, protocol;

                if (_stream.CurrentFrameCount >= 4)
                {
                    framesAvailable = 4;
                    protocol = 0x17;
                    size = 462; // typically 4 * 109 = 436 bytes of audio
                }
                else
                {
                    framesAvailable = 2;
                    protocol = 0x14;
                    size = 270; // typically 2 * 109 = 218 bytes of audio
                }

                Array.Fill<byte>(_outputBuffer, 0);

                _outputBuffer[0] = (byte)protocol;
                _outputBuffer[1] = 0x40; // Unknown
                _outputBuffer[2] = 0xa2; // Unknown

                // packet counter
                _outputBuffer[3] = (byte)(lilEndianCounter & 0xFF);
                _outputBuffer[4] = (byte)((lilEndianCounter >> 8) & 0xFF);

                // TODO: setting volume happens in a different report,
                // which we do not currently implement in this demo loop

                _outputBuffer[5] = 0x02; // 0x02 Speaker Mode On / 0x24 Headset Mode On
                //_outputBuffer[5] = 0x24; // 0x02 Speaker Mode On / 0x24 Headset Mode On

                lilEndianCounter += (ushort)framesAvailable;

                // splice audio data into packet
                audioData.CopyTo(_outputBuffer, 6, framesAvailable * frameSize);

                // controller ignores packets without the proper checksum
                uint crc = CRC32Calculator.SEED;
                CRC32Calculator.Add(ref crc, btHeaderSpan);
                CRC32Calculator.Add(ref crc, _outputBuffer.AsSpan(0, size - 4));
                crc = CRC32Calculator.Finalize(crc);

                // splice CRC into packet
                _outputBuffer[size - 4] = (byte)crc;
                _outputBuffer[size - 3] = (byte)(crc >> 8);
                _outputBuffer[size - 2] = (byte)(crc >> 16);
                _outputBuffer[size - 1] = (byte)(crc >> 24);

                _hidDevice.WriteOutputReportViaInterrupt(_outputBuffer.AsSpan()[..size]);
            }
        }

        _stream.SbcAudioFramesAvailable -= OnSbcAudioFramesAvailable;
        return;

        // event-based waiting for data to go easy on wasting CPU cycles
        void OnSbcAudioFramesAvailable(object? sender, SbcAudioFramesAvailableEventArgs args)
        {
            if (_stream.CurrentFrameCount < MinBufferedFramesRequired)
            {
                return;
            }

            // ReSharper disable once AccessToDisposedClosure
            framesAvailableEvent.Set();
        }
    }

    public void Start()
    {
        _stream.Start();
        _workerThread.Start();
    }

    #region Tuning

    /// <summary>
    ///     Represents the minimum number of buffered SBC audio frames required
    ///     to proceed with audio processing or transmission.
    ///     This constant is used within the HidAudioRouterWorker class to ensure
    ///     that a sufficient number of audio frames are available in the buffer
    ///     before further processing or transmission is initiated. It helps achieve
    ///     stability and prevents underflow scenarios when working with audio data.
    /// </summary>
    /// <remarks>Lowering this value can reduce latency but increases the risk of buffer underruns.</remarks>
    private const int MinBufferedFramesRequired = 4;

    /// <summary>
    ///     Specifies the maximum time, in milliseconds, to wait for the availability of SBC audio frames
    ///     before continuing processing in the HID audio routing worker loop.
    ///     This constant ensures efficient synchronization and prevents excessive CPU usage
    ///     by limiting the duration of the wait interval when listening for available audio frames.
    /// </summary>
    /// <remarks>This is not a critical value; however, CPU usage will increase if set too low.</remarks>
    private const int MaxFramesAvailableWaitMilliseconds = 20;

    #endregion
}