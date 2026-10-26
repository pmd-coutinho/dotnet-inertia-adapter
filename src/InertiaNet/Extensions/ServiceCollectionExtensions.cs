using InertiaNet.Core;
using InertiaNet.Middleware;
using InertiaNet.Ssr;
using InertiaNet.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering InertiaNet services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all InertiaNet services with the default <see cref="InertiaMiddleware"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="InertiaOptions"/>.</param>
    public static IServiceCollection AddInertia(
        this IServiceCollection services,
        Action<InertiaOptions>? configure = null)
        => services.AddInertia<InertiaMiddleware>(configure);

    /// <summary>
    /// Registers all InertiaNet services with a custom <see cref="InertiaMiddleware"/> subclass.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// A subclass of <see cref="InertiaMiddleware"/> that provides shared props, version logic, etc.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="InertiaOptions"/>.</param>
    public static IServiceCollection AddInertia<TMiddleware>(
        this IServiceCollection services,
        Action<InertiaOptions>? configure = null)
        where TMiddleware : InertiaMiddleware
    {
        // Options
        var optionsBuilder = services.AddOptions<InertiaOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);
        optionsBuilder.Validate(options => options.Ssr.Timeout == Timeout.InfiniteTimeSpan
            || (options.Ssr.Timeout > TimeSpan.Zero && options.Ssr.Timeout.TotalMilliseconds <= uint.MaxValue - 1),
            "Ssr.Timeout must be positive and no greater than 4294967294 milliseconds, or Timeout.InfiniteTimeSpan.");
        optionsBuilder.Validate(options => options.PropDiagnostics.MaxEntries is > 0 and <= 10000
            && options.PropDiagnostics.SlowPropThreshold >= TimeSpan.Zero,
            "PropDiagnostics requires MaxEntries between 1 and 10000 and a nonnegative SlowPropThreshold.");
        optionsBuilder.Validate(options => Enum.IsDefined(options.LargeIntegerSerialization),
            "LargeIntegerSerialization must be Number, String, or BigInt.");

        // HttpContextAccessor (idempotent)
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Scoped Inertia service
        services.TryAddScoped<IInertiaService, InertiaService>();

        // Note: InertiaMiddleware is NOT registered in DI.
        // UseMiddleware<T>() instantiates conventional middleware (constructor takes RequestDelegate)
        // itself via its own factory — registering it as a service causes "unable to resolve
        // RequestDelegate" errors because the DI container cannot provide RequestDelegate.

        // The optional SSR gateway is registered separately by AddInertiaWithSsr().

        // Tag helpers require IHttpContextAccessor (already registered above)
        // They are resolved by Razor automatically when AddRazorPages/AddControllersWithViews is called

        // Global MVC filters
        services.Configure<MvcOptions>(mvcOptions =>
        {
            // Run before ApiController's automatic invalid-model filter (-2000).
            mvcOptions.Filters.Add<InertiaPrecognitionFilter>(order: -3000);
            mvcOptions.Filters.Add<InertiaValidationFilter>();
        });

        return services;
    }

    /// <summary>
    /// Registers InertiaNet with SSR enabled, using <see cref="HttpSsrGateway"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="InertiaOptions"/>.</param>
    public static IServiceCollection AddInertiaWithSsr(
        this IServiceCollection services,
        Action<InertiaOptions>? configure = null)
        => services.AddInertiaWithSsr<InertiaMiddleware>(configure);

    /// <summary>
    /// Registers an <see cref="IInertiaEventHandler"/> implementation for the render pipeline.
    /// </summary>
    public static IServiceCollection AddInertiaEventHandler<T>(this IServiceCollection services)
        where T : class, IInertiaEventHandler
        => services.AddScoped<IInertiaEventHandler, T>();

    /// <summary>
    /// Registers the Vite integration, making <c>&lt;vite-input&gt;</c> and
    /// <c>&lt;vite-react-refresh&gt;</c> tag helpers available in Razor views.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="ViteOptions"/>.</param>
    public static IServiceCollection AddViteHelper(
        this IServiceCollection services,
        Action<ViteOptions>? configure = null)
    {
        // Registers IOptions<ViteOptions> so the ViteInputTagHelper and
        // ViteReactRefreshTagHelper can receive it via constructor injection.
        var optionsBuilder = services.AddOptions<ViteOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();

        return services;
    }

    /// <summary>
    /// Registers InertiaNet with SSR enabled and a custom middleware subclass.
    /// </summary>
    public static IServiceCollection AddInertiaWithSsr<TMiddleware>(
        this IServiceCollection services,
        Action<InertiaOptions>? configure = null)
        where TMiddleware : InertiaMiddleware
    {
        services.AddInertia<TMiddleware>(opts =>
        {
            opts.Ssr.Enabled = true;
            configure?.Invoke(opts);
        });

        // Register HttpClient for the SSR gateway
        services.AddHttpClient(InertiaSsr.HttpClientName);
        services.TryAddTransient<ISsrGateway, HttpSsrGateway>();

        return services;
    }
}
