# InertiaNet

A compatibility-first ASP.NET Core server adapter for [Inertia.js v3](https://inertiajs.com/docs/v3/getting-started).

Includes support for deferred props, merge props, once props, infinite scroll, history encryption, flash data, SSR with Vite hot mode, fragment preservation, prefetch support, and testing utilities.

**Targets:** .NET 8, .NET 9, .NET 10

**Versioning:** Stable packages are version 3.5.3. The next development train
is 3.5.4; the reviewed Laravel adapter baseline remains 3.5.2. See
[releasing](docs/releasing.md) for compatibility tracking and publishing rules.

---

## Verified integration

The [kitchen sink](samples/KitchenSink/README.md) is an interactive React feature
collection with persistent SQLite CRUD, forms/uploads, Precognition, prop loading
and merging, infinite scroll, polling, history, SSR and a response inspector.
Its Aspire AppHost starts the local services together; a standalone run path is
also available.

The [React/Vue playground](samples/Playground/README.md) demonstrates typed Pathfinder imports, CRUD, error bags, flash, cookie authentication, deferred props, scroll merging, native forms and SSR. Its browser tests exercise hydration, CSR fallback, custom roots and actual antiforgery cookies/headers. Runtime tests cover .NET 8, 9 and 10; generated-code fixtures pin Inertia **3.0.2** and **3.9.1**. The playground uses 3.9.1.

## Installation

```
dotnet add package InertiaNet --version 3.5.3
```

---

## Getting Started

### 1. Register services

```csharp
// Program.cs
builder.Services.AddControllersWithViews();
builder.Services.AddInertia();

// Optionally register the Vite helper (enables <vite-input> tag helpers)
builder.Services.AddViteHelper();
```

### 2. Add the middleware

```csharp
app.UseInertia(); // after UseSession / UseAuthentication
```

### Session & TempData

Flash data and validation errors across redirects require ASP.NET Core TempData services. `AddControllersWithViews()` registers the default cookie TempData provider, which works without session middleware.

To use the session-backed provider instead:

```csharp
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddControllersWithViews().AddSessionStateTempDataProvider();
// ...
app.UseSession();
app.UseInertia();
```

Register a distributed session/cache provider for a multi-instance deployment. Without a TempData provider, redirect state cannot persist.

### 3. Create the root layout

Add `Views/Shared/App.cshtml`:

```razor
@addTagHelper *, InertiaNet

<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>My App</title>
    <inertia-head />
</head>
<body>
    <vite-react-refresh />
    <inertia />
    <vite-input src="src/app.tsx" />
</body>
</html>
```

The recommended frontend workspace lives under `ClientApp/`:

```text
YourApp/
  ClientApp/
    package.json
    vite.config.ts
    tsconfig.json
    src/
      app.tsx
      pages/
  Program.cs
  Views/
  wwwroot/
```

---

## Configuration

All options are configured via `AddInertia()`:

```csharp
builder.Services.AddInertia(options =>
{
    options.RootView = "App";              // Razor view for initial HTML render
    options.EncryptHistory = false;        // Enable history encryption globally
    options.WithAllErrors = false;         // Return all validation errors per field
    options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots; // "values.3.code" (default); Brackets → "values[3].code"
    options.ExposeSharedPropKeys = true;   // Include sharedProps key list in responses
    options.PrefetchCacheMaxAge = 10;      // Default Cache-Control max-age for prefetch (seconds)
    options.Version = ManifestVersionStrategy.FromViteOrMix(webRootPath); // Asset versioning

    // Page component validation (catches typos during development)
    options.Pages.EnsurePagesExist = true;
    options.Pages.Paths = ["ClientApp/src/pages"];                 // source tree (HMR/dev mode)
    options.Pages.ManifestPaths = ["wwwroot/build/manifest.json"]; // build output (production)
});
```

> **Breaking change since 3.5.3:** validation error keys now follow the props naming policy per segment
> (a `null` `PropertyNamingPolicy` renames nothing) and indexers default to dot paths (`values.3.code`).
> See [Error Keys](#error-keys) for details and how to keep bracket indexers.

### SSR

```csharp
builder.Services.AddInertiaWithSsr(options =>
{
    options.Ssr.Url = "http://127.0.0.1:13714"; // Node.js SSR server
    options.Ssr.ThrowOnError = false;           // Fall back to CSR on failure
    options.Ssr.Timeout = TimeSpan.FromSeconds(3); // Default; bounds CSR fallback latency
    options.Ssr.ExcludePaths = ["/admin/*"];  // Skip SSR for matching routes
});

// Configure headers, handlers, proxy settings, or a shorter HttpClient timeout.
builder.Services.AddHttpClient(InertiaNet.Ssr.InertiaSsr.HttpClientName, client =>
{
    client.DefaultRequestHeaders.Add("X-SSR-App", "my-app");
});

// Optional: register with your application's existing health endpoint.
builder.Services.AddHealthChecks().AddInertiaSsr();
```

SSR timeout covers the request and response body. Set `Timeout.InfiniteTimeSpan` to disable the adapter's limit. Client disconnects propagate cancellation; they do not become CSR fallbacks. `ThrowOnError` exposes `SsrException.ErrorType`, including structured server errors (`BrowserApi`, `ComponentResolution`, `Render`) and local `Connection`/`Timeout` failures. The optional health check uses the same named client and timeout to probe the dedicated Node server at `Ssr.Url + "/health"`; disabled SSR is healthy. It does not map a health endpoint or probe Vite's development render bridge.

Subscribe to the `InertiaNet.SSR` ActivitySource and Meter through your usual .NET/OpenTelemetry configuration. Render spans include the component, outcome, response status, and classified error. `inertianet.ssr.renders` counts renders and `inertianet.ssr.duration` measures seconds, with `outcome` (`success`, `fallback`, `failure`, `cancelled`) and failure-only `error.type` tags. Prop values are excluded from telemetry.

Recommended deployment model:

- **Development**: run ASP.NET Core, Vite, and the Node SSR server together; `vite-input` uses the `hot` file and Inertia SSR posts to `__inertia_ssr` on the Vite dev server.
- **Production**: serve built frontend assets from `wwwroot/build`, run the Node SSR server separately, and point `options.Ssr.Url` at that long-lived process.
- **Fallback mode**: keep `ThrowOnError = false` unless SSR failures should fail the whole request; this allows production SSR to degrade cleanly to CSR.

Skip SSR for the current response:

```csharp
app.MapGet("/reports", (IInertiaService inertia) =>
{
    inertia.WithoutSsr();
    return inertia.Render("Reports/Index");
});
```

### Vite Helper

```csharp
builder.Services.AddViteHelper(options =>
{
    options.PublicDirectory  = "wwwroot";       // Web root
    options.BuildDirectory   = "build";         // Vite output sub-directory
    options.ManifestFilename = "manifest.json";
    options.HotFile          = "hot";           // Written by laravel-vite-plugin in dev
});
```

InertiaNet expects the same Vite conventions in development and production:

- development uses `PublicDirectory/HotFile` to discover the active dev server
- production uses `PublicDirectory/BuildDirectory/ManifestFilename` to resolve hashed assets
- SSR hot mode uses the same hot-file location, so keep Vite and ASP.NET aligned on `PublicDirectory`
- Vite itself is expected to run from `ClientApp/`, while built assets still land in `wwwroot/build`

---

## Request Lifecycle

At a high level, InertiaNet processes requests in four stages:

1. `InertiaMiddleware` prepares the request.
   It shares global props, restores flash data and validation errors from TempData, resolves request-scoped `Version` and `RootView`, and handles version mismatches before the endpoint runs.
2. Your endpoint returns `inertia.Render(...)`.
   This produces an `InertiaResult` that works for both MVC and Minimal APIs.
3. `PropsResolver` resolves the page props.
   Shared props, page props, deferred props, merge props, once props, scroll metadata, and event handlers are all applied here.
4. The response is written.
   Inertia XHR requests receive JSON. Initial page loads render the root Razor view, optionally with SSR markup injected via `<inertia-head />` and `<inertia />`. Successful official SSR bodies already include their root and page script and are emitted directly.

This split is intentional:

- middleware owns request concerns such as versioning, TempData, and shared request state
- `InertiaResult` owns page construction and response writing
- tag helpers own HTML embedding of the already-built page and SSR payload

---

## Shared Props & Middleware

Subclass `InertiaMiddleware` to share props on every request — the equivalent of Laravel's `HandleInertiaRequests`:

```csharp
public class HandleInertiaRequests : InertiaMiddleware
{
    protected override Task Share(HttpContext context, IInertiaService inertia)
    {
        inertia.Share("auth", new
        {
            user = context.User.Identity?.Name,
        });
        return Task.CompletedTask;
    }

    protected override string? GetVersion(HttpContext context)
        => ManifestVersionStrategy.Hash("wwwroot/build/manifest.json");
}
```

Register it:

```csharp
builder.Services.AddInertia<HandleInertiaRequests>();
app.UseInertia<HandleInertiaRequests>();
```

For an application that mixes Inertia pages with other endpoints, restrict the
middleware and validation filters to the page paths:

```csharp
builder.Services.AddInertia<HandleInertiaRequests>(options =>
{
    options.RequestPredicate = context => context.Request.Path.StartsWithSegments("/app");
});
```

The predicate defaults to handling every path. It must include initial HTML
requests, which have no `X-Inertia` header. Excluded requests skip sharing hooks,
version checks, Inertia response headers, and Inertia TempData processing. The
predicate also applies to MVC and explicitly attached Minimal API validation
and Precognition filters; it does not disable the application's antiforgery or
authorization middleware.

Empty, unstarted Inertia `200` and `204` responses redirect back by default.
Set `options.RedirectEmptyResponses = false` to preserve intentional empty
responses. Standard mutation redirect conversion (`302` to `303`) still applies;
Precognition responses are always excluded from empty-response redirects.

### Conditional Props

Use `When()` / `Unless()` to conditionally share props:

```csharp
inertia.When(user.IsAdmin, "adminSettings", adminSettings);
inertia.Unless(user.IsGuest, "notifications", async (sp, ct) =>
    await sp.GetRequiredService<INotificationService>().GetAsync(ct));
```

### CSRF/XSRF Token

When `IAntiforgery` is registered, InertiaNet shares a lazy `xsrfToken` prop.
The token is generated during page prop resolution, after the handler has run
and before response headers start. Redirects and unrelated responses do not
generate a token; partial reloads generate one only when they include this prop.
Initial HTML pages include it. Override `ShareCsrfToken` in your middleware
subclass to customise or disable:

```csharp
protected override Task ShareCsrfToken(HttpContext context, IInertiaService inertia)
{
    // Disable automatic CSRF sharing
    return Task.CompletedTask;
}
```

### Navigation Context

`RenderContext` (passed to `IProvidesInertiaProperties`) and `PropertyContext` (passed to `IProvidesInertiaProperty`) expose navigation state properties:

- `IsInertiaRequest` — true for Inertia XHR requests
- `IsPartialReload` — true when this is a partial reload for the current component (RenderContext only)
- `Referer` — the Referer header value
- `PartialComponent` — the partial-reload component name

---

## Rendering

### Minimal API

```csharp
app.MapGet("/", (IInertiaService inertia) => inertia.Render("Home"));

// First-class Minimal API result helper
app.MapGet("/dashboard", () => InertiaResults.Inertia("Dashboard", new { ready = true }));

// Explicit status code, e.g. for an error page (see Error Handling)
app.MapGet("/gone", () => InertiaResults.Inertia("Errors/Status", new { status = 410 }, StatusCodes.Status410Gone));

// Request-aware props without resolving IInertiaService manually
app.MapGet("/account", (HttpContext ctx) => InertiaResults.Inertia("Account", new
{
    path = ctx.Request.Path.Value,
    user = ctx.User.Identity?.Name,
}));

// Static route shorthand
app.MapInertia("/about", "About");

// Request-aware static route shorthand
app.MapInertia("/settings", "Settings", ctx => new { tab = ctx.Request.Query["tab"].ToString() });

// SPA fallback for frontend-driven routes
app.MapInertiaFallback("AppShell");
```

Initial HTML rendering for Minimal APIs still requires Razor view services and a root view.
Register `AddControllersWithViews()` or `AddRazorPages()` in addition to `AddInertia(...)`.

### MVC Controller

```csharp
public class PostsController : Controller
{
    public IActionResult Index()
        => this.Inertia("Posts/Index", new { posts = _db.Posts.ToList() });
}
```

`ControllerBase.Inertia(...)` remains the primary MVC surface.
If you need data for the root Razor view that should not be exposed to the frontend, use `WithViewData(...)` on the returned `InertiaResult`.
Use `WithStatus(...)` to send a status other than 200; see [Error Handling](#error-handling).

### Typed page contracts

Pass a props instance to the generic rendering overload, or declare a component that fixes its props type:

```csharp
public record DashboardProps(string UserName, int TotalPosts);

public static class Pages
{
    public static readonly InertiaComponent<DashboardProps> Dashboard = new("Dashboard");
}

// The compiler checks the component's props type.
return inertia.Render(Pages.Dashboard, new DashboardProps("Ana", 42));
// Minimal APIs:
return InertiaResults.Inertia(Pages.Dashboard, new DashboardProps("Ana", 42));
// String-based generic overload:
return inertia.Render<DashboardProps>("Dashboard", new DashboardProps("Ana", 42));
```

Typed props honor supported System.Text.Json property names, ignore conditions, extension data, and configured contracts without evaluating deferred wrappers early. Dictionaries retain their explicit top-level keys. An object-level custom converter owns its entire representation and must produce a JSON object.

Pathfinder generates a component-to-props map (`PropsFor<'Dashboard'>`), inferred shared-prop augmentation for `@inertiajs/core`, and separate JSON body, query and multipart contracts. Its [contract profile documentation](src/InertiaNet.Pathfinder/README.md#serialization-profiles) explains how to match custom JSON settings without running application configuration during generation.

For CI checks against MVC conventions, conditional registration or custom endpoint extensions, explicitly export `InertiaEndpointManifest.Capture(app)` and pass it to `pathfinder --endpoint-manifest endpoints.json --strict --check`. See the [endpoint-validation workflow](src/InertiaNet.Pathfinder/README.md#optional-registered-endpoint-validation).

### Large integers

The default JSON-number policy retains existing behavior. To keep `long`, `ulong`, and `BigInteger` values exact in JavaScript, choose a transport explicitly:

```csharp
builder.Services.AddInertia(options =>
    options.LargeIntegerSerialization = LargeIntegerSerialization.BigInt);
```

`BigInt` sends safe integers as numbers and larger values as Inertia `$bigint` markers, with `preserveBigIntegers: true`. This requires a client with the BigInt parser; it is tested against **Inertia 3.9.1**. Use `LargeIntegerSerialization.String` for decimal strings compatible with older clients, including the tested 3.0.2 client. Both modes cover nested props and flash. `BigInteger` also supports numeric transport. Decimal/floating-point values, dictionary keys and protocol timestamps retain their existing representation; explicit application converters take precedence.

Match Pathfinder with a contract file, for example `{ "output": { "largeIntegers": "BigInt" } }`, and `pathfinder --contracts contracts.json`. The generated output uses `number | bigint` in marker mode and `string` in string mode. JSON request handling remains controlled by ASP.NET's JSON configuration; modern Inertia clients submit BigInt request values as decimal strings.

Test responses with `HasBigIntegerProp(path, expected)`, `HasBigIntegerFlash(path, expected)` and `HasPreserveBigIntegers()` to compare exact integer values without floating-point conversion.

### Antiforgery and native forms

The starters configure ASP.NET's antiforgery header as `X-XSRF-TOKEN`, set the readable `XSRF-TOKEN` cookie on initial GETs, and validate unsafe MVC actions. Native forms use the `__RequestVerificationToken` field and `_method` override middleware. The [playground](samples/Playground/README.md) shows the complete flow and tests missing-token rejection plus successful JSON/native submissions. Minimal API applications should explicitly validate unsafe JSON endpoints with `IAntiforgery` or appropriate endpoint metadata.

## Analyzers

`InertiaNet.Analyzers` adds Roslyn diagnostics for common InertiaNet and Pathfinder mistakes.

Current diagnostics:

- `INERTIA001` invalid Inertia component names
- `INERTIA002` `JsonSerializerOptions` naming policies that do not affect the Inertia envelope (they shape prop values and [validation error keys](#error-keys))
- `INERTIA003` missing page component files when page validation is enabled
- `PATHFINDER001` Minimal API route templates that Pathfinder cannot resolve statically
- `PATHFINDER002` Minimal API handlers that cannot be resolved as a lambda or unambiguous source method group

Add it to your app project like any other analyzer package:

```xml
<ItemGroup>
  <PackageReference Include="InertiaNet.Analyzers" Version="VERSION" PrivateAssets="all" />
</ItemGroup>
```

Replace `VERSION` with the same published version as your `InertiaNet` package.

## Templates

`InertiaNet.Templates` ships starter templates for React and Vue.

Available templates:

- `inertianet-react`
- `inertianet-vue`

Install and use them:

```bash
dotnet new install InertiaNet.Templates@VERSION

dotnet new inertianet-react -n MyReactApp
dotnet new inertianet-vue -n MyVueApp
```

Replace `VERSION` with an exact published version from [NuGet](https://www.nuget.org/packages/InertiaNet.Templates), such as stable `3.5.3`. The next development prefix is 3.5.4; automatic builds use `3.5.4-alpha.<publishing workflow run number>`. The templates target .NET 10; SDK 10 template installation requires the explicit version. Keep all InertiaNet package references on the same published build. Local `3.5.4-alpha.0` builds are not a published release.

Each starter includes:

- an ASP.NET Core app configured with `AddInertia`, `AddViteHelper`, and a root Razor view
- a `ClientApp/` frontend workspace with a minimal entrypoint and `Home` page
- a `.devcontainer/` configuration with .NET and Node ready to go
- a committed `wwwroot/` via `.gitkeep`, so Vite and static asset output have a stable home from day one
- Vite configuration wired to `wwwroot`
- `MapInertia` plus a fallback route for SPA-style navigation

For prerelease publishing and trusted publishing setup, see `docs/releasing.md`.

---

## Props

### Always Props

Included in every response, even partial reloads that would otherwise skip it:

```csharp
inertia.Render("Dashboard", new
{
    errors = inertia.Always(new {}),
});
```

### Optional Props

Excluded from the initial load; only included when explicitly requested via `only`:

```csharp
inertia.Render("Users/Index", new
{
    users = inertia.Optional(async (sp, ct) =>
        await sp.GetRequiredService<IUserService>().GetAllAsync(ct)),
});
```

### Deferred Props

Excluded from the initial load. The client fetches them asynchronously after the first render. Group related props to batch the follow-up request:

```csharp
inertia.Render("Dashboard", new
{
    revenue    = inertia.Defer(async (sp, ct) => await GetRevenueAsync()),
    topClients = inertia.Defer(async (sp, ct) => await GetTopClientsAsync(), group: "charts"),
    topProducts = inertia.Defer(async (sp, ct) => await GetTopProductsAsync(), group: "charts"),
});
```

### Deferred-prop recovery

Use `.Rescue()` to keep a failed deferred section from failing the whole response:

```csharp
inertia.Render("Dashboard", new
{
    stats = inertia.Defer(async (sp, ct) => await LoadStatsAsync(ct)).Rescue(),
});
```

A failed rescued prop is omitted and listed in `rescuedProps`; the error is logged. The client can retry that prop. Cancellation still propagates. This requires a client release supporting rescued deferred props.

### Merge Props

The resolved value is merged (appended) into the client's existing data instead of replacing it:

```csharp
inertia.Render("Feed", new
{
    posts = inertia.Merge(async (sp, ct) => await GetPageAsync()),
});

// Deep merge
posts = inertia.DeepMerge(async (sp, ct) => await GetPageAsync());
```

### Once Props

Resolved once and remembered by the client across navigations. Ideal for shared reference data:

```csharp
inertia.Render("Billing", new
{
    plans = inertia.Once(async (sp, ct) => await GetPlansAsync()),
});

// With expiry and custom cache key
plans = inertia.Once(async (sp, ct) => await GetPlansAsync())
               .Until(TimeSpan.FromHours(1))
               .As("billingPlans");
```

Share once-props globally:

```csharp
// In HandleInertiaRequests.ShareOnce():
protected override Task ShareOnce(HttpContext context, IInertiaService inertia)
{
    inertia.ShareOnce("countries", async (sp, ct) =>
        await sp.GetRequiredService<ICountryService>().GetAllAsync(ct));
    return Task.CompletedTask;
}
```

### Combining Modifiers

Modifiers are chainable:

```csharp
// Deferred + merge + once
inertia.Defer(async (sp, ct) => await GetActivityAsync()).Merge().Once();

// Optional + once
inertia.Optional(async (sp, ct) => await GetCategoriesAsync()).Once();
```

---

## Flash Data

Send one-time data that is not persisted in browser history state. Automatically survives redirects:

```csharp
inertia.Flash("message", "User created successfully!");
return RedirectToAction("Index");
```

Access client-side via `page.flash.message`.

---

## Navigation

### External Redirect

Forces a full-page navigation (bypasses the Inertia client router):

```csharp
return inertia.Location("https://example.com");
```

### History Encryption

Encrypt page data in browser history to prevent sensitive data leaking via the back button:

```csharp
// Globally in options
options.EncryptHistory = true;

// Per-request
inertia.EncryptHistory();

// Per-route (MVC filter)
[EncryptHistory]
public IActionResult Dashboard() => ...

// Per-route (Minimal API)
app.MapGet("/secure", handler).AddEndpointFilter<EncryptHistoryEndpointFilter>();
```

### Clear History

Rotate the encryption key so old history entries can no longer be decrypted:

```csharp
inertia.ClearHistory();
```

---

## Authentication

Inertia visits are XHR requests. When a session expires, ASP.NET Core authentication answers
them as it would answer a browser: a cookie scheme returns `401` (it detects the client's
`X-Requested-With` header) and an OpenID Connect scheme returns `302` to the identity
provider. The XHR follows that redirect and fails the identity provider's CORS check, and the
Inertia client renders neither response as a page. The Inertia protocol answer is
`409 Conflict` with `X-Inertia-Location`, which makes the client perform a full-page visit.

Opt in where you register authentication. The call can come before or after
`AddAuthentication()`:

```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options => builder.Configuration.Bind("Oidc", options))
    .AddInertiaChallengeHandling();
// or: builder.Services.AddInertiaChallengeHandling();
```

| Request | Challenge (not signed in) | Forbid (signed in, not allowed) |
|---------|---------------------------|---------------------------------|
| Inertia visit (`X-Inertia`) | `409` + `X-Inertia-Location: <the scheme's sign-in URL>` | `403` without `Location` |
| Inertia prefetch (`Purpose: prefetch`) | `409` + `X-Inertia-Location: <the prefetched URL>`; the scheme is not invoked | `403` without `Location` |
| API request (see below) | `401` without `Location` | `403` without `Location` |
| Any other request (browser navigation) | Unchanged (`302` to the sign-in page or identity provider) | Unchanged (`302` to the access-denied page) |

The scheme handler runs first and only its redirect is translated. A redirect includes the
cookie handler's XHR answer, which is a `401`/`403` that carries `Location`. Because only the
redirect is translated, the sign-in URL keeps the scheme's `ReturnUrl`/`state`, your scheme
events still apply, and a remote handler's correlation/nonce cookies are set on the XHR response
before the browser leaves for the identity provider. Responses that are not redirects (for
example a bearer `401` with `WWW-Authenticate`) are left unchanged.

The registered `IAuthenticationService` is decorated, so the handling covers authorization
middleware, MVC `[Authorize]`, `Results.Challenge()`/`Challenge()` and forwarded or policy schemes,
for any middleware order. When a policy lists several schemes (for example
`[Authorize(AuthenticationSchemes = "Cookies,Bearer")]`), each is challenged in turn; the location
visit from a redirecting scheme is kept when a later scheme only answers a bare `401`.
InertiaNet does not reference the OpenID Connect package. Sign-out is not translated: submit a
remote sign-out (which redirects to the identity provider) as a regular form or link rather than
an Inertia visit.

**Prefetch.** Inertia prefetches (for example on hover) should have no side effects, and a remote
challenge would set correlation/nonce cookies for a sign-in that may never happen. A prefetch is
therefore answered with a location visit to the prefetched URL without invoking the scheme. If the
prefetch is used, the client loads that page in full and the browser navigation is challenged
normally.

**Return URL after a mutation.** A `GET` visit returns to the page that was requested. When a
`POST`/`PUT`/`PATCH`/`DELETE` visit is challenged without an explicit
`AuthenticationProperties.RedirectUri`, the return URL is the same-origin `Referer` (path and
query), otherwise the application root (`PathBase + "/"`). A browser cannot revisit the mutation
URL with `GET`; Laravel's `Redirector::guest()` makes the same choice. Unsaved form state is lost
on the full-page visit. A `Referer` whose path is not local (such as `https://host//other.example`)
is ignored. The `Referer` is compared with the request's scheme and host, so behind a
TLS-terminating proxy configure `UseForwardedHeaders()`; otherwise an `https` `Referer` never
matches the `http` request and the return URL silently becomes the root.

**API requests.** `InertiaChallengeOptions.ApiRequestPredicate` selects the non-Inertia requests
that receive `401`/`403`. The default, `InertiaChallengeOptions.DefaultApiRequestPredicate`, selects:

- requests that expect JSON, using the same rule as Laravel's `expectsJson()`: the most preferred
  `Accept` type is JSON, or the request has `X-Requested-With: XMLHttpRequest` and accepts any type
  (this covers Precognition's `Accept: application/json`);
- script requests that the browser marks with a `Sec-Fetch-Mode` other than `navigate`, such as a
  plain `fetch()` with the default `Accept: */*`. Browsers send `Sec-Fetch-*` only to secure
  origins (HTTPS and `localhost`); over plain HTTP such a `fetch()` keeps its redirect unless you
  customise `ApiRequestPredicate`;
- requests excluded by `InertiaOptions.RequestPredicate` whose `Accept` header does not list HTML.
  A browser navigation to an excluded path still gets its sign-in redirect.

When an API challenge's redirect is replaced with `401`, cookies that the scheme set during the
challenge (OpenID Connect correlation and nonce cookies) are removed too, because no sign-in
callback will follow. Cookies set earlier in the request are kept.

```csharp
.AddInertiaChallengeHandling(options =>
{
    options.ApiRequestPredicate = context =>
        context.Request.Path.StartsWithSegments("/admin-json")
        || InertiaChallengeOptions.DefaultApiRequestPredicate(context);
    // options.ApiRequestPredicate = null; // only change Inertia visits
});
```

**Forbidden visits.** A forbidden Inertia visit is answered with a bare `403` and is not
redirected to the access-denied page, because that page is usually not an Inertia page. The
client reports a non-Inertia error response through its `httpException` event. Without a handler,
it shows the response body in a modal, which is blank unless the application writes a body. With
[`UseInertiaErrorPages()`](#application-error-pages) the `403` is rendered as the Inertia error page
instead, so the client shows that page rather than a modal. You can also handle it on the client:

```javascript
router.on('httpException', (event) => {
  if (event.detail.response.status === 403) {
    // show a toast or visit an error page
    return false; // suppress the default modal
  }
})
```

**Unsupported: challenges that write a page.** A challenge whose handler writes the response
itself, such as OpenID Connect with `AuthenticationMethod = OpenIdConnectRedirectBehavior.FormPost`,
is left unchanged and does not work for Inertia visits: the client shows the auto-submitting form
in its sandboxed error modal, where it posts inside the frame and most identity providers refuse
to be framed. Use the default redirect (`GET`) authentication method with Inertia.

---

## Validation

`InertiaValidationFilter` automatically serializes `ModelState` errors to TempData
when a controller action redirects with invalid state on a request carrying
`X-Inertia`. Ordinary MVC form submissions keep their normal validation behavior.
The middleware restores the captured errors as the `errors` prop on the next request:

```csharp
[HttpPost]
public async Task<IActionResult> Store([FromBody] CreateUserRequest request)
{
    if (!ModelState.IsValid)
        return RedirectToAction("Create"); // errors auto-forwarded via TempData

    await _userService.CreateAsync(request);
    return RedirectToAction("Index");
}
```

### Minimal API Validation

For Minimal API endpoints, use the endpoint filter and `SetInertiaValidationErrors` extension:

This explicit opt-in also supports ordinary form submissions without `X-Inertia`.
Both validation filters honor `InertiaOptions.RequestPredicate` when configured.

```csharp
app.MapPost("/users", (HttpContext ctx, CreateUserRequest request) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        ctx.SetInertiaValidationErrors(errors);
        return Results.Redirect("/users/create");
    }
    // ...
    return Results.Redirect("/users");
}).WithInertiaValidation();
```

`SetInertiaValidationErrors(...)` also accepts `ValidationProblemDetails` and `ModelStateDictionary`:

```csharp
app.MapPost("/users", (HttpContext ctx, CreateUserRequest request) =>
{
    var problem = new ValidationProblemDetails(new Dictionary<string, string[]>
    {
        ["email"] = ["Email is required"],
    });

    ctx.SetInertiaValidationErrors(problem, bag: "createUser");
    return Results.Redirect("/users/create");
}).WithInertiaValidation();
```

### Error Keys

Every error-producing path (the MVC filter, all `SetInertiaValidationErrors` overloads and
Precognition) normalizes keys the same way, one path segment at a time:

- Member segments of ModelState and supplied keys use the same `PropertyNamingPolicy` that props are
  serialized with, so a CLR member path names the field the page received. Without
  `InertiaOptions.JsonSerializerOptions` that is InertiaNet's camelCase default (`Address.Street` becomes
  `address.street`). With custom options, their policy is used, and a `null` policy renames nothing
  (`Address.Street` stays `Address.Street`), exactly as props keep their CLR names. With a `null` policy,
  System.Text.Json binding errors keep the names the client sent (`$.addresses[0].street` becomes
  `addresses.0.street`), so send CLR-cased field names (`Addresses.0.Street`) if those errors should match
  props and merge with DataAnnotations errors.
- Numeric indexers are kept. Other indexers are dictionary keys and use the configured
  `DictionaryKeyPolicy` (none by default), matching how dictionary props are serialized.
- The `$` root of System.Text.Json binding errors (`$.lines[0].quantity`) is removed. Quoted
  indexers (`$['first name']`) are JSON property names and are written as plain segments without renaming.
- Keys that normalize to the same path have their messages merged. Under the default `Dots` format an
  empty indexer is dropped (`Items[]` becomes `items` and merges with `Items` errors), and a dictionary key
  containing `.` is split like a nested member (`Prices[EUR.USD]` becomes `prices.EUR.USD`).
- Restored keys are written as-is: `DictionaryKeyPolicy` does not rename them, or the error-bag name, a second time.

`[JsonPropertyName]` overrides on input models are not consulted. To make ModelState use JSON names in the
first place, register ASP.NET Core's metadata provider; camelCase and snake_case keys come out unchanged
when normalized again:

```csharp
builder.Services.AddControllersWithViews(options =>
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()));
```

The provider defaults to camelCase, so when `InertiaOptions.JsonSerializerOptions` sets a different
`PropertyNamingPolicy`, pass it the same policy
(`new SystemTextJsonValidationMetadataProvider(JsonNamingPolicy.SnakeCaseLower)`). With a `null` policy, don't register it: it requires a non-null policy, and its camelCase default would
give `email` errors for an `Email` prop.

> **Breaking changes since 3.5.3.** In 3.5.3 the MVC filter camel-cased the whole ModelState key as one name
> (`address.Street`, `values[3].Code`), `SetInertiaValidationErrors` forwarded keys unchanged, and Precognition
> camel-cased every segment whatever policy was configured.
>
> - **Indexers are written as dot paths by default.** `ValidationErrorKeyFormat` defaults to `Dots`, so an
>   indexed field reaches the page as `values.3.code` and a dictionary-keyed one as `prices.EUR`, after a
>   redirect and in a Precognition response alike. This is the form Inertia's form types and Precognition
>   use. If your client reads bracket keys such as `errors['values[3].code']`, switch it to dot paths or keep
>   brackets (now with every segment renamed, `values[3].code`):
>
>   ```csharp
>   builder.Services.AddInertia(options => options.ValidationErrorKeyFormat = ValidationErrorKeyFormat.Brackets);
>   ```
>
>   Empty indexers are dropped under `Dots` (`Items[]` becomes `items` instead of `items[]`, and merges with
>   `Items` errors), and a dictionary key containing `.` becomes ambiguous (`Prices[EUR.USD]` gives
>   `prices.EUR.USD` instead of `prices[EUR.USD]`).
> - **Precognition dictionary-keyed errors use dot paths.** 3.5.3 rewrote only numeric indexers, so
>   Precognition returned `prices[EUR]` and `prices[EurRate].amount`. It now returns `prices.EUR` and
>   `prices.EurRate.amount`, with a configured `DictionaryKeyPolicy` applied to the dictionary key and the
>   props policy to member segments.
> - **Every segment follows the props naming policy.** Nested MVC keys change from `address.Street` to
>   `address.street`. A configured policy now applies (`SnakeCaseLower` gives `first_name`). Custom
>   `JsonSerializerOptions` with a `null` `PropertyNamingPolicy` rename nothing, as for props, so MVC and
>   Precognition errors change from `email` to `Email`, matching an `Email` prop. To keep camelCase keys, set
>   `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` (which camel-cases props too) or leave
>   `JsonSerializerOptions` unset. With a `null` policy, JSON binding errors keep the names the client sent,
>   so send CLR-cased field names, and don't register `SystemTextJsonValidationMetadataProvider` (it requires
>   a non-null policy and defaults to camelCase); when a policy is set, pass the provider the same one.
> - **Minimal API keys are normalized.** Keys passed to the dictionary and `ValidationProblemDetails`
>   overloads of `SetInertiaValidationErrors` are normalized like ModelState keys, so `Address.Street` becomes
>   `address.street`, but hand-written keys that are not CLR member paths are renamed too (`SKU` becomes `sku`,
>   `API_KEY` becomes `apI_KEY`). System.Text.Json renames properties with those names the same way, so the
>   keys still match props serialized with the same policy. Set `ValidationErrorKeyFormat.Preserve` to keep
>   verbatim forwarding.

`ValidationErrorKeyFormat` chooses how indexers are written for errors forwarded across redirects:

| ModelState / supplied key | `Dots` (default) | `Brackets` | `Preserve` |
| --- | --- | --- | --- |
| `Email` | `email` | `email` | `Email` |
| `Address.Street` | `address.street` | `address.street` | `Address.Street` |
| `Values[3].Code` | `values.3.code` | `values[3].code` | `Values[3].Code` |
| `Prices[EUR]` | `prices.EUR` | `prices[EUR]` | `Prices[EUR]` |

`Dots` matches Laravel and the dot paths that Inertia's form types build for nested fields
(`form.errors['values.3.code']`). Precognition responses always use dot paths, because the Precognition
client sends and matches them, so under the default a field has the same key in a Precognition response and
after a redirect. `Brackets` keeps ASP.NET's bracket indexers; with it, the same
field is `values.3.code` in a Precognition response and `values[3].code` after a redirect.
`Preserve` forwards keys exactly as supplied. A `Precognition-Validate-Only` field selects its errors in either
spelling and in any casing (`prices.EUR`, `Prices[EUR]`).

### Error Bags

Scope errors for pages with multiple forms:

```javascript
router.post('/users', data, { errorBag: 'createUser' });
```

## Error Handling

### Status codes

Inertia responses keep the status code already on the response, which is 200 unless something
changed it. Use `WithStatus` to render a page with another status, for example an error page.
It applies to the initial HTML response and to the JSON response for Inertia visits:

```csharp
// MVC
return this.Inertia("Errors/Status", new { status = 404 }).WithStatus(StatusCodes.Status404NotFound);

// Minimal API
app.MapGet("/reports/{id}", (int id, IInertiaService inertia) =>
    inertia.Render("Errors/Status", new { status = 403 }).WithStatus(StatusCodes.Status403Forbidden));

// Minimal API result helper: every InertiaResults.Inertia overload has a statusCode variant
app.MapGet("/archive/{id}", (int id) =>
    InertiaResults.Inertia("Errors/Status", new { status = 410 }, StatusCodes.Status410Gone));
```

`WithStatus` and the `statusCode` overloads accept statuses that can carry a page: 2xx except 204 and
205, and 4xx/5xx. The overloads throw `ArgumentOutOfRangeException` when the result is created.
Redirects and 409 location/version responses are not pages, so keep using `Results.Redirect`,
`inertia.Location(...)` and the middleware for those.

The Inertia client renders any response that has the `X-Inertia` header as a page, whatever its
status. For a status of 400 or above it first fires the cancelable `httpException` event and the
visit's `onHttpException` callback. Returning `false` or calling `preventDefault()` there stops
the page from rendering.

Once an error page renders, the client finishes the visit as it would for any other page: unless the
page has validation `errors`, it fires the `success` event and the visit's `onSuccess` callback, and
flushes the prefetch cache for the page URL. A handler such as `form.post(url, { onSuccess: () =>
form.reset() })` therefore also runs after a 500 page. Check `page.component` there, or cancel the
`httpException` event, if that matters. The Laravel adapter behaves the same way.

### Application error pages

`UseInertiaErrorPages()` renders ASP.NET Core's error responses as an Inertia page. It is opt-in and
packages ASP.NET Core's exception handler and status code pages with an error route:

```csharp
var app = builder.Build();

app.UseInertiaErrorPages(); // before UseInertia
app.UseInertia();
app.MapControllers();
```

- **Status code pages.** Empty 4xx/5xx responses (an unmatched route, `Results.NotFound()`, a 403 from
  authorization) re-execute the pipeline on `/error/{status}`.
- **Exceptions.** Unhandled exceptions re-execute it on `/error/500`. This is off in the Development
  environment by default, so the developer exception page keeps showing exception details there.
- **The error route.** `/error/{status:int:range(400,599)}` is mapped for every method, because
  re-execution keeps the original method and a failed POST arrives as a POST. It renders the `ErrorPage`
  component with a `status` prop and that status, on the initial HTML response and on the JSON response
  for Inertia visits. Statuses outside the range (`/error/302`) get the 404 page instead of failing in
  `WithStatus`.
- **Plain error responses stay plain.** JSON API requests, Precognition requests and paths excluded by
  `InertiaOptions.RequestPredicate` keep ASP.NET Core's responses. API requests are classified by the
  configured `InertiaChallengeOptions.ApiRequestPredicate`, which is
  `InertiaChallengeOptions.DefaultApiRequestPredicate` (they prefer JSON or are a script `fetch()`) unless
  you replaced it, for example in `AddInertiaChallengeHandling`; setting it to null there only turns off
  the challenge translation, and error pages then keep using the default. The check runs on the original
  request, not on the error route.
- **Anonymous access.** The error route allows anonymous users, so a fallback authorization policy does
  not send an anonymous user's 404 on a public page to the sign-in page with `/error/404` as the return
  URL. The page only exposes the status, as Laravel renders its exception pages outside route middleware.

Everything is configurable:

```csharp
app.UseInertiaErrorPages(options =>
{
    options.Component = "Errors/Status";      // default "ErrorPage", as in the Inertia docs
    options.Path = "/errors";                 // default "/error": /errors/{status}, /errors/500
    options.HandleExceptions = true;          // default: false in Development, true elsewhere
    options.AllowAnonymous = true;            // default true: the error route skips authorization
    options.RequestPredicate = context =>     // default InertiaErrorPageOptions.DefaultRequestPredicate
        InertiaErrorPageOptions.DefaultRequestPredicate(context)
        && !context.Request.Path.StartsWithSegments("/webhooks");
});
```

The page component receives the status and can choose its own text:

```vue
<script setup>
defineProps({ status: Number })
</script>
```

Call it once, before `UseInertia()` (and before `UseAuthentication()`/`UseAuthorization()` if those
errors should be rendered too); a second call throws `InvalidOperationException`. An explicit
`UseRouting()` may come before or after it. `Path` cannot be `/`, which would turn top-level URLs such as
`/404` into error pages. The helper is an extension of `WebApplication`; an app built with
`Startup`/`IApplicationBuilder` can use the hand-written pattern below. `ConfigureEndpoint` receives the
error endpoint for rate limiting or other endpoint conventions. It runs after `AllowAnonymous`, so an
authorization requirement added there (`endpoint.RequireAuthorization("Staff")`) only applies when you also
set `options.AllowAnonymous = false`. Pathfinder cannot see the component the helper renders, so declare
its `status` prop on the client yourself.

The page `url`, and the `X-Inertia-Location` of an asset version conflict detected on the error route, is
the URL of the original request, not the error route, so the browser address does not change.

On re-execution the Inertia middleware runs again only when `UseInertia` comes after the error
middlewares and `RequestPredicate` matches the error path. It then calls `Share` again and shares
an empty `errors` prop. When the error was raised after `UseInertia`, the first pass has already
consumed validation errors forwarded from a previous redirect, so they are not on the error page,
while flash data is kept. Laravel's exception
responses leave shared data out by default, so keep error pages independent of shared props where
you can.

Status code pages only replace empty 4xx/5xx responses, so an error response that already has a body,
such as the ProblemDetails body an `[ApiController]` action writes for `NotFound()`, is left alone.
InertiaNet's own 409 responses (asset version conflicts, `Location` redirects, fragment redirects and
the sign-in location visits of [challenge handling](#authentication)) opt out, so they still reach the
client.

The helper is built on this hand-written pattern, which you can use instead when you need more control,
such as per-status props, or when the app does not use `WebApplication`. The sketch is not equivalent: it
has no request selection (JSON API, Precognition and excluded-path requests get the error page too), no
`AllowAnonymous`, no `HandleExceptions` option and no guard against a second registration, and it renders
an `Errors/Status` component rather than `ErrorPage`:

```csharp
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error/500");
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseInertia();
app.MapControllers();

// Map, not MapGet: re-execution keeps the original method, so a failed POST arrives as a POST.
// The range constraint limits the route to error statuses, so /error/302 gets a 404 page
// instead of failing in WithStatus.
app.Map("/error/{status:int:range(400,599)}", (int status, IInertiaService inertia) =>
    inertia.Render("Errors/Status", new { status }).WithStatus(status));
```

When writing it by hand, exclude JSON APIs in the error endpoint (check `context.Request.IsInertia()`
and the `Accept` header) rather than wrapping the error middlewares in `UseWhen`. On `WebApplication`,
when routing runs before the branch (the default: implicit routing, or an explicit `UseRouting()` before
it), a `UseWhen` branch loses the global route builder, so the re-executed request no longer reaches the
error route and every error becomes an empty 404 or the original exception.

### Render failures

Use `HandleExceptionsUsing` to render custom error pages when an exception occurs while an Inertia
response is rendered (for example in a prop callback):

```csharp
builder.Services.AddInertia(options =>
{
    options.HandleExceptionsUsing = (exception, context) =>
        context.RequestServices.GetRequiredService<IInertiaService>()
            .Render("Errors/ServerError", new
            {
                message = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    ? exception.Message
                    : "Something went wrong.",
            });
});
```

An Inertia page returned by the handler is sent with status 500, on the initial HTML response and on the
JSON response for Inertia visits, as Laravel's exception responses carry the real status. Call
`WithStatus` to send another status, for example `.WithStatus(StatusCodes.Status503ServiceUnavailable)`
for a maintenance page; an explicit status always wins. A status the handler assigns itself, as in
`context.Response.StatusCode = 404` before returning the page, is also kept. InertiaNet detects it by
comparing with the status the response had when the handler was called (normally what it had before the
failed render), so assigning that same status is not detected and the page is still sent with 500; use
`WithStatus` for that case. Other results returned by the handler set their own status.

> **Behaviour change.** Before this release a handler page without `WithStatus` kept the status the
> response had before the failed render, normally 200. It is now 500, so the client raises its
> `httpException` event and `onHttpException` callback for it (see [Status codes](#status-codes)). Add
> `.WithStatus(StatusCodes.Status200OK)` to keep the old status, for example in a handler that re-renders
> the page without SSR (`inertia.WithoutSsr()`) after an SSR failure and should still count as a success.
> This includes a page rendered during status code re-execution: if the 404 page's own render fails and
> the handler returns a page, that page now reports 500, not 404.

The handler may also return a normal ASP.NET Core result when that is more appropriate:

```csharp
options.HandleExceptionsUsing = (exception, context) =>
    Results.StatusCode(StatusCodes.Status500InternalServerError);
```

This hook runs for both MVC and Minimal API Inertia responses. Exceptions thrown by the action or
endpoint itself, before an Inertia result runs, are handled by the exception handler above.

---

## Precognition

Precognition lets the frontend validate form data against server-side rules in real-time without submitting the form. When the client sends `Precognition: true`, the server runs model binding and validation only:

- **Valid** → 204 No Content + `Precognition: true` and `Precognition-Success: true`
- **Invalid** → 422 Unprocessable Entity + `Precognition: true`, with `{ message, errors }` JSON

The 422 body has the shape Laravel sends, `{ "message": "The given data was invalid.", "errors": { "email":
["..."] } }`, with `application/json`; the Precognition client reads `errors` from it. InertiaNet writes this
body itself for MVC and Minimal APIs alike, so the application's MVC or Minimal API JSON options
(`AddJsonOptions`, `ConfigureHttpJsonOptions`) rename neither `message`/`errors` nor the error keys, which
already follow the [error key](#error-keys) rules. Being a fixed result rather than an
`UnprocessableEntityObjectResult`, it also skips MVC output formatters and content negotiation. MVC result
filters can read its 422 through `IStatusCodeActionResult`, and Minimal API endpoint filters through
`IStatusCodeHttpResult`, `IContentTypeHttpResult` and `IValueHttpResult`; it is no longer a
`JsonHttpResult`. The Precognition client also requires the `Precognition: true` response header, which
is set before the result runs.

```csharp
[HttpPost]
public async Task<IActionResult> Store([FromBody] CreateUserRequest request)
{
    // Action body never executes for precognition requests —
    // the InertiaPrecognitionFilter short-circuits after validation.
    await _userService.CreateAsync(request);
    return RedirectToAction("Index");
}
```

The MVC filter runs before `[ApiController]` automatic validation responses. It honors `Precognition-Validate-Only` field paths and never executes the action on a validation-only request. Error keys are dot paths (`addresses.0.street`) using the [error key](#error-keys) naming rules, whatever `ValidationErrorKeyFormat` is set to.

Minimal APIs opt in per endpoint or route group:

```csharp
app.MapPost("/users", ([FromBody] CreateUserRequest request) => SaveUser(request))
   .WithInertiaPrecognition();
```

The Minimal API filter validates DataAnnotations on bound DTOs, nested objects and collections before invoking the handler. Ordinary submissions still execute normally; the filter does not replace the application's normal validation flow. Request-body parsing/binding happens before endpoint filters, so malformed payloads may be rejected by ASP.NET before the Precognition filter runs.

---

## Event Hooks

Implement `IInertiaEventHandler` to hook into the render pipeline:

```csharp
public class MyEventHandler : IInertiaEventHandler
{
    public Task OnAfterResolveProps(HttpContext context, Dictionary<string, object?> props)
    {
        // Modify resolved props before the page object is built
        return Task.CompletedTask;
    }

    public Task OnBeforeRender(HttpContext context, InertiaPage page)
    {
        // Inspect or modify the page object before the response is written
        return Task.CompletedTask;
    }
}
```

Register with:

```csharp
builder.Services.AddInertiaEventHandler<MyEventHandler>();
```

Multiple handlers can be registered — they run in registration order.

---

## Vite Tag Helpers

Add `@addTagHelper *, InertiaNet` to `_ViewImports.cshtml`, then use in your layout:

These tag helpers assume your app has Razor view support enabled and that your configured root view renders `<inertia />`.

| Tag | Description |
|---|---|
| `<inertia />` | Renders the root `<div id="app">` and the page data `<script>` |
| `<inertia-head />` | Renders SSR-generated `<head>` tags (meta, title, links) |
| `<vite-input src="..." />` | Renders the correct `<script>` / `<link>` tags for a Vite entry-point |
| `<vite-react-refresh />` | Injects the React Fast Refresh preamble in HMR mode (no-op in production) |

### Vue example

```razor
<inertia-head />
<inertia />
<vite-input src="src/app.ts" />
```

### React example

```razor
<inertia-head />
<vite-react-refresh />
<inertia />
<vite-input src="src/app.tsx" />
```

The `vite-input` tag helper:

- **HMR mode** (when `PublicDirectory/HotFile` exists): injects the Vite dev-server client and the requested module.
- **Production**: reads `wwwroot/build/manifest.json`, resolves the hashed filename, and emits `<script type="module">` for JS entries. It follows static imports for deduplicated stylesheets and module preloads, handles cycles, and leaves dynamic imports unloaded.

`AddViteHelper()` registers a shared parsed-manifest cache, also used for component-existence checks. Every read checks file length and last-write time; deployments invalidate the cache when either changes. Hot mode bypasses it. Set `ViteOptions.CacheManifest = false` if your deployment replaces content while preserving both metadata values.

Production script, stylesheet, and module-preload URLs include the current
request's `PathBase`. For an app mounted at `/portal`, the default asset prefix
is `/portal/build/`. Configure Vite's `base` consistently so its generated imports
and CSS asset references use the same deployment prefix. HMR URLs continue to
use the hot file's dev-server URL without the application's prefix.

### CSP nonces and integrity hashes

Both Vite helpers accept a request-specific `nonce`:

```razor
<vite-react-refresh nonce="@(Context.Items["CspNonce"] as string)" />
<vite-input src="src/app.tsx" nonce="@(Context.Items["CspNonce"] as string)" />
```

Alternatively, supply the nonce through options. The application generates a
fresh nonce for each response and uses the same value in its CSP header:

```csharp
builder.Services.AddViteHelper(options =>
{
    options.NonceProvider = context => context.Items["CspNonce"] as string;
});
```

An explicit helper nonce overrides the provider. It is propagated to generated
script and stylesheet tags, including the inline React refresh preamble and
module preloads. The helpers do not generate a nonce or set a CSP header.

When a manifest entry supplies an `integrity` hash, the matching production
script, stylesheet, or module preload receives `integrity` and
`crossorigin="anonymous"`. CSS entries are matched by their output filename,
including CSS referenced by another chunk. Vite needs a build plugin to generate
these hashes. Missing hashes emit ordinary tags; no request-time hashing occurs.
Set `ViteOptions.IntegrityKey` to a custom manifest property, or to `null` to
disable integrity attributes. Dynamically imported modules remain owned by Vite;
the helper only applies integrity to tags it emits.

### Vite config

Use `laravel-vite-plugin` (or any plugin that writes a `hot` file):

```js
// ClientApp/vite.config.ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import laravel from 'laravel-vite-plugin';

export default defineConfig({
  plugins: [
    laravel({
      input: ['src/app.tsx'],
      publicDirectory: '../wwwroot',
    }),
    react(),
  ],
});
```

The entry paths used by `<vite-input>` remain relative to the frontend workspace root, so a `ClientApp/vite.config.ts` file can still expose entries like `src/app.tsx` or `src/app.ts`.

---

## Asset Versioning

Wire up automatic asset version checking so clients reload when you deploy new assets:

```csharp
builder.Services.AddInertia(options =>
{
    options.Version = ManifestVersionStrategy.FromViteOrMix(
        builder.Environment.WebRootPath);
});
```

`ManifestVersionStrategy` hashes the Vite manifest (or Mix manifest) with xxHash128. When the hash the server sends differs from the version the client has, the middleware returns `409 Conflict + X-Inertia-Location` and the client performs a full-page reload.

---

## Testing

`InertiaNet` ships a testing API modelled after Laravel's `assertInertia` helpers:

```csharp
using InertiaNet.Testing;

// In an xUnit / NUnit integration test
var page = await _client.GetInertiaAsync("/posts");

page.HasComponent("Posts/Index")
    .HasProp("posts")
    .HasUrl("/posts");

var partial = await _client.ReloadInertiaAsync(page,
    new InertiaReloadOptions { Only = ["posts"], Reset = ["posts"] });

page.HasDeferredProp("statistics", "stats");
var deferred = await _client.LoadDeferredPropsAsync(page, ["stats"]);
deferred["stats"].HasProp("statistics");

var redirect = await _client.GetAsync("/stale-assets", HttpCompletionOption.ResponseHeadersRead);
redirect.AssertVersionRedirect().To("/stale-assets");
```

Set the client's default `X-Inertia-Version` header when the tested application requires a known asset version. Reloads carry the asserted page's component and version and support `Only`, `Except`, `Reset`, and `ExceptOnceProps`. Deferred helpers send one partial request per selected group and return each server response; they do not simulate browser prop merging or once-prop expiry. Requests leave the client's default headers unchanged.

`response.AssertInertiaAsync()` remains available for manually sent requests; it requires status 200. Pass the expected status, as in `response.AssertInertiaAsync(HttpStatusCode.NotFound)`, to assert an error page rendered with `WithStatus`. Metadata assertions include `HasMergeProp`, `HasPrependProp`, `HasDeepMergeProp`, `HasMatchingProp`, `HasRescuedProp`, `HasOnceProp`, and `HasScrollProp`. The last two accept an optional complete expected metadata object, including explicit null fields. `HasNullProp` checks an explicit JSON null.

### Development prop diagnostics

```csharp
builder.Services.AddInertia(options =>
{
    options.PropDiagnostics.Enabled = true;
    options.PropDiagnostics.SlowPropThreshold = TimeSpan.FromMilliseconds(100);
    options.PropDiagnostics.MaxEntries = 200;
});
```

Diagnostics run only in the Development environment by default; `DevelopmentOnly = false` explicitly allows other environments. They classify values, callbacks, providers, and wrappers and record resolved/excluded/deferred/optional/cached/rescued/failed/cancelled outcomes. Durations include nested resolution. Debug logs report completed measurements; successful props exceeding the threshold use warning-level logs. No prop values are captured or added to the response. Use `HttpContext.GetInertiaPropDiagnostics()` from `InertiaNet.Diagnostics` to inspect a snapshot in request hooks or local tooling. Collection is bounded by `MaxEntries`, reserving parent measurements before nested values fill the limit.

Entries include the registered route name, deferred group and once-cache key when available. After rendering, `HttpContext.GetInertiaRenderDiagnostics()` provides component/format, success, rendering duration and output bytes before response compression. The byte counter observes writes without buffering or serializing props again, and restores the original response stream on failures. Read completed render metrics from middleware after the endpoint or from diagnostic logs.

---

## System Overview

The repository separates the ASP.NET Core runtime from development-time tooling.
The runtime turns MVC and Minimal API results into Inertia page responses;
Pathfinder reads C# contracts and emits TypeScript; analyzers report supported
source mistakes; templates and the playground show application integration.
[Directory.Build.props](./Directory.Build.props) supplies the shared package
version used by the publishable projects.

## Subdirectories & System Boundaries

- [Source packages](./src/README.md): the runtime adapter, Pathfinder generator,
  Roslyn analyzers, starter templates, and the MSBuild companion package.
- [Samples](./samples/README.md): the React/Vue playground and its server/client
  contracts.
- [Scripts](./scripts/README.md): template staging and installed-package
  verification helpers.
- Test configuration lives in
  [InertiaNet.Tests](./tests/InertiaNet.Tests/InertiaNet.Tests.csproj),
  [InertiaNet.Pathfinder.Tests](./tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj),
  [InertiaNet.Analyzers.Tests](./tests/InertiaNet.Analyzers.Tests/InertiaNet.Analyzers.Tests.csproj),
  and the [TypeScript contract tests](./tests/InertiaNet.Pathfinder.TypeScript/package.json).
  Test-only directories do not receive component contracts.
- [CI](./.github/workflows/ci.yml) composes runtime, generator, analyzer, generated
  TypeScript, browser, template, and installed-package checks.
  [Release documentation](./docs/releasing.md) describes staging and publishing.

## Global Invariants

- The runtime and Pathfinder tool target .NET 8, 9, and 10; the playground,
  starter applications, and tooling test projects require .NET 10. The analyzer
  assembly targets .NET Standard 2.0 for compiler integration.
- MVC and Minimal APIs share the Inertia runtime contract. Middleware prepares
  request state, prop resolution selects and evaluates values, and results/tag
  helpers own response output.
- Pathfinder generation is a development-time operation; application startup is
  not executed to discover contracts. Explicit configuration and endpoint
  manifests cover cases source discovery cannot establish on its own.
- Publishable packages derive their version from the same shared MSBuild
  properties. Template staging replaces `__INERTIANET_PACKAGE_VERSION__` with
  that resolved version before packaging.
- Frontend workspaces live under `ClientApp`; Vite assets are emitted under the
  hosting application's `wwwroot/build` directory. Keep frontend entry paths,
  root views, and asset options aligned.

## Global Side Effects

- Hosted applications write HTTP responses and redirect state through TempData;
  configured authentication and antiforgery flows use cookies. Prop callbacks
  and registered event handlers may perform application-defined work.
- Enabled SSR sends HTTP requests to the configured renderer or Vite bridge.
  Asset helpers and version strategies read local hot files and manifests.
- Pathfinder writes generated TypeScript and tracking files; its watch mode
  observes project inputs. Opting into the MSBuild companion can invoke that
  generation during builds.
- Template staging replaces the supplied staging directory, copies package
  sources, and stamps versions. Package verification installs tools and creates
  temporary projects; frontend builds write generated modules and asset output.
  See each component contract for its precise boundary.

## System Verification

Run the following from the repository root with the SDKs/runtimes and frontend
packages required by the relevant project installed:

```bash
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
dotnet test tests/InertiaNet.Pathfinder.Tests/InertiaNet.Pathfinder.Tests.csproj
dotnet test tests/InertiaNet.Analyzers.Tests/InertiaNet.Analyzers.Tests.csproj
npm --prefix tests/InertiaNet.Pathfinder.TypeScript ci
npm --prefix tests/InertiaNet.Pathfinder.TypeScript test
```

The runtime suite targets all three supported frameworks; add `--framework
net8.0`, `net9.0`, or `net10.0` to select one. Browser and template smoke checks
have additional setup documented in the [playground contract](./samples/Playground/README.md),
[templates contract](./src/InertiaNet.Templates/README.md), and
[CI workflow](./.github/workflows/ci.yml).

---

## License

[MIT](./LICENSE)
