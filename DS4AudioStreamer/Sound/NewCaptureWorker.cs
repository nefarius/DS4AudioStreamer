using System.Diagnostics;

using DS4Windows;

using FFT.CRC;

namespace DS4AudioStreamer.Sound;

public class NewCaptureWorker : IDisposable
{
    private readonly HidDevice _hidDevice;

    private readonly byte[] _outputBuffer = new byte[640];

    private readonly SbcAudioStream _stream;

    private readonly Thread _workerThread;

    public NewCaptureWorker(
        HidDevice hidDevice
    )
    {
        _hidDevice = hidDevice;

        hidDevice.OpenFileStream(_outputBuffer.Length);

        ArgumentNullException.ThrowIfNull(hidDevice.SafeReadHandle);

        NativeMethods.HidD_SetNumInputBuffers(hidDevice.SafeReadHandle.DangerousGetHandle(), 2);

        _stream = new SbcAudioStream();
        _workerThread = new Thread(_worker);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _hidDevice.Dispose();
        _stream.Dispose();
    }

    private unsafe void _worker()
    {
        byte btHeader = 0xa2;
        ReadOnlySpan<byte> btHeaderSpan = new(&btHeader, 1);
        ushort lilEndianCounter = 0;

        while (true)
        {
            Stopwatch sw = Stopwatch.StartNew();
            CircularBuffer<byte> audioData = _stream.SbcAudioData;
            int frameSize = _stream.FrameSize;

            while (_stream.CurrentFrameCount >= 2)
            {
                int framesAvailable, size, protocol;

                if (_stream.CurrentFrameCount >= 4)
                {
                    framesAvailable = 4;
                    protocol = 0x17;
                    size = 462;
                }
                else
                {
                    framesAvailable = 2;
                    protocol = 0x14;
                    size = 270;
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

                uint crc = CRC32Calculator.SEED;
                CRC32Calculator.Add(ref crc, btHeaderSpan);
                CRC32Calculator.Add(ref crc, new ReadOnlySpan<byte>(_outputBuffer, 0, size - 4));
                crc = CRC32Calculator.Finalize(crc);

                // splice CRC into packet
                _outputBuffer[size - 4] = (byte)crc;
                _outputBuffer[size - 3] = (byte)(crc >> 8);
                _outputBuffer[size - 2] = (byte)(crc >> 16);
                _outputBuffer[size - 1] = (byte)(crc >> 24);

                ArgumentNullException.ThrowIfNull(_hidDevice.FileStream);

                _hidDevice.FileStream.Write(_outputBuffer, 0, size);
                _hidDevice.FileStream.Flush();

                sw.Stop();
                Debug.WriteLine($"HID Output Report calculation took {sw.ElapsedMilliseconds} ms");
            }
        }
    }

    public void Start()
    {
        _stream.Start();
        _workerThread.Start();
    }
}