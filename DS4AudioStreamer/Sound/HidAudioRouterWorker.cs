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
        void OnSbcAudioFramesAvailable(object? sender, EventArgs args)
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
        SendControllerDataReport();
        _stream.Start();
        _workerThread.Start();
    }
    
    private void SendControllerDataReport()
    {
        int size = 78;
        Array.Fill<byte>(_outputBuffer, 0);

        _outputBuffer[0] = 0x11;            // Report ID
        _outputBuffer[1] = 0x40 | 0x80;     // Mode Type
        _outputBuffer[2] = 0xA2;            // Transaction type
        _outputBuffer[3] = 0xF3;            // Common Flags enable rumble (0x01), lightbar (0x02), flash (0x04) Headphone volume L (0x10), Headphone volume R (0x20), Mic volume (0x40), Speaker volume (0x80)

        _outputBuffer[6] = 0;               // Rumble Right
        _outputBuffer[7] = 0;               // Rumble Left

        _outputBuffer[8] = 255;             // Light Bar Red
        _outputBuffer[9] = 0;               // Light Bar Green
        _outputBuffer[10] = 255;            // Light Bar Blue

        _outputBuffer[11] = 0;              // Flash On
        _outputBuffer[12] = 0;              // Flash Off

        _outputBuffer[21] = 0x50;           // Left Channel Volume      int: 0-80
        _outputBuffer[22] = 0x50;           // Right Channel Volume     int: 0-80
        _outputBuffer[23] = 0x50;           // Mic Volume               int: 0-80
        _outputBuffer[24] = 0x50;           // Speaker Volume           int: 0-80

        uint crc = ComputeCrc32(_outputBuffer, 0, size - 4);
        BitConverter.GetBytes(crc).CopyTo(_outputBuffer, size - 4);

        _hidDevice.WriteOutputReportViaInterrupt(_outputBuffer.AsSpan()[..size]);
    }

    private uint ComputeCrc32(byte[] data, int offset, int length)
    {
        uint crc = ~0xEADA2D49u;
        for (int i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            for (int b = 0; b < 8; b++)
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xEDB88320u
                    : crc >> 1;
        }
        return ~crc;
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