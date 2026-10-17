using SysDash.Core.Threading;

namespace SysDash.Widgets.Common;

public static class ServiceExtensions
{
    public static IThreadMarshaller GetMarshaller(this IServiceProvider services)
        => services.GetService(typeof(IThreadMarshaller)) as IThreadMarshaller
           ?? throw new InvalidOperationException("IThreadMarshaller is not registered in the container.");
}