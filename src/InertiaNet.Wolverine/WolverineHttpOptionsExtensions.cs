using InertiaNet.Core;
using Wolverine.Http;

namespace InertiaNet.Wolverine;

/// <summary>
/// Extension methods for serving Inertia pages from Wolverine.HTTP endpoints.
/// </summary>
public static class WolverineHttpOptionsExtensions
{
    /// <summary>
    /// Configures Wolverine.HTTP endpoints to work with InertiaNet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <see cref="IInertiaService"/> parameter on a Wolverine.HTTP endpoint is taken from
    /// <c>HttpContext.RequestServices</c>. Without this, Wolverine creates the service in its own
    /// scope, so props and request settings applied by <c>UseInertia()</c> middleware (for example
    /// props shared in <c>InertiaMiddleware.Share</c>) are missing from the page.
    /// </para>
    /// <para>
    /// Call it when mapping the endpoints:
    /// <c>app.MapWolverineEndpoints(opts =&gt; opts.UseInertia());</c>
    /// </para>
    /// </remarks>
    /// <param name="options">The Wolverine.HTTP options.</param>
    /// <returns>The same options, for chaining.</returns>
    public static WolverineHttpOptions UseInertia(this WolverineHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.SourceServiceFromHttpContext<IInertiaService>();
        return options;
    }
}
