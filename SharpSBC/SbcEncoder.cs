using System;
using System.Diagnostics.CodeAnalysis;

using static SharpSBC.Native;

namespace SharpSBC;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class SbcEncoder : IDisposable
{
    private sbc_t _sbc;

    public SbcEncoder(
        int sampleRate,
        SubBandCount subBandsCount,
        int bitPool,
        ChannelMode channelMode,
        AllocationMode snr,
        BlockCount blocks
    )
    {
        _sbc = new sbc_t();

        int sbcInit = sbc_init(ref _sbc, 0);
        if (sbcInit < 0)
        {
            throw new Exception("Could not init SBC Encoder");
        }

        _sbc.frequency = sampleRate switch
        {
            16000 => SBC_FREQ_16000,
            32000 => SBC_FREQ_32000,
            44100 => SBC_FREQ_44100,
            48000 => SBC_FREQ_48000,
            _ => _sbc.frequency
        };

        _sbc.subbands = (byte)subBandsCount;

        _sbc.mode = (byte)channelMode;

        _sbc.endian = SBC_LE;

        _sbc.bitpool = (byte)bitPool;
        _sbc.allocation = (byte)snr;

        _sbc.blocks = (byte)blocks;

        CodeSize = sbc_get_codesize(ref _sbc);
        FrameSize = sbc_get_frame_length(ref _sbc);
    }

    /// <summary>
    ///     SBC input block size in bytes.
    /// </summary>
    public ulong CodeSize { get; }
    
    /// <summary>
    ///     SBC output block (frame) size in bytes.
    /// </summary>
    public ulong FrameSize { get; }

    public void Dispose()
    {
        sbc_finish(ref _sbc);
    }

    public long Encode(ReadOnlySpan<byte> src, ReadOnlySpan<byte> dst, ulong dstSize, out ulong encoded)
    {
        ulong tmp;
        long len;

        unsafe
        {
            fixed (byte* pSrc = src)
            fixed (byte* pDst = dst)
            {
                len = Encode(pSrc, pDst, dstSize, out tmp);
            }
        }

        encoded = tmp;

        return len;
    }

    public unsafe long Encode(byte* src, byte* dst, ulong dstSize, out ulong encoded)
    {
        ulong tmp;

        long len = sbc_encode(ref _sbc, src, CodeSize, dst, dstSize, &tmp);

        encoded = tmp;

        return len;
    }
}