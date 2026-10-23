namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// A response body that serves <c>data</c> in chunks and then ends, drops the connection, or stalls.
/// </summary>
internal sealed class ScriptedStream(byte[] data, int stopAfter, StreamEnding ending, int chunkSize = int.MaxValue) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= stopAfter && ending == StreamEnding.Stall)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return ReadChunk(buffer.Span);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= stopAfter && ending == StreamEnding.Stall)
        {
            throw new NotSupportedException("A stalling stream can only be read asynchronously.");
        }

        return ReadChunk(buffer.AsSpan(offset, count));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int ReadChunk(Span<byte> buffer)
    {
        if (_position >= stopAfter)
        {
            return ending == StreamEnding.Drop ? throw new IOException("The connection was reset.") : 0;
        }

        int count = Math.Min(Math.Min(buffer.Length, chunkSize), stopAfter - _position);
        data.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }
}
