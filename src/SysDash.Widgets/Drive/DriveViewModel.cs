using SysDash.Core.History;
using SysDash.Core.Models;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.Widgets.Common;

namespace SysDash.Widgets.Drive;

/// <summary>
/// Detailed disks widget: one ring per drive (used %) with free space, plus
/// combined read / write rates and a mirrored I/O history graph.
/// </summary>
public sealed class DriveViewModel : HardwareWidgetViewModel
{
    private HistoryBuffer _read;
    private HistoryBuffer _write;
    private IReadOnlyList<DriveItem> _drives = Array.Empty<DriveItem>();
    private double[] _readHistory = Array.Empty<double>();
    private double[] _writeHistory = Array.Empty<double>();
    private string _readText = "—";
    private string _writeText = "—";
    private string _peakReadText = "—";
    private string _peakWriteText = "—";
    private bool _hasData;

    public DriveViewModel(IThreadMarshaller marshaller, DriveSettings settings)
        : base(marshaller, settings, settings.HistoryCapacity)
    {
        int capacity = Math.Clamp(settings.HistoryCapacity, 30, 900);
        _read = new HistoryBuffer(capacity);
        _write = new HistoryBuffer(capacity);
    }

    public DriveSettings Options => (DriveSettings)Settings;

    public override string Title => "Disks";

    public IReadOnlyList<DriveItem> Drives { get => _drives; private set => SetProperty(ref _drives, value); }

    public double[] ReadHistory { get => _readHistory; private set => SetProperty(ref _readHistory, value); }

    public double[] WriteHistory { get => _writeHistory; private set => SetProperty(ref _writeHistory, value); }

    public string ReadText { get => _readText; private set => SetProperty(ref _readText, value); }

    public string WriteText { get => _writeText; private set => SetProperty(ref _writeText, value); }

    public string PeakReadText { get => _peakReadText; private set => SetProperty(ref _peakReadText, value); }

    public string PeakWriteText { get => _peakWriteText; private set => SetProperty(ref _peakWriteText, value); }

    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }

    public bool ShowDrives => Options.ShowDrives;

    public bool ShowActivity => Options.ShowActivity;

    protected override void OnSettingsChanged()
    {
        ResizeHistory(Options.HistoryCapacity);
        _read = Resize(_read, Options.HistoryCapacity);
        _write = Resize(_write, Options.HistoryCapacity);
        ReadHistory = _read.ToArray();
        WriteHistory = _write.ToArray();
        Drives = Array.Empty<DriveItem>(); // rebuild rows for a new drive selection
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        IReadOnlyList<DriveReading>? drives = snapshot.Drives;
        if (drives is null || drives.Count == 0)
        {
            Drives = Array.Empty<DriveItem>();
            HasData = false;
            return;
        }

        // Empty PreferredDrive = all drives; otherwise just that one (if connected).
        IEnumerable<DriveReading> shown = drives;
        if (!string.IsNullOrWhiteSpace(Options.PreferredDrive))
        {
            DriveReading? preferred = drives.FirstOrDefault(d =>
                d.Name.Equals(Options.PreferredDrive, StringComparison.OrdinalIgnoreCase) ||
                d.Name.TrimEnd('\\').Equals(Options.PreferredDrive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                shown = new[] { preferred };
            }
        }

        // Re-use row objects when the drive set is unchanged so the rings animate
        // in place instead of the whole list being rebuilt every second.
        List<DriveReading> list = shown.ToList();
        bool sameSet = _drives.Count == list.Count
            && _drives.Select(d => d.Name).SequenceEqual(list.Select(d => d.Name.TrimEnd('\\')));

        List<DriveItem> rows = sameSet ? _drives.ToList() : list.Select(_ => new DriveItem()).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            DriveReading d = list[i];
            DriveItem item = rows[i];
            item.Name = d.Name.TrimEnd('\\');
            item.UsedPercent = Math.Clamp(d.UsedPercent, 0, 100);
            item.UsedText = $"{d.UsedPercent:0}%";
            item.FreeText = $"{UnitFormat.Bytes(d.FreeBytes)} available";
            item.TotalText = UnitFormat.Bytes(d.TotalBytes);
            item.IsLow = d.TotalBytes > 0 && d.FreeBytes / d.TotalBytes * 100 < Options.LowSpacePercent;
        }

        if (!sameSet)
        {
            Drives = rows;
        }

        double read = list.Sum(d => d.ReadBytesPerSec);
        double write = list.Sum(d => d.WriteBytesPerSec);
        ReadText = UnitFormat.BytesPerSecond(read);
        WriteText = UnitFormat.BytesPerSecond(write);

        _read.Add(read);
        _write.Add(write);
        ReadHistory = _read.ToArray();
        WriteHistory = _write.ToArray();
        PeakReadText = UnitFormat.BytesPerSecond(_read.Max());
        PeakWriteText = UnitFormat.BytesPerSecond(_write.Max());

        PushHistory(list.Max(d => d.UsedPercent));
        HasData = true;
    }
}
