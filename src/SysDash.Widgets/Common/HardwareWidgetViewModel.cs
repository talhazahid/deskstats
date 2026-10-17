using SysDash.Core.History;
using SysDash.Core.Mvvm;
using SysDash.Core.Threading;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Common;

/// <summary>
/// Shared base for widgets that visualize a numeric history (sparkline):
/// CPU, RAM, GPU, Network, Drive. Owns the HistoryBuffer and the Sparkline array.
/// </summary>
public abstract class HardwareWidgetViewModel : WidgetViewModel
{
    private HistoryBuffer _buffer;
    private double[] _sparkline = Array.Empty<double>();

    protected HardwareWidgetViewModel(IThreadMarshaller marshaller, WidgetSettings settings, int historyCapacity)
        : base(marshaller, settings)
    {
        _buffer = new HistoryBuffer(Math.Clamp(historyCapacity, 10, 900));
    }

    protected HistoryBuffer History => _buffer;

    /// <summary>Samples the graphs hold (one per refresh) — bound to BarGraph.Capacity.</summary>
    public int GraphCapacity => _buffer.Capacity;

    /// <summary>Resizes a history buffer, keeping the newest samples.</summary>
    protected static HistoryBuffer Resize(HistoryBuffer buffer, int capacity)
    {
        capacity = Math.Clamp(capacity, 10, 900);
        if (buffer.Capacity == capacity)
        {
            return buffer;
        }

        var resized = new HistoryBuffer(capacity);
        foreach (double value in buffer.ToArray().TakeLast(capacity))
        {
            resized.Add(value);
        }

        return resized;
    }

    /// <summary>Applies a new graph time span to the shared history buffer.</summary>
    protected void ResizeHistory(int capacity)
    {
        _buffer = Resize(_buffer, capacity);
        Sparkline = _buffer.ToArray();
    }

    public double[] Sparkline
    {
        get => _sparkline;
        protected set => SetProperty(ref _sparkline, value);
    }

    protected void PushHistory(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0;
        }

        _buffer.Add(value);
        Sparkline = _buffer.ToArray();
    }

    protected static double? ClampPercent(double? v)
        => v is { } x ? Math.Clamp(x, 0, 100) : null;
}