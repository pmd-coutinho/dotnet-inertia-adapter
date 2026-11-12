# ASP.NET Core Integration Extensions

## Overview

Public extensions register the runtime and connect controllers, Minimal APIs, middleware, validation, request headers, and health checks.

## Interface

- `AddInertia` / `AddInertia<TMiddleware>`, `AddInertiaWithSsr` / `AddInertiaWithSsr<TMiddleware>`, `AddInertiaEventHandler<T>`, and `AddViteHelper` configure services.
- `AddInertiaChallengeHandling` (on `IServiceCollection` or `AuthenticationBuilder`) decorates `IAuthenticationService` so challenges/forbids follow the Inertia protocol.
- `UseInertia` / `UseInertia<TMiddleware>` install conventional middleware. `UseInertiaErrorPages` (on `WebApplication`, opt-in) registers the exception handler and re-executing status code pages for selected requests and maps the `{Path}/{status:int:range(400,599)}` error route rendering an Inertia error page.
- `ControllerExtensions.Inertia` renders controller responses; `InertiaResults.Inertia` supports objects, dictionaries, typed components, and synchronous/async request factories; each form has an overload taking a `statusCode` that is applied with `WithStatus`. `InertiaResults.Location` creates external navigation results.
- `MapInertia` maps GET routes; `MapInertiaFallback` maps a fallback. Both support static props or request factories and attach component metadata.
- `WithInertiaComponent`, `WithInertiaPrecognition`, and `WithInertiaValidation` configure endpoints; the latter two also support route groups.
- `SetInertiaValidationErrors` accepts dictionaries, `ValidationProblemDetails`, or ModelState; `SetInertiaErrorBag` supplies a bag override.
- Request extensions inspect Inertia status, partial component/only/except, version, error bag, reset paths, and loaded once keys.
- `AddInertiaSsr` registers an optional SSR health check on `IHealthChecksBuilder`.

## Invariants

- `AddInertia` uses singleton HTTP-context access and a scoped `IInertiaService`, preserving existing registrations through `TryAdd`. Conventional middleware is activated by `UseMiddleware` rather than registered as a service.
- `AddInertiaChallengeHandling` registers the authentication core if needed and wraps the existing `IAuthenticationService` once, whatever the call order relative to `AddAuthentication`. Repeated calls only add option configuration.
- Global MVC Precognition runs at order `-3000`, before the automatic invalid-model filter; validation forwarding is also registered.
- Option validation requires a positive bounded SSR timeout or `Timeout.InfiniteTimeSpan`, diagnostics entry bounds of 1–10000, a nonnegative slow threshold, and defined large-integer and validation error key formats.
- SSR registration enables SSR before invoking the configuration delegate, registers the named HTTP client, and preserves an existing gateway registration. The delegate can override enabled state.
- `UseInertiaErrorPages` must run before `UseInertia` so the middleware runs again on the error route. Its request predicate is evaluated on the original request; the error middlewares run in a branch that keeps `WebApplication`'s global route builder, because a `UseWhen` branch would re-execute without routing. Exception handling defaults to off in Development. The error route is mapped with `AllowAnonymous()` unless `InertiaErrorPageOptions.AllowAnonymous` is false, and `ConfigureEndpoint` runs after that. The helper can be called once per application (a second call throws `InvalidOperationException`, as a duplicate route would make every error ambiguous), and `Path` cannot be `/`.
- Result factories resolve the scoped service at execution; async factories receive `RequestAborted`. Their `statusCode` overloads validate the status when the result is created and throw `ArgumentOutOfRangeException` for statuses that cannot carry a page.
- `IsInertia()` checks header presence regardless of its value. List header readers split comma-separated values and trim/remove empty entries.
- Validation helpers copy errors to request items; redirect persistence requires the corresponding filter, which normalizes their keys like the MVC filter. Health-check registration does not map a public health endpoint.

## Side Effects

Registers services, options, filters, health checks, routes, and middleware. Validation helpers mutate request items; result execution invokes application factories and rendering/redirect behavior. Vite registration adds a shared memory cache.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
