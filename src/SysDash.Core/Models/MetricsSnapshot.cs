namespace SysDash.Core.Models;

/// <summary>CPU readings produced by the monitoring layer. All values optional — null means "not available".</summary>
public sealed record CpuReading(
    string Name,
    double? Load,
    double? ClockMhz,
    double? TemperatureC,
    double? Voltage,
    double? FanRpm,
    IReadOnlyList<double>? PerCoreLoad,
    IReadOnlyList<double>? PerCoreClockMhz,
    double? PackagePowerW = null,
    bool ApproximateTemperature = false);

/// <summary>RAM readings.</summary>
public sealed record RamReading(
    double? UsedBytes,
    double? TotalBytes,
    double? Load,
    double? FreeBytes,
    double? VirtualUsedBytes = null,
    double? VirtualTotalBytes = null)
{
    public double? VirtualPercent =>
        VirtualTotalBytes is > 0 ? (VirtualUsedBytes ?? 0) / VirtualTotalBytes.Value * 100 : null;

    public double? UsedPercent =>
        TotalBytes is > 0 ? (UsedBytes ?? 0) / TotalBytes.Value * 100 : null;
}

/// <summary>GPU readings. One per GPU hardware node.</summary>
public sealed record GpuReading(
    string Name,
    double? CoreLoad,
    double? VramLoad,
    double? CoreClockMhz,
    double? VramClockMhz,
    double? TemperatureC,
    double? Voltage,
    double? FanPercent,
    double? VramUsedBytes,
    double? VramTotalBytes)
{
    public double? VramPercent =>
        VramTotalBytes is > 0 ? (VramUsedBytes ?? 0) / VramTotalBytes.Value * 100 : VramLoad;
}

/// <summary>Logical drive readings.</summary>
public sealed record DriveReading(
    string Name,
    double TotalBytes,
    double UsedBytes,
    double FreeBytes,
    double ReadBytesPerSec,
    double WriteBytesPerSec)
{
    public double UsedPercent => TotalBytes > 0 ? UsedBytes / TotalBytes * 100 : 0;
}

/// <summary>Network adapter readings (bytes/sec calculated from interface counters).</summary>
public sealed record NetworkAdapterReading(
    string Name,
    double DownBytesPerSec,
    double UpBytesPerSec,
    string? IpV4,
    bool IsUp);

/// <summary>
/// One raw sensor (temperature / fan / power) for the Sensors widget.
/// <paramref name="Kind"/> is "temperature" (°C), "fan" (RPM) or "power" (W).
/// </summary>
public sealed record SensorReading(string Group, string Name, string Kind, double Value);

/// <summary>
/// Immutable snapshot of every monitored component at one moment in time.
/// Published by IMetricsService, marshalled to the UI thread, consumed by widgets.
/// </summary>
public sealed record MetricsSnapshot(
    DateTimeOffset Timestamp,
    CpuReading? Cpu = null,
    RamReading? Ram = null,
    IReadOnlyList<GpuReading>? Gpus = null,
    IReadOnlyList<DriveReading>? Drives = null,
    IReadOnlyList<NetworkAdapterReading>? Adapters = null,
    IReadOnlyList<SensorReading>? Sensors = null,
    string? SensorHint = null)
{
    public static MetricsSnapshot Empty { get; } = new(
        DateTimeOffset.Now,
        null,
        null,
        Array.Empty<GpuReading>(),
        Array.Empty<DriveReading>(),
        Array.Empty<NetworkAdapterReading>());
}