using System.Net.NetworkInformation;
using System.Net.Sockets;
using SysDash.Core.Models;

namespace SysDash.Monitoring.IO;

/// <summary>
/// Network adapter sampling using interface counters (deltas). Unlike the original's
/// NetworkInterface performance counters, this needs no admin rights and no counter
/// category. Adapted conceptually from SidebarDiagnostics' NetworkMonitor — GPLv3.
/// </summary>
public sealed class NetworkSampler
{
    private readonly Dictionary<string, (long Rx, long Tx, DateTime At)> _previous = new();

    public List<NetworkAdapterReading> ReadAll()
    {
        var now = DateTime.UtcNow;
        var result = new List<NetworkAdapterReading>();
        var liveNames = new HashSet<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (IsIgnorable(ni))
                {
                    continue;
                }

                liveNames.Add(ni.Name);

                long rx;
                long tx;

                bool statsOk = true;
                try
                {
                    var stats = ni.GetIPv4Statistics();
                    rx = stats.BytesReceived;
                    tx = stats.BytesSent;
                }
                catch
                {
                    // No counters this tick: report 0 and keep the previous baseline,
                    // so the next good read does not produce a huge fake spike.
                    rx = 0;
                    tx = 0;
                    statsOk = false;
                }

                double rxRate = 0;
                double txRate = 0;

                if (statsOk && _previous.TryGetValue(ni.Name, out var prev) && now > prev.At)
                {
                    double seconds = (now - prev.At).TotalSeconds;
                    if (seconds > 0)
                    {
                        rxRate = Math.Max(0, (rx - prev.Rx) / seconds);
                        txRate = Math.Max(0, (tx - prev.Tx) / seconds);
                    }
                }

                if (statsOk)
                {
                    _previous[ni.Name] = (rx, tx, now);
                }

                string? ipv4 = null;
                try
                {
                    ipv4 = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        ?.Address.ToString();
                }
                catch
                {
                    // keep null
                }

                result.Add(new NetworkAdapterReading(
                    ni.Name,
                    rxRate,
                    txRate,
                    ipv4,
                    ni.OperationalStatus == OperationalStatus.Up));
            }
        }
        catch
        {
            // Degraded run: widgets show whatever adapters we already read.
        }

        // Drop entries for adapters that disappeared to avoid unbounded growth.
        if (_previous.Count > liveNames.Count)
        {
            foreach (string stale in _previous.Keys.Where(k => !liveNames.Contains(k)).ToArray())
            {
                _previous.Remove(stale);
            }
        }

        return result;
    }

    private static bool IsIgnorable(NetworkInterface ni)
        => ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
           || ni.Name.StartsWith("isatap", StringComparison.OrdinalIgnoreCase);
}