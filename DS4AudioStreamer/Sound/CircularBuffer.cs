/*
 * NOTE: I wonder where this implementation is from, it looks a bit like
 * https://github.com/joaoportela/CircularBuffer-CSharp but not quite.
 * Ultimately, we want the individual types used to be decoupled for easy reuse, some day :)
 */

using System.Diagnostics.CodeAnalysis;

namespace DS4AudioStreamer.Sound;

/// <summary>
///     A thread-safe circular (ring) buffer for unmanaged value types.
/// </summary>
/// <typeparam name="T">
///     The element type stored in the buffer. Must be an unmanaged type
///     (e.g., primitive numeric type, struct without managed references).
/// </typeparam>
/// <remarks>
///     <para>
///         The buffer behaves as a fixed-size FIFO queue. When new data exceeds
///         <see cref="Capacity" />, the oldest data is overwritten automatically.
///     </para>
///     <para>
///         All operations are thread-safe via an internal lock.
///     </para>
/// </remarks>
public class CircularBuffer<T> where T : unmanaged
{
    private readonly T[] _backingBuffer;
    private readonly Lock _sync = new();
    private int _end;

    private bool _hasData;

    private int _start;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CircularBuffer{T}" /> class
    ///     with the specified capacity.
    /// </summary>
    /// <param name="size">
    ///     The number of <typeparamref name="T" /> elements that the buffer can hold.
    ///     This is the maximum number of elements that can be stored without overwriting.
    /// </param>
    public CircularBuffer(int size)
    {
        _backingBuffer = new T[size];
        _start = 0;
        _end = 0;
    }

    /// <summary>
    ///     Gets the maximum number of elements that the buffer can hold.
    /// </summary>
    /// <remarks>
    ///     The size in bytes is <c>Capacity * sizeof(T)</c>.
    /// </remarks>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public int Capacity => _backingBuffer.Length;

    /// <summary>
    ///     Gets the current number of elements stored in the buffer.
    /// </summary>
    /// <remarks>
    ///     The size in bytes is <c>CurrentLength * sizeof(T)</c>.
    /// </remarks>
    public int CurrentLength
    {
        get
        {
            lock (_sync)
            {
                if (_end > _start)
                {
                    return _end - _start;
                }

                if (_end < _start)
                {
                    return _end + Capacity - _start;
                }

                return _hasData ? Capacity : 0;
            }
        }
    }

    /// <summary>
    ///     Copies elements from a source array into the buffer, overwriting the
    ///     oldest data if necessary.
    /// </summary>
    /// <param name="arr">
    ///     The source array containing the elements to write.
    /// </param>
    /// <param name="length">
    ///     The number of elements from <paramref name="arr" /> to write into the buffer.
    ///     Must be less than or equal to <paramref name="arr" /> length.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         If writing <paramref name="length" /> elements would exceed <see cref="Capacity" />,
    ///         the oldest elements in the buffer are discarded to make room.
    ///     </para>
    ///     <para>
    ///         The operation is thread-safe.
    ///     </para>
    /// </remarks>
    public unsafe void CopyFrom(T[] arr, int length)
    {
        lock (_sync)
        {
            int startOffset = 0;
            if (CurrentLength + length >= Capacity)
            {
                startOffset = CurrentLength + length - Capacity;
            }

            if (_end + length > Capacity)
            {
                int newLength = Capacity - _end;
                int remainder = length - newLength;

                Buffer.BlockCopy(arr, 0, _backingBuffer, _end * sizeof(T), newLength * sizeof(T));
                Buffer.BlockCopy(arr, newLength * sizeof(T), _backingBuffer, 0, remainder * sizeof(T));

                _end = remainder;
            }
            else
            {
                Buffer.BlockCopy(arr, 0, _backingBuffer, _end * sizeof(T), length * sizeof(T));
                _end = (_end + length) % Capacity;
            }

            _start = (_start + startOffset) % Capacity;

            _hasData = true;
        }
    }

