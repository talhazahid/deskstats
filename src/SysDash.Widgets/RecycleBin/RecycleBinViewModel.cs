using System.Runtime.InteropServices;
using System.Windows.Threading;
using SysDash.Core.Models;
using SysDash.Core.Mvvm;
using SysDash.Core.Threading;
using SysDash.Core.Units;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.RecycleBin;

public sealed class RecycleBinViewModel : WidgetViewModel
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private const uint SherbNoConfirmation = 0x1;
    private const uint SherbNoProgressUi = 0x2;
    private const uint SherbNoSound = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private readonly DispatcherTimer _timer;
    private readonly RelayCommand _emptyCommand;
    private long _itemCount;
    private string _countText = "—";
    private string _sizeText = string.Empty;
    private bool _hasItems;

    public RecycleBinViewModel(IThreadMarshaller marshaller, RecycleBinSettings settings)
        : base(marshaller, settings)
    {
        _timer = new DispatcherTimer { Interval = RefreshInterval() };
        _timer.Tick += (_, _) => Query();

        _emptyCommand = new RelayCommand(Empty, () => _hasItems);
    }

    public RecycleBinSettings Options => (RecycleBinSettings)Settings;

    public override string Title => "Recycle Bin";

    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    public bool HasItems
    {
        get => _hasItems;
        private set
        {
            if (SetProperty(ref _hasItems, value))
            {
                _emptyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand EmptyCommand => _emptyCommand;

    protected override void OnActivated()
    {
        _timer.Interval = RefreshInterval();
        _timer.Start();
        Query();
    }

    protected override void OnDispose()
    {
        _timer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        // SHQueryRecycleBinW is called on its own timer.
    }

    private TimeSpan RefreshInterval()
    {
        int seconds = Math.Clamp(Options.RefreshSeconds, 2, 300);
        return TimeSpan.FromSeconds(seconds);
    }

    private void Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        int hr = SHQueryRecycleBinW(null, ref info);

        if (hr != 0)
        {
            CountText = "—";
            SizeText = string.Empty;
            HasItems = false;
            return;
        }

        _itemCount = info.i64NumItems;
        CountText = $"{info.i64NumItems:N0} item{(info.i64NumItems == 1 ? string.Empty : "s")}";
        SizeText = Options.ShowSize ? UnitFormat.Bytes(info.i64Size) : string.Empty;
        HasItems = info.i64NumItems > 0;
    }

    private void Empty()
    {
        // SHEmptyRecycleBin is called with NO_CONFIRMATION below, so ask here:
        // one stray click must never permanently delete everything.
        if (System.Windows.MessageBox.Show(
                "Permanently delete all items in the Recycle Bin?",
                "DeskStats",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
        if (hr == 0)
        {
            Query();
        }
    }
}