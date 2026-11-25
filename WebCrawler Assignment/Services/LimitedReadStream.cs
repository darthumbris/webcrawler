namespace WebCrawler.Services;

public sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _bytesRead;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _bytesRead;
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (offset > buffer.Length - count)
        {
            throw new ArgumentException(
                "The offset and count exceed the buffer bounds.");
        }
        if (count == 0)
        {
            return 0;
        }

        EnsureCanReadMore();

        var allowedLength = (int)Math.Min(count, maxBytes - _bytesRead);
        var bytesRead = inner.Read(buffer, offset, allowedLength);

        _bytesRead += bytesRead;
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }
        EnsureCanReadMore();
        var allowedLength = (int)Math.Min(buffer.Length, maxBytes - _bytesRead);

        var bytesRead = await inner.ReadAsync(buffer[..allowedLength], cancellationToken);
        _bytesRead += bytesRead;
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (offset > buffer.Length - count)
        {
            throw new ArgumentException(
                "The offset and count exceed the buffer bounds.");
        }
        if (count == 0)
        {
            return 0;
        }

        EnsureCanReadMore();

        var allowedLength = (int)Math.Min(
            count,
            maxBytes - _bytesRead);

        var bytesRead = await inner.ReadAsync(buffer.AsMemory(offset, allowedLength), cancellationToken);

        _bytesRead += bytesRead;
        return bytesRead;
    }

    private void EnsureCanReadMore()
    {
        if (_bytesRead >= maxBytes)
        {
            throw new InvalidDataException(
                $"The decompressed sitemap exceeded the maximum size of {maxBytes} bytes.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Flush() =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
