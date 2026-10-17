using System.Globalization;
using System.Windows.Threading;
using SysDash.Core.Models;
using SysDash.Core.Mvvm;
using SysDash.Core.Threading;
using SysDash.SDK.Widgets;

namespace SysDash.Widgets.Clock;

public sealed class ClockViewModel : WidgetViewModel
{
    private readonly DispatcherTimer _timer;
    private string _timeText = "—";
    private string _secondsText = string.Empty;
    private string _dateText = string.Empty;
    private DateTime _shownMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastBuiltFor = DateTime.MinValue;
    private string _monthTitle = string.Empty;
    private IReadOnlyList<CalendarDay> _days = Array.Empty<CalendarDay>();
    private IReadOnlyList<string> _weekdayNames = Array.Empty<string>();

    public ClockViewModel(IThreadMarshaller marshaller, ClockSettings settings)
        : base(marshaller, settings)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();

        PreviousMonthCommand = new RelayCommand(() => ShowMonth(_shownMonth.AddMonths(-1)));
        NextMonthCommand = new RelayCommand(() => ShowMonth(_shownMonth.AddMonths(1)));
        TodayCommand = new RelayCommand(() => ShowMonth(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)));
        BuildCalendar();
    }

    public RelayCommand PreviousMonthCommand { get; }

    public RelayCommand NextMonthCommand { get; }

    public RelayCommand TodayCommand { get; }

    public bool ShowCalendar => Options.ShowCalendar;

    public string MonthTitle
    {
        get => _monthTitle;
        private set => SetProperty(ref _monthTitle, value);
    }

    public IReadOnlyList<CalendarDay> Days
    {
        get => _days;
        private set => SetProperty(ref _days, value);
    }

    public IReadOnlyList<string> WeekdayNames
    {
        get => _weekdayNames;
        private set => SetProperty(ref _weekdayNames, value);
    }

    private void ShowMonth(DateTime firstOfMonth)
    {
        _shownMonth = firstOfMonth;
        BuildCalendar();
    }

    /// <summary>6 x 7 grid for the shown month, padded with the neighbouring months' days.</summary>
    private void BuildCalendar()
    {
        DateTime today = DateTime.Today;
        _lastBuiltFor = today;

        DayOfWeek firstDay = Options.WeekStartsMonday ? DayOfWeek.Monday : DayOfWeek.Sunday;
        CultureInfo culture = CultureInfo.CurrentCulture;

        var names = new List<string>(7);
        for (int i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)firstDay + i) % 7);
            names.Add(culture.DateTimeFormat.GetAbbreviatedDayName(dow).ToUpperInvariant());
        }

        WeekdayNames = names;
        MonthTitle = _shownMonth.ToString("MMMM yyyy", culture).ToUpperInvariant();

        int offset = ((int)_shownMonth.DayOfWeek - (int)firstDay + 7) % 7;
        DateTime start = _shownMonth.AddDays(-offset);

        var days = new List<CalendarDay>(42);
        for (int i = 0; i < 42; i++)
        {
            DateTime d = start.AddDays(i);
            days.Add(new CalendarDay(
                d.Day,
                d.Month == _shownMonth.Month && d.Year == _shownMonth.Year,
                d == today,
                d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        }

        Days = days;
    }

    public ClockSettings Options => (ClockSettings)Settings;

    public override string Title => "Clock";

    public string TimeText
    {
        get => _timeText;
        private set => SetProperty(ref _timeText, value);
    }

    /// <summary>Seconds shown in a separate column when enabled.</summary>
    public string SecondsText
    {
        get => _secondsText;
        private set => SetProperty(ref _secondsText, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public bool ShowSeconds => Options.ShowSeconds;

    public bool ShowDate => Options.ShowDate;

    protected override void OnActivated()
    {
        Tick();
        _timer.Start();
    }

    protected override void OnDispose()
    {
        _timer.Stop();
    }

    protected override void Update(MetricsSnapshot snapshot)
    {
        // Time is timer-driven; no snapshot data needed.
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;

        if (!string.IsNullOrWhiteSpace(Options.FormatOverride))
        {
            try
            {
                TimeText = now.ToString(Options.FormatOverride);
            }
            catch (FormatException)
            {
                TimeText = now.ToString("t"); // invalid custom format: fall back instead of crashing
            }
            SecondsText = string.Empty;
        }
        else
        {
            string main = Options.Use24Hour
                ? (Options.ShowSeconds ? "HH\\:mm\\:ss" : "HH\\:mm")
                : (Options.ShowSeconds ? "h\\:mm\\:ss" : "h\\:mm tt");
            TimeText = now.ToString(main);

            if (Options.ShowSeconds)
            {
                // Split trailing seconds into their own column for a large digital look.
                string[] parts = TimeText.Split(':');
                if (parts.Length == 3 && !Options.Use24Hour)
                {
                    SecondsText = parts[2];
                    TimeText = parts[0] + ":" + parts[1] + " " + now.ToString("tt");
                }
                else if (parts.Length == 3)
                {
                    SecondsText = parts[2];
                    TimeText = parts[0] + ":" + parts[1];
                }
                else
                {
                    SecondsText = string.Empty;
                }
            }
            else
            {
                SecondsText = string.Empty;
            }
        }

        DateText = Options.ShowDate ? now.ToString("dddd, d MMMM yyyy") : string.Empty;
        OnPropertiesChanged(nameof(ShowSeconds), nameof(ShowDate), nameof(ShowCalendar));

        // Midnight rollover: move the "today" highlight.
        if (now.Date != _lastBuiltFor)
        {
            BuildCalendar();
        }
    }
}