using NAudio.CoreAudioApi;

namespace DS4AudioStreamer.Sound;

/// <summary>
///     Represents a buffered WASAPI capture for loopback audio.
///     This class captures audio playing through the system's default output device
///     while using a buffered mechanism for a specified buffer duration.
/// </summary>
public class BufferedLoopbackCapture(MMDevice captureDevice, int audioBufferMillisecondsLength)
    : WasapiCapture(captureDevice, true, audioBufferMillisecondsLength)
{
    /// <summary>Specify loopback</summary>
    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
    {
        return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}