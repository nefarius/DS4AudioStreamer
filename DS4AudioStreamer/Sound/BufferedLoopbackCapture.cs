using NAudio.CoreAudioApi;

namespace DS4AudioStreamer.Sound;

/// <summary>
///     Represents a buffered WASAPI capture for loopback audio.
///     This class captures audio playing through the system's default output device
///     while using a buffered mechanism for a specified buffer duration.
/// </summary>
/// <remarks>https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording</remarks>
public class BufferedLoopbackCapture(MMDevice captureDevice)
    : WasapiCapture(captureDevice, true, 0)
{
    /// <summary>Specify loopback</summary>
    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
    {
        return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}