using InertiaNet.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods that make ASP.NET Core authentication challenges and forbids Inertia-aware.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Makes every authentication scheme's challenge and forbid responses Inertia-aware.
    /// A sign-in redirect for an Inertia request becomes <c>409 Conflict</c> with
    /// <c>X-Inertia-Location</c>, so the client performs a full-page visit (required for
    /// cross-origin identity providers). Forbid redirects for Inertia requests become a bare
    /// <c>403</c>. An Inertia prefetch is answered with a location visit to the prefetched URL
    /// without starting a sign-in. Requests selected by
    /// <see cref="InertiaChallengeOptions.ApiRequestPredicate"/> receive <c>401</c>/<c>403</c>
    /// instead of redirects. Other requests are unchanged.
    /// </summary>
    /// <remarks>
    /// The registered <see cref="IAuthenticationService"/> is decorated, so this works for cookie,
    /// OpenID Connect, OAuth and other remote schemes without referencing their packages, for any
    /// middleware order, and may be called before or after <c>AddAuthentication()</c>.
    /// Repeated calls only apply additional option configuration.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="InertiaChallengeOptions"/>.</param>
    public static IServiceCollection AddInertiaChallengeHandling(
        this IServiceCollection services,
        Action<InertiaChallengeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<InertiaChallengeOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(InertiaChallengeHandlingMarker)))
            return services;

        services.AddSingleton<InertiaChallengeHandlingMarker>();

        // Ensure a registration exists so the decorator can wrap it; AddAuthentication() uses
        // TryAdd and therefore keeps the decorated registration when it is called afterwards.
        services.AddAuthenticationCore();

        var inner = services.Last(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType == typeof(IAuthenticationService));
        services.Remove(inner);
        services.Add(ServiceDescriptor.Describe(
            typeof(IAuthenticationService),
            provider => new InertiaAuthenticationService(
                CreateInner(provider, inner),
                provider.GetRequiredService<IOptions<InertiaChallengeOptions>>()),
            inner.Lifetime));

        return services;
    }

    /// <summary>
    /// Makes every authentication scheme's challenge and forbid responses Inertia-aware.
    /// See <see cref="AddInertiaChallengeHandling(IServiceCollection, Action{InertiaChallengeOptions}?)"/>.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configure">Optional action to configure <see cref="InertiaChallengeOptions"/>.</param>
    public static AuthenticationBuilder AddInertiaChallengeHandling(
        this AuthenticationBuilder builder,
        Action<InertiaChallengeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddInertiaChallengeHandling(configure);
        return builder;
    }

    private static IAuthenticationService CreateInner(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IAuthenticationService instance) return instance;
        if (descriptor.ImplementationFactory is not null)
            return (IAuthenticationService)descriptor.ImplementationFactory(provider);
        return (IAuthenticationService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }

    private sealed class InertiaChallengeHandlingMarker;
}
