namespace RouteAudio.Audio;

/// <summary>单写单读的 16-bit 环形缓冲（锁实现，追求正确性；后续可换无锁版本）。</summary>
public sealed class ShortRingBuffer
{
    private readonly short[] _buffer;
    private readonly object _gate = new();
    private int _read;
    private int _write;
    private int _count;

    public ShortRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new short[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_gate) return _count;
        }
    }

    public void Write(ReadOnlySpan<short> data)
    {
        lock (_gate)
        {
            foreach (var sample in data)
            {
                _buffer[_write] = sample;
                _write = (_write + 1) % _buffer.Length;
                if (_count < _buffer.Length) _count++;
                else _read = (_read + 1) % _buffer.Length; // 覆盖最旧数据（溢出保护）
            }
        }
    }

    public int Read(Span<short> destination)
    {
        lock (_gate)
        {
            var toRead = Math.Min(destination.Length, _count);
            for (var i = 0; i < toRead; i++)
            {
                destination[i] = _buffer[_read];
                _read = (_read + 1) % _buffer.Length;
            }

            _count -= toRead;
            return toRead;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _read = 0;
            _write = 0;
            _count = 0;
        }
    }
}
