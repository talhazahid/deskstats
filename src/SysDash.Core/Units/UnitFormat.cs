using System.Globalization;

namespace SysDash.Core.Units;

/// <summary>Friendly formatting helpers for hardware values.</summary>
public static class UnitFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Bytes(double bytes, int decimals = 1)
    {
        if (bytes < 1024)
        {
            return $"{bytes:0} B";
        }

        double kb = bytes / 1024;
        if (kb < 1024)
        {
            return $"{kb.ToString("0.#", Inv)} KB";
        }

        double mb = kb / 1024;
        if (mb < 1024)
        {
            return $"{mb.ToString("0.#", Inv)} MB";
        }

        double gb = mb / 1024;
        if (gb < 1024)
        {
            return $"{gb.ToString("0.##", Inv)} GB";
        }

        return $"{((double)gb / 1024).ToString("0.##", Inv)} TB";
    }

    public static string BytesPerSecond(double bytesPerSec, int decimals = 1)
        => $"{Bytes(bytesPerSec, decimals)}/s";

    /// <summary>Network-style rate in bits (decimal units, like ISP speeds): "94.3 Mbps".</summary>
    public static string BitsPerSecond(double bytesPerSec)
    {
        double bits = Math.Max(0, bytesPerSec) * 8;
        if (bits < 1000)
        {
            return $"{bits.ToString("0", Inv)} bps";
        }

        double kb = bits / 1000;
        if (kb < 1000)
        {
            return $"{kb.ToString("0.#", Inv)} kbps";
        }

        double mb = kb / 1000;
        if (mb < 1000)
        {
            return $"{mb.ToString("0.#", Inv)} Mbps";
        }

        return $"{(mb / 1000).ToString("0.##", Inv)} Gbps";
    }

    public static string Percent(double value) => $"{value.ToString("0", Inv)}%";

    public static string MegaHertz(double mhz) => $"{mhz.ToString("0", Inv)} MHz";

    public static string GigaHertz(double mhz) => $"{((double)mhz / 1000).ToString("0.00", Inv)} GHz";

    public static string Celcius(double c) => $"{c.ToString("0", Inv)}°C";

    public static string Fahrenheit(double c) => $"{((double)c * 1.8 + 32).ToString("0", Inv)}°F";

    public static string Temperature(double celcius, bool fahrenheit)
        => fahrenheit ? Fahrenheit(celcius) : Celcius(celcius);

    public static string Voltage(double v) => $"{v.ToString("0.00", Inv)} V";

    public static string Rpm(double rpm) => $"{rpm.ToString("0", Inv)} RPM";

    public static string Clock(double mhz, bool gigahertz)
        => gigahertz ? GigaHertz(mhz) : MegaHertz(mhz);

    public static string Gigabytes(double bytes) => Bytes(bytes, 2);
}