using System.Windows.Threading;
using SysDash.Core.Threading;

namespace SysDash.App;

/// <summary>Marshals background work onto the WPF UI thread (Dispatcher).</summary>
public sealed class ThreadMarshaller : IThreadMarshaller
{
    private readonly Dispatcher _dispatcher;

    public ThreadMarshaller(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    public void RunOnUiThread(Action action)
    {
        if (action is null)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}