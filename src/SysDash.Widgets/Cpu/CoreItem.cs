using SysDash.Core.Mvvm;

namespace SysDash.Widgets.Cpu;

/// <summary>One core entry shown in the per-core bar row.</summary>
public sealed class CoreItem : ObservableObject
{
    private string _name = "C";
    private double _load;
    private double _clockMhz;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Load
    {
        get => _load;
        set => SetProperty(ref _load, value);
    }

    public double ClockMhz
    {
        get => _clockMhz;
        set => SetProperty(ref _clockMhz, value);
    }
}