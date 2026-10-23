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
