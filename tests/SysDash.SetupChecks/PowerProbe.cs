using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using SysDash.Monitoring.Hardware;

internal static class PowerProbe
{
    public static void Run(string output)
    {
        var computer = new Computer { IsGpuEnabled = true };
        using var native = new AmdPowerTelemetry();
        computer.Open();
        try
        {
        var samples = new List<object>();
        for (int i = 0; i < 12; i++)
        {
            foreach (var gpu in computer.Hardware)
            {
                gpu.Update();
                samples.Add(new { Timestamp = DateTimeOffset.Now, Device = gpu.Name,
                    Raw = gpu.Sensors.Where(s => s.SensorType is SensorType.Power or SensorType.Load or SensorType.Clock).Select(s => new { s.Name, Type = s.SensorType.ToString(), Value = SensorCatalog.Value(s) }).ToArray(),
                    DriverPower = native.Read(gpu), Selected = SensorCatalog.ReadSensors(new[] { gpu }, native).Where(s => s.Kind == "power").ToArray() });
            }
            Thread.Sleep(1000);
        }
        File.WriteAllText(Path.Combine(output, "live-power-probe.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { computer.Close(); }
    }
}
