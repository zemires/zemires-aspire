using Aspire.Hosting.ApplicationModel;
using Zitadel.Api;

namespace Aspire.Hosting;

/// <summary>
/// An initialization callback that runs when a Zitadel resource or project becomes ready.
/// </summary>
public class ZitadelInitializationAnnotation : IResourceAnnotation
{
    /// <summary>
    ///     Constructs a new instance of the <see cref="ZitadelInitializationAnnotation" /> class.
    /// </summary>
    /// <param name="initialization">The initialization callback.</param>
    public ZitadelInitializationAnnotation(Func<ZitadelInitializationContext, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        Initialization = initialization;
    }

    /// <summary>
    ///     Constructs a new instance of the <see cref="ZitadelInitializationAnnotation" /> class.
    /// </summary>
    /// <param name="initialization">The initialization callback receiving the API client options and the resource.</param>
    public ZitadelInitializationAnnotation(Func<Clients.Options, IResource, Task> initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        Initialization = context => initialization(context.Options, context.Resource);
    }

    /// <summary>
    /// Gets the initialization callback.
    /// </summary>
    public Func<ZitadelInitializationContext, Task> Initialization { get; }
}
