using InertiaNet.Core;
using InertiaNet.Middleware;

namespace Playground;

public sealed class PlaygroundMiddleware(RequestDelegate next) : InertiaMiddleware(next)
{
    protected override Task Share(HttpContext context, IInertiaService inertia)
    {
        inertia.Share("xsrfToken", context.Items["Playground.CsrfToken"] as string);
        inertia.Share("auth", new AuthShared(context.User.Identity?.IsAuthenticated == true
            ? new UserSummary(42, context.User.Identity.Name ?? "Reader") : null));
        return Task.CompletedTask;
    }
    protected override Task ShareOnce(HttpContext context, IInertiaService inertia)
    {
        inertia.ShareOnce("app", (Func<AppShared>)(() => new AppShared("InertiaNet playground")));
        return Task.CompletedTask;
    }
}
