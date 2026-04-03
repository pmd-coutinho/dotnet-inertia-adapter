using Microsoft.AspNetCore.Http;

namespace InertiaNet.Core;

/// <summary>
/// Request-scoped Inertia render settings resolved by the middleware.
/// These values must not be recomputed from shared options later in the pipeline.
/// </summary>
internal sealed class InertiaRequestSettings
{
    public InertiaRequestSettings(string rootView, string? version)
    {
        RootView = rootView;
        Version = version;
    }

    public string RootView { get; }
    public string? Version { get; }

    public static InertiaRequestSettings Resolve(HttpContext httpContext, InertiaOptions options)
    {
        if (httpContext.Items.TryGetValue(InertiaContextKeys.RequestSettingsKey, out var value)
            && value is InertiaRequestSettings settings)
        {
            return settings;
        }

        return new InertiaRequestSettings(options.RootView, options.Version?.Invoke());
    }
}
