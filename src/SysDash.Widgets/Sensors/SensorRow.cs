namespace SysDash.Widgets.Sensors;

/// <summary>One sensor line: name, formatted value and a 0–100 ring value.</summary>
public sealed record SensorRow(string Name, string ValueText, double Percent);
