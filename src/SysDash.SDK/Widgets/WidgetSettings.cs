namespace SysDash.SDK.Widgets;

/// <summary>Base class for all per-widget settings. Instances are plain JSON-serializable POCOs.</summary>
public abstract class WidgetSettings
{
    [Setting("Fluid placement", Group = "Placement", Order = 900,
        Description = "Move and resize freely. Ignore spacing, snapping and screen edges; overlapping other widgets is allowed.")]
    public bool FluidPlacement { get; set; }
}
