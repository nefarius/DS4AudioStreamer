using static SharpSBC.Native;

namespace SharpSBC;

public enum AllocationMode
{
    Loudness = SBC_AM_LOUDNESS,
    Snr = SBC_AM_SNR
}