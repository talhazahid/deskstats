using SysDash.Core.Mvvm;

namespace SysDash.Widgets.Timezone;

/// <summary>One timezone row.</summary>
public sealed class ZoneTime : ObservableObject
{
    private string _time = "—";

    public ZoneTime(string label)
    {
        Label = label;
    }

    public string Label { get; }

    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }
}