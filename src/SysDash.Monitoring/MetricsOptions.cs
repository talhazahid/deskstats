namespace SysDash.Monitoring;

/// <summary>Configuration for the metrics collector.</summary>
public sealed class MetricsOptions
{
    public int PollIntervalMs { get; set; } = 1000;

    public bool IncludeGpu { get; set; } = true;

    public bool IncludeDrives { get; set; } = true;

    public bool IncludeNetwork { get; set; } = true;

    public TimeSpan PollInterval => TimeSpan.FromMilliseconds(Math.Clamp(PollIntervalMs, 200, 60_000));
}