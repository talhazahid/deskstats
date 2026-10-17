namespace SysDash.Core.Threading;

/// <summary>
/// Contract for code that needs to hop onto the UI thread.
/// Implemented by the App shell (WPF Dispatcher); used by widget view models.
/// </summary>
public interface IThreadMarshaller
{
    bool IsOnUiThread { get; }

    void RunOnUiThread(Action action);
}