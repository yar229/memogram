using System.Buffers.Text;

namespace Memogram;

/// <summary>
/// A forward-only stream that encodes the bytes read from an underlying source
/// as base64 (UTF-8), producing output on the fly with constant memory usage.
/// </summary>
public sealed class Base64Stream : Stream
{
    private const int InputChunkSize = 3072;    // multiple of 3
    private const int OutputBufferSize = 4096;  // InputChunkSize * 4 / 3

    private readonly Stream _source;
    private readonly bool _leaveOpen;
    private readonly byte[] _input = new byte[InputChunkSize + 2];
    private readonly byte[] _output = new byte[OutputBufferSize];

    private int _pendingCount;
    private int _outputPos;
    private int _outputLen;
    private bool _endReached;
    private bool _disposed;

    public Base64Stream(Stream source, bool leaveOpen = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            if (_outputPos < _outputLen)
            {
                int n = Math.Min(buffer.Length, _outputLen - _outputPos);
                _output.AsSpan(_outputPos, n).CopyTo(buffer);
                _outputPos += n;
                return n;
            }

            if (_endReached)
                return 0;

            Produce();
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_outputPos < _outputLen)
            {
                int n = Math.Min(buffer.Length, _outputLen - _outputPos);
                _output.AsMemory(_outputPos, n).CopyTo(buffer);
                _outputPos += n;
                return n;
            }

            if (_endReached)
                return 0;

            await ProduceAsync(cancellationToken);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && !_leaveOpen)
                _source.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (!_leaveOpen)
                await _source.DisposeAsync();
        }
        base.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Produce()
    {
        _outputPos = 0;
        _outputLen = 0;

        int nRead = _source.Read(_input, _pendingCount, InputChunkSize);
        if (nRead == 0)
        {
            _endReached = true;
            if (_pendingCount > 0)
                FlushRemaining();
            return;
        }

        int total = _pendingCount + nRead;
        Base64.EncodeToUtf8(_input.AsSpan(0, total), _output, out int consumed, out int written);
        _outputLen = written;

        int leftover = total - consumed;
        if (leftover > 0)
            _input.AsSpan(consumed, leftover).CopyTo(_input);
        _pendingCount = leftover;
    }

    private async ValueTask ProduceAsync(CancellationToken ct)
    {
        _outputPos = 0;
        _outputLen = 0;

        int nRead = await _source.ReadAsync(_input.AsMemory(_pendingCount, InputChunkSize), ct);
        if (nRead == 0)
        {
            _endReached = true;
            if (_pendingCount > 0)
                FlushRemaining();
            return;
        }

        int total = _pendingCount + nRead;
        Base64.EncodeToUtf8(_input.AsSpan(0, total), _output, out int consumed, out int written);
        _outputLen = written;

        int leftover = total - consumed;
        if (leftover > 0)
            _input.AsSpan(consumed, leftover).CopyTo(_input);
        _pendingCount = leftover;
    }

    private void FlushRemaining()
    {
        Span<char> buffer = stackalloc char[4];
        Convert.TryToBase64Chars(_input.AsSpan(0, _pendingCount), buffer, out int charsWritten);
        for (int i = 0; i < charsWritten; i++)
            _output[i] = (byte)buffer[i];
        _outputLen = charsWritten;
        _pendingCount = 0;
    }
}
