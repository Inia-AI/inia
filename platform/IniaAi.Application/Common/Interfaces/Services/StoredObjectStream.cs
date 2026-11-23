namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// A stored file read as it streams from its store, with its size known before the first read, so a
/// reader that needs the whole file allocates it once at that size. It cannot seek. Disposing it
/// releases what holds the read open, such as a database connection or an object store response.
/// </summary>
public sealed class StoredObjectStream : Stream
{
    private readonly Stream _content;
    private readonly IDisposable? _owner;
    private long _position;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredObjectStream"/> class.
    /// </summary>
    /// <param name="content">The stream the store answered with.</param>
    /// <param name="length">The stored size in bytes.</param>
    /// <param name="owner">What holds the read open; disposed after <paramref name="content"/>.</param>
    public StoredObjectStream(Stream content, long length, IDisposable? owner = null)
    {
        _content = content;
        Length = length;
        _owner = owner;
    }

    /// <summary>
    /// The size of a stored file before its first read: what a <see cref="StoredObjectStream"/> was
    /// told, or what is left of a stream that can seek. Null when neither says.
    /// </summary>
    public static long? LengthOf(Stream content)
    {
        return content switch
        {
            StoredObjectStream stored => stored.Length,
            { CanSeek: true } => content.Length - content.Position,
            _ => null,
        };
    }

    /// <summary>
    /// Every byte left in a stream: one array of the stored size when the size is known, a copy otherwise.
    /// The stream is read to its end, where an S3 client checks the file's checksum, and a stream
    /// longer than its stored size is refused instead of cut short.
    /// </summary>
    public static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (LengthOf(content) is not long length)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            return copy.ToArray();
        }

        byte[] bytes = new byte[length];
        await content.ReadExactlyAsync(bytes, cancellationToken);
        if (await content.ReadAsync(new byte[1], cancellationToken) != 0)
        {
            throw new InvalidDataException($"The stored file holds more than its stored size of {length} bytes.");
        }

        return bytes;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <summary>
    /// The stored size in bytes, known before the first read even though the stream cannot seek.
    /// </summary>
    public override long Length { get; }

    /// <summary>
    /// The number of bytes read so far. It cannot be set.
    /// </summary>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Count(_content.Read(buffer, offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        return Count(_content.Read(buffer));
    }

    /// <inheritdoc />
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return Count(await _content.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return Count(await _content.ReadAsync(buffer, cancellationToken));
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _content.Dispose();
            _owner?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _content.DisposeAsync();
            if (_owner is IAsyncDisposable asyncOwner)
            {
                await asyncOwner.DisposeAsync();
            }
            else
            {
                _owner?.Dispose();
            }
        }

        await base.DisposeAsync();
    }

    private int Count(int read)
    {
        _position += read;
        return read;
    }
}
