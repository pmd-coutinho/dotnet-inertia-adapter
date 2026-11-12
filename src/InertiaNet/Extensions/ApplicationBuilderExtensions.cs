using InertiaNet.Core;
using InertiaNet.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace InertiaNet.Extensions;

/// <summary>
/// Extension methods on <see cref="IApplicationBuilder"/> for registering the Inertia middleware,
/// and on <see cref="WebApplication"/> for Inertia error pages.
/// </summary>
public static class ApplicationBuilderExtensions
{
    private const string ErrorPagesRegisteredKey = "InertiaNet.ErrorPagesRegistered";

    /// <summary>
    /// Registers the default <see cref="InertiaMiddleware"/> in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseInertia(this IApplicationBuilder app)
        => app.UseMiddleware<InertiaMiddleware>();

    /// <summary>
    /// Registers a custom subclass of <see cref="InertiaMiddleware"/> in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseInertia<TMiddleware>(this IApplicationBuilder app)
        where TMiddleware : InertiaMiddleware
        => app.UseMiddleware<TMiddleware>();

    /// <summary>
    /// Renders ASP.NET Core error responses as an Inertia error page: registers
    /// <c>UseExceptionHandler("{Path}/500")</c> (unless <see cref="InertiaErrorPageOptions.HandleExceptions"/>
    /// is false, the default in Development) and <c>UseStatusCodePagesWithReExecute("{Path}/{0}")</c> for the
    /// requests <see cref="InertiaErrorPageOptions.RequestPredicate"/> selects, and maps
    /// <c>{Path}/{status:int:range(400,599)}</c> for every method to render
    /// <see cref="InertiaErrorPageOptions.Component"/> with a <c>status</c> prop and that status.
    /// </summary>
    /// <remarks>
    /// Call it before <c>UseInertia()</c>, so the Inertia middleware runs again when the error route is
    /// re-executed. The page reports the original request URL. JSON API requests, Precognition requests and
    /// paths excluded by <see cref="InertiaOptions.RequestPredicate"/> keep plain error responses by default.
    /// </remarks>
    /// <param name="app">The application.</param>
    /// <param name="configure">Optional configuration of the component, path and request selection.</param>
    /// <returns>The application, for chaining.</returns>
    /// <exception cref="ArgumentException">The configured component or path is invalid.</exception>
    /// <exception cref="InvalidOperationException">The helper was already called on this application.</exception>
    public static WebApplication UseInertiaErrorPages(this WebApplication app, Action<InertiaErrorPageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A second call would map the error route twice, and every re-executed error would then fail with an
        // AmbiguousMatchException inside the error handler.
        var properties = ((IApplicationBuilder)app).Properties;
        if (properties.ContainsKey(ErrorPagesRegisteredKey))
            throw new InvalidOperationException("UseInertiaErrorPages() can only be called once per application.");

        var options = new InertiaErrorPageOptions { HandleExceptions = !app.Environment.IsDevelopment() };
        configure?.Invoke(options);
        options.Validate();
        properties[ErrorPagesRegisteredKey] = true;

        var path = options.Path.TrimEnd('/');
        var component = options.Component;
        var handleExceptions = options.HandleExceptions;
        var predicate = options.RequestPredicate;

        // The branch decides on the original request; re-execution continues after it, on the error route.
        app.Use(next =>
        {
            var branch = CreateBranchWithGlobalRouting(app);
            if (handleExceptions)
                branch.UseExceptionHandler($"{path}/{StatusCodes.Status500InternalServerError}");
            branch.UseStatusCodePagesWithReExecute($"{path}/{{0}}");
            branch.Run(next);
            var errorPages = branch.Build();
            return context => predicate(context) ? errorPages(context) : next(context);
        });

        // Map, not MapGet: re-execution keeps the original method, so a failed POST arrives as a POST.
        // The range keeps /error/302 or /error/200 from failing in WithStatus; they get the 404 page.
        var endpoint = app.Map($"{path}/{{status:int:range(400,599)}}", (int status, IInertiaService inertia) =>
                inertia.Render(component, new { status }).WithStatus(status))
            .WithInertiaComponent(component)
            .ExcludeFromDescription();
        if (options.AllowAnonymous)
            endpoint.AllowAnonymous();
        options.ConfigureEndpoint?.Invoke(endpoint);

        return app;
    }

    // UseWhen would branch with WebApplication's New(), which drops the global endpoint route builder, so
    // UseExceptionHandler and UseStatusCodePagesWithReExecute would re-execute without routing and every error
    // route would 404. A branch holding a copy of the application's properties keeps the re-routing they do on
    // WebApplication. The copy is taken while the pipeline is built, as they read those properties then.
    private static ApplicationBuilder CreateBranchWithGlobalRouting(WebApplication app)
    {
        var branch = new ApplicationBuilder(app.Services);
        foreach (var (key, value) in ((IApplicationBuilder)app).Properties)
            branch.Properties[key] = value;
        return branch;
    }
}
