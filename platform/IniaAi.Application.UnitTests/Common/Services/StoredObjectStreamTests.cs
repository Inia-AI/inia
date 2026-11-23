using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Testing.Fixtures;

namespace IniaAi.Application.UnitTests.Common.Services;

/// <summary>
/// Reading a stored file whole allocates it once, at its stored size, and reads the store's stream
/// to its end: an S3 client checks the file's checksum only there, and a stream longer than the
/// size its store gave would otherwise be cut short without a word.
/// </summary>
public class StoredObjectStreamTests
{
    [Fact]
    public async Task ReadAllAsync_ReturnsTheStoredBytes_AndReadsTheStreamToItsEnd()
    {
        byte[] content = [1, 2, 3, 4, 5];
        var source = new EndWatchingStream(content);

        byte[] read = await StoredObjectStream.ReadAllAsync(new StoredObjectStream(source, content.Length), TestContext.Current.CancellationToken);

        Assert.Equal(content, read);
        Assert.True(source.ReachedTheEnd);
    }

    [Fact]
    public async Task ReadAllAsync_AStreamLongerThanItsStoredSize_IsRefused()
    {
        byte[] content = [1, 2, 3, 4, 5];

        _ = await Assert.ThrowsAsync<InvalidDataException>(() =>
            StoredObjectStream.ReadAllAsync(new StoredObjectStream(new ForwardOnlyStream(content), content.Length - 1), TestContext.Current.CancellationToken));
    }

    private sealed class EndWatchingStream(byte[] content) : Stream
    {
        private readonly ForwardOnlyStream _inner = new(content);

        public bool ReachedTheEnd { get; private set; }

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
            return Watch(_inner.Read(buffer, offset, count));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Watch(await _inner.ReadAsync(buffer, cancellationToken));
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

        private int Watch(int read)
        {
            ReachedTheEnd |= read == 0;
            return read;
        }
    }
}
