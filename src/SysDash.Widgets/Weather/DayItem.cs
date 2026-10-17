namespace SysDash.Widgets.Weather;

/// <summary>One day of the multi-day forecast.</summary>
public sealed record DayItem(string Day, string Icon, string High, string Low);
