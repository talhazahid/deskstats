using System.Globalization;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using SysDash.Core.Models;

namespace SysDash.Monitoring.Hardware;

/// <summary>Read-only AMD ADL PMLog queries. Board and ASIC power are different scopes.
/// ABI and sensor IDs: GPUOpen display-library include/adl_structures.h and adl_defines.h.
/// Never substitutes ASIC power for unsupported board power.</summary>
public sealed class AmdPowerTelemetry : IDisposable
{
    private readonly object _gate = new();
    private readonly Allocate _allocate = Marshal.AllocHGlobal;
    private IntPtr _context;
    private bool _attempted;
    private bool _disposed;

    public IReadOnlyList<SensorReading> Read(IHardware gpu)
    {
        lock (_gate)
        {
            if (_disposed || gpu.HardwareType != HardwareType.GpuAmd) return Array.Empty<SensorReading>();
            try
            {
                if (!_attempted)
                {
                    _attempted = true;
                    if (Create(_allocate, 1, ref _context) != 0) return Array.Empty<SensorReading>();
                }
                if (_context == IntPtr.Zero || !int.TryParse(gpu.Identifier.ToString().Split('/').Last(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    return Array.Empty<SensorReading>();
                var data = new LogOutput { Size = Marshal.SizeOf<LogOutput>(), Sensors = new SensorData[256] };
                if (Query(_context, index, ref data) != 0) return Array.Empty<SensorReading>();
                return Decode(gpu.Name, data.Sensors[23].Supported != 0, data.Sensors[23].Value,
                    data.Sensors[73].Supported != 0, data.Sensors[73].Value);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return Array.Empty<SensorReading>();
            }
        }
    }

    public static IReadOnlyList<SensorReading> Decode(string name, bool asicSupported, double asicWatts, bool boardSupported, double boardWatts)
    {
        var rows = new List<SensorReading>();
        string group = "GPU · " + name;
        if (boardSupported && double.IsFinite(boardWatts) && boardWatts >= 0)
            rows.Add(new(group, "Total board power", "power", boardWatts));
        if (asicSupported && double.IsFinite(asicWatts) && asicWatts >= 0)
            rows.Add(new(group, "ASIC / chip power", "power", asicWatts));
        return rows;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_context == IntPtr.Zero) return;
            try { Destroy(_context); }
            finally { _context = IntPtr.Zero; }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Allocate(int size);
    [StructLayout(LayoutKind.Sequential)]
    private struct SensorData { public int Supported; public int Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LogOutput
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public SensorData[] Sensors;
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Create", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Create(Allocate allocate, int connectedAdapters, ref IntPtr context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_Main_Control_Destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Destroy(IntPtr context);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("atiadlxx.dll", EntryPoint = "ADL2_New_QueryPMLogData_Get", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Query(IntPtr context, int index, ref LogOutput data);
}
