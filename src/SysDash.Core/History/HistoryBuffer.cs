namespace SysDash.Core.History;

/// <summary>
/// Fixed-capacity ring buffer for sparkline history values.
/// Not thread-safe; mutating and reading happen on the UI thread for each widget.
/// </summary>
public sealed class HistoryBuffer
{
    private readonly double[] _values;
    private int _head;
    private int _count;

    public HistoryBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _values = new double[capacity];
    }

    public int Capacity => _values.Length;

    public int Count => _count;

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0;
        }

        _values[_head] = value;
        _head = (_head + 1) % _values.Length;

        if (_count < _values.Length)
        {
            _count++;
        }
    }

    /// <summary>Returns values in chronological order.</summary>
    public double[] ToArray()
    {
        var result = new double[_count];

        for (int i = 0; i < _count; i++)
        {
            result[i] = _values[(_head - _count + _values.Length + i) % _values.Length];
        }

        return result;
    }

    public double Max()
    {
        double max = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_values[i] > max)
            {
                max = _values[i];
            }
        }
        return max;
    }

    public double Last()
    {
        if (_count == 0)
        {
            return 0;
        }

        return _values[(_head - 1 + _values.Length) % _values.Length];
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        Array.Clear(_values);
    }
}