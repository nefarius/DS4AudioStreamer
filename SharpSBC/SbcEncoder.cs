using System;
using System.Diagnostics.CodeAnalysis;

using static SharpSBC.Native;

namespace SharpSBC;

/// <summary>
///     Represents an SBC (Subband Codec) encoder, which is used for encoding audio data
///     into SBC format. This encoder supports settings for sample rate, sub-band count,
///     bit pool, channel mode, allocation mode, and block count.
/// </summary>
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

    /// <summary>
    ///     Encodes audio data from a source buffer into a destination buffer, using the SBC encoding format.
    /// </summary>
    /// <param name="src">The source buffer containing raw audio data to encode.</param>
    /// <param name="dst">The destination buffer that will hold the encoded SBC data.</param>
    /// <param name="dstSize">The maximum size of the destination buffer, in bytes.</param>
    /// <param name="encoded">
    ///     An output parameter that contains the number of bytes successfully encoded into the destination
    ///     buffer.
    /// </param>
    /// <returns>
    ///     The number of bytes consumed from the source buffer during encoding. Returns -1 if encoding fails.
    /// </returns>
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

    /// <summary>
    ///     Encodes audio data from a source buffer into a destination buffer using the SBC encoding format.
    /// </summary>
    /// <param name="src">A pointer to the source buffer containing raw audio data to encode.</param>
    /// <param name="dst">A pointer to the destination buffer where the encoded SBC data will be stored.</param>
    /// <param name="dstSize">The size of the destination buffer, in bytes.</param>
    /// <param name="encoded">
    ///     An output parameter that holds the number of bytes successfully written to the destination
    ///     buffer.
    /// </param>
    /// <returns>
    ///     The number of bytes consumed from the source buffer during the encoding process. Returns -1 if the encoding
    ///     operation fails.
    /// </returns>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public unsafe long Encode(byte* src, byte* dst, ulong dstSize, out ulong encoded)
    {
        ulong tmp;

        long len = sbc_encode(ref _sbc, src, CodeSize, dst, dstSize, &tmp);

        encoded = tmp;

        return len;
    }
}