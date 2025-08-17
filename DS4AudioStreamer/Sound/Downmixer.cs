namespace DS4AudioStreamer.Sound;

public static class Downmixer
{
    public static void DownmixToStereo(Span<float> input, Span<float> output, int targetFrames, int sourceChannels)
    {
        if (sourceChannels < 2)
        {
            throw new ArgumentException("Need at least 2 channels to downmix");
        }

        int inIdx = 0;
        int outIdx = 0;

        for (int i = 0; i < targetFrames; i++)
        {
            float left = input[inIdx]; // Front Left
            float right = input[inIdx + 1]; // Front Right

            if (sourceChannels > 2)
            {
                // Center
                if (sourceChannels > 2)
                {
                    left += input[inIdx + 2] * 0.7f;
                    right += input[inIdx + 2] * 0.7f;
                }

                // LFE
                if (sourceChannels > 3)
                {
                    left += input[inIdx + 3] * 0.5f;
                    right += input[inIdx + 3] * 0.5f;
                }

                // SL/SR
                if (sourceChannels > 4)
                {
                    left += input[inIdx + 4] * 0.7f;
                }

                if (sourceChannels > 5)
                {
                    right += input[inIdx + 5] * 0.7f;
                }

                // SBL/SBR (7.1)
                if (sourceChannels > 6)
                {
                    left += input[inIdx + 6] * 0.7f;
                }

                if (sourceChannels > 7)
                {
                    right += input[inIdx + 7] * 0.7f;
                }
            }

            // Prevent clipping by scaling down
            output[outIdx] = left * 0.5f;
            output[outIdx + 1] = right * 0.5f;

            inIdx += sourceChannels;
            outIdx += 2;
        }
    }

    public static void Downmix6To2(Span<float> input, Span<float> output, int frames)
    {
        int inIdx = 0;
        int outIdx = 0;

        for (int i = 0; i < frames; i++)
        {
            // Load 6 floats (L, R, C, LFE, SL, SR)
            // We can’t load all 6 at once with Vector<float> (needs multiple of Vector<float>.Count),
            // so SIMD wins mostly for bulk copy/scale. For mixing, plain scalar math is fine.

            float l = input[inIdx];
            float r = input[inIdx + 1];
            float c = input[inIdx + 2];
            float lfe = input[inIdx + 3];
            float sl = input[inIdx + 4];
            float sr = input[inIdx + 5];

            float left = l + (c * 0.7f) + (lfe * 0.5f) + (sl * 0.7f);
            float right = r + (c * 0.7f) + (lfe * 0.5f) + (sr * 0.7f);

            output[outIdx] = left * 0.5f;
            output[outIdx + 1] = right * 0.5f;

            inIdx += 6;
            outIdx += 2;
        }
    }
}