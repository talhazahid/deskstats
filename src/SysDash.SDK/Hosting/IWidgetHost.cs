namespace SysDash.SDK.Hosting;

/// <summary>
/// The container that renders one widget instance: either the sidebar shell
/// (IsDetached == false) or a floating window (IsDetached == true).
/// </summary>
public interface IWidgetHost : IDisposable
{
    Guid InstanceId { get; }

    /// <summary>Stable host id: "sidebar" or "window:&lt;guid&gt;".</summary>
    string HostName { get; }

    bool IsDetached { get; }

    bool HostVisible { get; set; }

    void Show();

    void Hide();

    /// <summary>Raised when the host closes (user closed a widget window).</summary>
    event EventHandler? Closed;
}