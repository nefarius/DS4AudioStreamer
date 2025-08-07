using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DS4AudioStreamer.Sound;

public static class WaveInEventArgsExtensions
{
    public static int GetSampleFrameCount(this WaveInEventArgs args, WasapiCapture device)
    {
        return args.BytesRecorded / device.WaveFormat.BlockAlign;
    }

}