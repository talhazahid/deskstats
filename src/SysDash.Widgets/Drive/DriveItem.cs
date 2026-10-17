using SysDash.Core.Mvvm;

namespace SysDash.Widgets.Drive;

/// <summary>One drive row.</summary>
public sealed class DriveItem : ObservableObject
{
    private string _name = string.Empty;
    private double _usedPercent;
    private string _usedText = string.Empty;
    private string _freeText = string.Empty;
    private string _ioText = string.Empty;
    private string _totalText = string.Empty;
    private bool _isLow;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double UsedPercent
    {
        get => _usedPercent;
        set => SetProperty(ref _usedPercent, value);
    }

    public string UsedText
    {
        get => _usedText;
        set => SetProperty(ref _usedText, value);
    }

    public string FreeText
    {
        get => _freeText;
        set => SetProperty(ref _freeText, value);
    }

    /// <summary>True when free space is below the widget's warning level (ring turns red).</summary>
    public bool IsLow
    {
        get => _isLow;
        set => SetProperty(ref _isLow, value);
    }

    /// <summary>Drive capacity, e.g. "931 GB".</summary>
    public string TotalText
    {
        get => _totalText;
        set => SetProperty(ref _totalText, value);
    }

    /// <summary>Read/write speed summary ("21 MB/s ↓ · 4.2 MB/s ↑").</summary>
    public string IoText
    {
        get => _ioText;
        set => SetProperty(ref _ioText, value);
    }
}