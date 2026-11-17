using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncDroidAndroid.Helpers;

public sealed class ProgressTrackingStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long, string>? _throttledCallback;
    private readonly Stopwatch _totalSw = Stopwatch.StartNew();
    private readonly Stopwatch _windowSw = Stopwatch.StartNew();
    private long _windowBytes;

    public long TotalBytesTransferred { get; private set; }
    public double ElapsedSeconds => _totalSw.Elapsed.TotalSeconds;

    public ProgressTrackingStream(Stream inner, Action<long, string>? throttledCallback = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _throttledCallback = throttledCallback;
    }

    private void OnBytesTransferred(int count)
    {
        if (count <= 0) return;
        TotalBytesTransferred += count;
        _windowBytes += count;

        if (_throttledCallback != null && _windowSw.ElapsedMilliseconds >= 1500)
        {
            double winSec = _windowSw.Elapsed.TotalSeconds;
            string speedStr = FileHashHelper.FormatTransferSpeed(_windowBytes, winSec);
            _windowBytes = 0;
            _windowSw.Restart();
            _throttledCallback(TotalBytesTransferred, speedStr);
        }
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _inner.Read(buffer, offset, count);
        OnBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        OnBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int bytesRead = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        OnBytesTransferred(bytesRead);
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        OnBytesTransferred(count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        OnBytesTransferred(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        OnBytesTransferred(buffer.Length);
    }

    /// <summary>
    /// Copies from <paramref name="source"/> into this stream while enforcing an idle timeout on each read/write operation.
    /// Throws <see cref="TimeoutException"/> if no bytes are transferred within <paramref name="idleTimeout"/>,
    /// or <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> is triggered.
    /// </summary>
    public async Task CopyFromWithIdleTimeoutAsync(
        Stream source,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken = default,
        int bufferSize = 65536,
        Action? onIdleTimeoutAbort = null)
    {
        byte[] buffer = new byte[bufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idleCts.CancelAfter(idleTimeout);

            int bytesRead;
            try
            {
                using var reg = idleCts.Token.Register(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        try { onIdleTimeoutAbort?.Invoke(); } catch { }
                    }
                });
                bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), idleCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && idleCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Stream transfer stalled: no data received within {idleTimeout.TotalSeconds:F0} seconds.",
                    ex);
            }

            if (bytesRead <= 0)
            {
                break;
            }

            await WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _totalSw.Stop();
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
