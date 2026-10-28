using InertiaNet.Core;
using InertiaNet.Middleware;

namespace KitchenSink;

public sealed class KitchenSinkMiddleware(RequestDelegate next) : InertiaMiddleware(next)
{
    protected override Task Share(HttpContext context, IInertiaService inertia)
    {
        inertia.Share("xsrfToken", context.Items["KitchenSink.CsrfToken"] as string);
        inertia.Share("auth", new { name = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null });
        inertia.Share("request", inertia.Always(new { id = Guid.NewGuid().ToString("N")[..8], at = DateTimeOffset.UtcNow.ToString("O") }));
        inertia.When(context.User.Identity?.IsAuthenticated == true, "memberMessage", "Shared conditionally for signed-in visitors.");
        return Task.CompletedTask;
    }

    protected override Task ShareOnce(HttpContext context, IInertiaService inertia)
    {
        inertia.ShareOnce("reference", (_, _) => Task.FromResult<object?>(new
        {
            framework = "InertiaNet · React", loadedAt = DateTimeOffset.UtcNow.ToString("O"),
            teams = new[] { "Design", "Engineering", "Operations" },
        }));
        return Task.CompletedTask;
    }
}

public sealed class RenderObserver : IInertiaEventHandler
{
    public Task OnAfterResolveProps(HttpContext context, Dictionary<string, object?> props)
    {
        props["renderHook"] = "Added by OnAfterResolveProps";
        return Task.CompletedTask;
    }
}
