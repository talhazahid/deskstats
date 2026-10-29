using System.Diagnostics;
using System.Runtime.InteropServices;
using SysDash.Core.Units;

namespace SysDash.Widgets.Ram;

internal sealed record MemoryProcess(int Id, int ParentId, string Name, long Bytes);
public sealed record AppMemoryRow(string Name, string ValueText, string Details);

internal static class AppMemorySampler
{
    internal static IReadOnlyList<AppMemoryRow> Group(IReadOnlyList<MemoryProcess> processes, int count)
    {
        var byId = processes.ToDictionary(p => p.Id);
        var groups = new Dictionary<string, List<MemoryProcess>>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in processes)
        {
            if (process.Name.Equals("Idle", StringComparison.OrdinalIgnoreCase)) continue;
            var owner = process;
            var visited = new HashSet<int> { process.Id };
            // Attribute embedded browser helpers only when their live parent chain is known.
            while (owner.Name.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase)
                && byId.TryGetValue(owner.ParentId, out var parent) && visited.Add(parent.Id))
                owner = parent;
            string key = owner.Name;
            if (!groups.TryGetValue(key, out var members)) groups[key] = members = new();
            members.Add(process);
        }
        return groups.OrderByDescending(g => g.Value.Sum(p => p.Bytes)).Take(count)
            .Select(g => new AppMemoryRow(FriendlyName(g.Key), UnitFormat.Bytes(g.Value.Sum(p => p.Bytes)),
                string.Join(" · ", g.Value.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(gp => gp.Key.Equals(g.Key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(gp => gp.Key)
                    .Select(gp => $"{gp.Key}.exe ×{gp.Count()}"))))
            .ToList();
    }

    private static string FriendlyName(string name) => name.ToLowerInvariant() switch
    {
        "searchhost" => "Windows Search",
        "whatsapp.root" => "WhatsApp",
        "msedgewebview2" => "WebView2 (owner unavailable)",
        "svchost" => "Windows services",
        "brave" => "Brave",
        _ => name
    };

    internal static IReadOnlyList<AppMemoryRow> Sample(int count)
    {
        var parents = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot != new IntPtr(-1))
        {
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                if (Process32First(snapshot, ref entry))
                    do { parents[(int)entry.Id] = (int)entry.ParentId; } while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
        }
        var processes = new List<MemoryProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { processes.Add(new(process.Id, parents.GetValueOrDefault(process.Id), process.ProcessName, process.WorkingSet64)); }
                catch { /* Process exited or access denied. */ }
            }
        }
        return Group(processes, count);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr DefaultHeap;
        public uint ModuleId, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

