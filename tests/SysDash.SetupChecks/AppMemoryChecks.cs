using SysDash.Widgets.Ram;

internal static class AppMemoryChecks
{
    internal static bool Run()
    {
        MemoryProcess[] source = [new(1, 0, "WhatsApp.Root", 100), new(2, 1, "msedgewebview2", 200), new(3, 2, "msedgewebview2", 300), new(4, 0, "SearchHost", 50), new(5, 4, "msedgewebview2", 70), new(6, 99, "msedgewebview2", 20)];
        var rows = AppMemorySampler.Group(source, 10);
        bool passed = rows.Count == 3 && rows[0].Name == "WhatsApp" && rows[0].Details.Contains("msedgewebview2.exe ×2")
            && rows[0].ValueText == SysDash.Core.Units.UnitFormat.Bytes(600)
            && rows[1].Name == "Windows Search" && rows[2].Name.Contains("owner unavailable");
        var live = AppMemorySampler.Sample(5);
        passed &= live.Count > 0 && live.All(r => !string.IsNullOrWhiteSpace(r.Details));
        foreach (var row in live) Console.WriteLine($"{row.Name}: {row.ValueText} | {row.Details}");
        Console.WriteLine($"App ownership, nested helpers, unknown parents and live sampling: {passed}");
        return passed;
    }
}
