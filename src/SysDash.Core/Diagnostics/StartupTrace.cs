using System.Diagnostics;

namespace SysDash.Core.Diagnostics;

/// <summary>
/// Startup timing marks shared by the whole process. Marks are written to the
/// normal log once <see cref="Log"/> is configured; anything recorded before
/// that (module load) is buffered and flushed, so no boot information is lost.
/// Logging only — this type must never influence startup behaviour.
/// </summary>
public static class StartupTrace
{
    private static readonly object Sync = new();
    private static readonly Stopwatch Watch = new();
    private static readonly List<string> Pending = new();
    private static long _lastMs;

    static StartupTrace()
    {
        Watch.Start();
    }

    /// <summary>Elapsed milliseconds since the first mark (module load).</summary>
    public static long ElapsedMs
    {
        get
        {
            lock (Sync)
            {
                return Watch.ElapsedMilliseconds;
            }
        }
    }

    /// <summary>Records a step with total + delta time since the previous mark.</summary>
    public static void Mark(string step)
    {
        long now;
        long delta;
        lock (Sync)
        {
            now = Watch.ElapsedMilliseconds;
            delta = now - _lastMs;
            _lastMs = now;
        }

        Write($"[startup] {DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} +{now,6} ms (+{delta,5} ms)  {step}");
    }

    /// <summary>How long the host/CLR took before managed code could run.</summary>
    public static void MarkBoot()
    {
        try
        {
            DateTime started = Process.GetCurrentProcess().StartTime;
            long ms = (long)(DateTime.Now - started).TotalMilliseconds;
            Write($"[startup] {DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} process→managed boot: ~{ms} ms (process created {started:HH:mm:ss.fff})");
        }
        catch
        {
            // Process.StartTime can throw in restricted environments; tracing is optional.
        }
    }

    /// <summary>Writes buffered marks after <see cref="Log.Configure"/> has run.</summary>
    public static void FlushPending()
    {
        string[] pending;
        lock (Sync)
        {
            if (Pending.Count == 0)
            {
                return;
            }

            pending = Pending.ToArray();
            Pending.Clear();
        }

        foreach (string line in pending)
        {
            Log.Info(line);
        }
    }

    private static void Write(string line)
    {
        lock (Sync)
        {
            if (!Log.IsConfigured)
            {
                Pending.Add(line);
                return;
            }
        }

        Log.Info(line);
    }
}
