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

        // HttpContextAccessor (idempotent)
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Scoped Inertia service
        services.TryAddScoped<IInertiaService, InertiaService>();

        // Note: InertiaMiddleware is NOT registered in DI.
        // UseMiddleware<T>() instantiates conventional middleware (constructor takes RequestDelegate)
        // itself via its own factory — registering it as a service causes "unable to resolve
        // RequestDelegate" errors because the DI container cannot provide RequestDelegate.

        // SSR gateway (only registered when SSR URL is configured via AddSsr<>())
        // — registered separately via AddInertiaWithSsr()

        // Tag helpers require IHttpContextAccessor (already registered above)
        // They are resolved by Razor automatically when AddRazorPages/AddControllersWithViews is called

        // Global MVC filters
        services.Configure<MvcOptions>(mvcOptions =>
        {
            mvcOptions.Filters.Add<InertiaPrecognitionFilter>(order: -100);
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
        services.AddHttpClient<ISsrGateway, HttpSsrGateway>();

        return services;
    }
}
