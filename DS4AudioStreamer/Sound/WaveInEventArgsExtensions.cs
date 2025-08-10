using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DS4AudioStreamer.Sound;

public static class WaveInEventArgsExtensions
{
    /// <summary>
    ///     Calculates the number of audio frames recorded using the provided <see cref="WaveInEventArgs" /> and device's wave
    ///     format.
    /// </summary>
    /// <param name="args">The <see cref="WaveInEventArgs" /> containing the audio buffer and recorded bytes.</param>
    /// <param name="device">
    ///     The <see cref="WasapiCapture" /> device used for capturing audio, providing the wave format
    ///     details.
    /// </param>
    /// <returns>
    ///     The number of audio frames recorded, calculated by dividing the bytes recorded by the block alignment of the
    ///     device's wave format.
    /// </returns>
    public static int GetNumFrames(this WaveInEventArgs args, WasapiCapture device)
    {
        return args.BytesRecorded / device.WaveFormat.BlockAlign;
    }
}