    /// <summary>
    ///     Copies up to <paramref name="length" /> elements from the buffer into
    ///     <paramref name="destination" /> at the specified offset, removing them
    ///     from the buffer in the process.
    /// </summary>
    /// <param name="destination">The destination array to receive the elements.</param>
    /// <param name="offset">The index in <paramref name="destination" /> where copying begins.</param>
    /// <param name="length">The number of elements to copy.</param>
    /// <remarks>
    ///     <para>
    ///         If fewer than <paramref name="length" /> elements are available, the remaining slots
    ///         in <paramref name="destination" /> are zero-filled (default(<typeparamref name="T" />)).
    ///     </para>
    ///     <para>
    ///         The operation is thread-safe.
    ///     </para>
    /// </remarks>
    public unsafe void CopyTo(T[] destination, int offset, int length)
    {
        lock (_sync)
        {
            int zeroFill = 0;

            // Zero-fill if the request can't be filled with the current buffer contents
            if (length > CurrentLength)
            {
                zeroFill = length - CurrentLength;
                length -= zeroFill;
            }

            if (_start + length > Capacity)
            {
                int newLength = Capacity - _start;
                int remainder = length - newLength;


                Buffer.BlockCopy(_backingBuffer, _start * sizeof(T), destination, offset * sizeof(T),
                    newLength * sizeof(T));
                Buffer.BlockCopy(_backingBuffer, 0, destination, (offset + newLength) * sizeof(T),
                    remainder * sizeof(T));

                _start = remainder;
            }
            else if (length > 0)
            {
                Buffer.BlockCopy(_backingBuffer, _start * sizeof(T), destination, offset * sizeof(T),
                    length * sizeof(T));
                Array.Copy(_backingBuffer, _start, destination, offset, length);

                _start = (_start + length) % Capacity;
            }

            if (zeroFill > 0)
            {
                _hasData = false;
                Array.Fill(destination, new T(), length + offset, zeroFill);
                // TODO: ???
                Console.WriteLine("Glitch");
            }
            else if (_start == _end)
            {
                _hasData = false;
            }
        }
    }

    /// <summary>
    ///     Copies up to <paramref name="length" /> elements from the buffer into
    ///     <paramref name="destination" />, removing them from the buffer in the process.
    /// </summary>
    /// <param name="destination">The destination array to receive the elements.</param>
    /// <param name="length">The number of elements to copy.</param>
    /// <remarks>
    ///     <para>
    ///         If fewer than <paramref name="length" /> elements are available, the remaining slots
    ///         in <paramref name="destination" /> are zero-filled (default(<typeparamref name="T" />)).
    ///     </para>
    ///     <para>
    ///         The operation is thread-safe.
    ///     </para>
    /// </remarks>
    public unsafe void CopyTo(T[] destination, int length)
    {
        lock (_sync)
        {
            int zeroFill = 0;
            // Zero-fill if the request can't be filled with the current buffer contents
            if (length > CurrentLength)
            {
                zeroFill = length - CurrentLength;
                length -= zeroFill;
            }

            if (_start + length >= Capacity)
            {
                int newLength = Capacity - _start;
                int remainder = length - newLength;

                Buffer.BlockCopy(_backingBuffer, _start * sizeof(T), destination, 0, newLength * sizeof(T));
                Buffer.BlockCopy(_backingBuffer, 0, destination, newLength * sizeof(T), remainder * sizeof(T));

                _start = remainder;
            }
            else if (length > 0)
            {
                Buffer.BlockCopy(_backingBuffer, _start * sizeof(T), destination, 0, length * sizeof(T));

                _start = (_start + length) % Capacity;
            }

            if (zeroFill > 0)
            {
                _hasData = false;
                Array.Fill(destination, new T(), length, zeroFill);
            }
            else if (_start == _end)
            {
                _hasData = false;
            }
        }
    }
}