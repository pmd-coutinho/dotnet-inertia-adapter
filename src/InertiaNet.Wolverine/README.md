# InertiaNet.Wolverine

Serve [Inertia.js](https://inertiajs.com) pages from
[Wolverine.HTTP](https://wolverinefx.net/guide/http/) endpoints with
[InertiaNet](https://github.com/pmd-coutinho/dotnet-inertia-adapter).

Requires .NET 9 or later and WolverineFx.Http 6.

## Setup

Register InertiaNet as usual, then call `UseInertia()` when mapping the
Wolverine endpoints:

```csharp
builder.Host.UseWolverine();
builder.Services.AddWolverineHttp();
builder.Services.AddControllersWithViews(); // the initial page renders through Razor
builder.Services.AddInertia();

var app = builder.Build();
app.UseInertia();
app.MapWolverineEndpoints(opts => opts.UseInertia());
```

WolverineFx 6 compiles endpoint code at run time only when
`WolverineFx.RuntimeCompilation` is referenced (or when code is generated ahead
of time); see the Wolverine documentation.

## Endpoints

An endpoint returns the `InertiaResult` from `IInertiaService.Render`:

```csharp
public static class DashboardEndpoints
{
    [WolverineGet("/dashboard")]
    public static IResult Get(IInertiaService inertia) =>
        inertia.Render("Dashboard", new
        {
            stats = inertia.Defer(async (services, ct) => await LoadStatsAsync(services, ct)),
        });
}
```

Initial HTML visits, Inertia JSON visits, asset-version checks, partial reloads,
deferred props and props shared by `InertiaMiddleware` all work as they do for
Minimal API and MVC endpoints.

## Why `UseInertia()` is needed

`UseInertia()` tells Wolverine to take `IInertiaService` from
`HttpContext.RequestServices`. Without it, Wolverine builds the service in its
own scope, and props shared by the Inertia middleware are silently missing from
the page.

## Not yet supported

Validation error forwarding and Precognition for Wolverine.HTTP form endpoints
are planned. Until then, use a Minimal API or MVC endpoint for forms that need
them.
