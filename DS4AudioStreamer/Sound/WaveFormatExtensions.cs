using NAudio.Wave;

namespace DS4AudioStreamer.Sound;

public static class WaveFormatExtensions
{
    public static long GetMaxBufferSize(this WaveFormat format, int bufferMilliseconds)
    {
        if (bufferMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMilliseconds));
        }

        // If BitsPerSample is known, calculate directly
        if (format.BitsPerSample > 0)
        {
            int bytesPerSample = format.BitsPerSample / 8;
            return (long)Math.Ceiling(
                format.SampleRate * bytesPerSample * format.Channels * bufferMilliseconds / 1000.0
            );
        }

        // Fallback for compressed / weird formats: use average bytes per second
        if (format.AverageBytesPerSecond > 0)
        {
            return (long)Math.Ceiling(
                format.AverageBytesPerSecond * bufferMilliseconds / 1000.0
            );
        }

        throw new InvalidOperationException(
            "Cannot determine buffer size from WaveFormat — missing BitsPerSample and AverageBytesPerSecond."
        );
    }
}