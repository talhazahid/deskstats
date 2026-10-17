namespace SysDash.Widgets.Clock;

/// <summary>One cell of the month calendar.</summary>
public sealed record CalendarDay(int Day, bool IsCurrentMonth, bool IsToday, bool IsWeekend);
