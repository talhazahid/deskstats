using System.Runtime.InteropServices;

namespace SysDash.Theming.Native;

/// <summary>
/// Monitor model names (from EDID, e.g. "DELL P2419H") keyed by GDI device name
/// ("\\.\DISPLAY3"), via the Windows Display Configuration API — the same source
/// Windows Settings → Display uses. Best-effort: returns an empty map on failure.
/// Structures are read from raw buffers at their documented offsets to keep the
/// interop small and layout-independent.
/// </summary>
internal static class DisplayNames
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const int GetSourceName = 1;
    private const int GetTargetName = 2;

    private const int PathInfoSize = 72;   // DISPLAYCONFIG_PATH_INFO
    private const int ModeInfoSize = 64;   // DISPLAYCONFIG_MODE_INFO
    private const int SourceNameSize = 84; // header (20) + WCHAR[32]
    private const int TargetNameSize = 420; // header (20) + 16 + WCHAR[64] + WCHAR[128]
    private const int TargetFriendlyNameOffset = 36;
    private const int TargetDevicePathOffset = 164;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, IntPtr pathArray,
        ref uint numModeInfoArrayElements, IntPtr modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    /// <summary>GDI device name → (EDID model name, unique monitor device path).</summary>
    public static IReadOnlyDictionary<string, (string Name, string Path)> ByGdiDeviceName()
    {
        var result = new Dictionary<string, (string Name, string Path)>(StringComparer.OrdinalIgnoreCase);
        IntPtr paths = IntPtr.Zero;
        IntPtr modes = IntPtr.Zero;

        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0 || pathCount == 0)
            {
                return result;
            }

            paths = Marshal.AllocHGlobal((int)pathCount * PathInfoSize);
            modes = Marshal.AllocHGlobal((int)Math.Max(1, modeCount) * ModeInfoSize);
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return result;
            }

            for (int i = 0; i < pathCount; i++)
            {
                IntPtr path = paths + i * PathInfoSize;

                // sourceInfo: adapterId (LUID) @0, id @8 — targetInfo: adapterId @20, id @28
                uint srcLow = (uint)Marshal.ReadInt32(path, 0);
                int srcHigh = Marshal.ReadInt32(path, 4);
                uint srcId = (uint)Marshal.ReadInt32(path, 8);
                uint tgtLow = (uint)Marshal.ReadInt32(path, 20);
                int tgtHigh = Marshal.ReadInt32(path, 24);
                uint tgtId = (uint)Marshal.ReadInt32(path, 28);

                string? gdi = ReadName(GetSourceName, SourceNameSize, srcLow, srcHigh, srcId, 20, 32);
                string? friendly = ReadName(GetTargetName, TargetNameSize, tgtLow, tgtHigh, tgtId, TargetFriendlyNameOffset, 64);
                string? devicePath = ReadName(GetTargetName, TargetNameSize, tgtLow, tgtHigh, tgtId, TargetDevicePathOffset, 128);

                if (!string.IsNullOrWhiteSpace(gdi) && !result.ContainsKey(gdi))
                {
                    result[gdi] = ((friendly ?? string.Empty).Trim(), (devicePath ?? string.Empty).Trim());
                }
            }
        }
        catch
        {
            // Older Windows / remote sessions: names are optional.
        }
        finally
        {
            if (paths != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(paths);
            }

            if (modes != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(modes);
            }
        }

        return result;
    }

    private static string? ReadName(int type, int size, uint adapterLow, int adapterHigh, uint id, int nameOffset, int maxChars)
    {
        IntPtr packet = Marshal.AllocHGlobal(size);
        try
        {
            for (int b = 0; b < size; b++)
            {
                Marshal.WriteByte(packet, b, 0);
            }

            // DISPLAYCONFIG_DEVICE_INFO_HEADER: type, size, adapterId (LUID), id
            Marshal.WriteInt32(packet, 0, type);
            Marshal.WriteInt32(packet, 4, size);
            Marshal.WriteInt32(packet, 8, unchecked((int)adapterLow));
            Marshal.WriteInt32(packet, 12, adapterHigh);
            Marshal.WriteInt32(packet, 16, unchecked((int)id));

            if (DisplayConfigGetDeviceInfo(packet) != 0)
            {
                return null;
            }

            string? text = Marshal.PtrToStringUni(packet + nameOffset, maxChars);
            int nul = text?.IndexOf('\0') ?? -1;
            return nul >= 0 ? text![..nul] : text;
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }
}
