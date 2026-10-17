using System.Runtime.CompilerServices;
using SysDash.Core.Diagnostics;

namespace SysDash.App;

/// <summary>
/// Earliest managed entry point. The module initializer runs before Main, so
/// the startup stopwatch is anchored to the process and the host/CLR boot cost
/// is captured before anything else executes.
/// </summary>
internal static class AppStartup
{
    [ModuleInitializer]
    internal static void Init()
    {
        StartupTrace.MarkBoot();
        StartupTrace.Mark("App module loaded (pre-Main)");
    }
}
