namespace IniaAi.Testing.Fixtures;

/// <summary>
/// A stream over bytes that reads forward only and cannot say its length, as a network stream does,
/// so a test can prove a store takes a stream it cannot measure or rewind.
/// </summary>
public sealed class ForwardOnlyStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _inner.Read(buffer, offset, count);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
