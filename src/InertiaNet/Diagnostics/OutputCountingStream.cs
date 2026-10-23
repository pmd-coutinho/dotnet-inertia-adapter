namespace InertiaNet.Diagnostics;

/// <summary>Observes writes without buffering, serializing values, or closing the response stream.</summary>
internal sealed class OutputCountingStream(Stream inner) : Stream
{
    private long _bytes;
    public long BytesWritten => Interlocked.Read(ref _bytes);
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); Interlocked.Add(ref _bytes, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { inner.Write(buffer); Interlocked.Add(ref _bytes, buffer.Length); }
    public override void WriteByte(byte value) { inner.WriteByte(value); Interlocked.Increment(ref _bytes); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        Interlocked.Add(ref _bytes, buffer.Length);
    }
}
