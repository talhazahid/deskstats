using System.Runtime.InteropServices;
using SysDash.Core.Models;

namespace SysDash.Monitoring.IO;

/// <summary>
/// Physical + commit memory straight from Windows (GlobalMemoryStatusEx).
/// Works without admin rights and does not depend on how a given
/// LibreHardwareMonitor version names / indexes its memory sensors (0.9.6
/// changed them, which made the Memory widget show 0%).
/// </summary>
public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>Returns used / total / free physical memory and committed / commit-limit, in bytes.</summary>
    public static RamReading? Read()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            {
                return null;
            }

            double total = status.ullTotalPhys;
            double free = status.ullAvailPhys;
            double used = Math.Max(0, total - free);

            // "Page file" fields are the system commit charge / commit limit.
            double commitLimit = status.ullTotalPageFile;
            double committed = Math.Max(0, commitLimit - status.ullAvailPageFile);

            return new RamReading(
                used,
                total,
                status.dwMemoryLoad,
                free,
                commitLimit > 0 ? committed : null,
                commitLimit > 0 ? commitLimit : null);
        }
        catch
        {
            return null;
        }
    }
}
