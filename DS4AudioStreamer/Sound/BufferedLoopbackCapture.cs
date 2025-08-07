using NAudio.CoreAudioApi;

namespace DS4AudioStreamer.Sound;

public class BufferedLoopbackCapture(MMDevice captureDevice, int audioBufferMillisecondsLength)
    : WasapiCapture(captureDevice, true, audioBufferMillisecondsLength)
{
    /// <summary>Specify loopback</summary>
    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
    {
        return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}