using System.IO;
using SysDash.Layouts;

namespace SysDash.App;

public static class InstallationState
{
    public static bool Apply(string instance, AppSettings settings, ILayoutService layouts)
    {
        if (string.IsNullOrWhiteSpace(instance) || instance == settings.InstallationInstance) return false;
        // Archive rather than delete the previous customized layout on a fresh install.
        layouts.SaveAs(layouts.ActiveLayout, "Before reinstall " + Guid.NewGuid().ToString("N")[..8]);
        layouts.SaveActive(LayoutPresets.Build(LayoutPresets.Default));
        settings.InstallationInstance = instance;
        settings.DisplayMode = "widgets";
        return true;
    }
}
