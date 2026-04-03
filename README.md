# InertiaNet

A compatibility-first ASP.NET Core server adapter for [Inertia.js v3](https://inertiajs.com/docs/v3/getting-started).

Includes support for deferred props, merge props, once props, infinite scroll, history encryption, flash data, SSR with Vite hot mode, fragment preservation, prefetch support, and testing utilities.

**Targets:** .NET 8, .NET 9, .NET 10

---

## Installation

```
dotnet add package InertiaNet
```

---

## Getting Started

### 1. Register services

```csharp
// Program.cs
builder.Services.AddInertia();

// Optionally register the Vite helper (enables <vite-input> tag helpers)
builder.Services.AddViteHelper();
```

### 2. Add the middleware

```csharp
app.UseInertia(); // after UseSession / UseAuthentication
```

### Session & TempData

Flash data and validation error forwarding across redirects require ASP.NET Core session middleware:

```csharp
builder.Services.AddSession();
// ...
app.UseSession();
app.UseInertia(); // must come after UseSession
```

Without session middleware, flash data and validation forwarding will not persist across redirects.

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
    <vite-input src="src/main.tsx" />
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
    options.ExposeSharedPropKeys = true;   // Include sharedProps key list in responses
    options.PrefetchCacheMaxAge = 10;      // Default Cache-Control max-age for prefetch (seconds)
    options.Version = ManifestVersionStrategy.FromViteOrMix(webRootPath); // Asset versioning

    // Page component validation (catches typos during development)
    options.Pages.EnsurePagesExist = true;
    options.Pages.Paths = ["ClientApp/src/pages"];                 // source tree (HMR/dev mode)
    options.Pages.ManifestPaths = ["wwwroot/build/manifest.json"]; // build output (production)
});
```

### SSR

```csharp
builder.Services.AddInertiaWithSsr(options =>
{
    options.Ssr.Url = "http://127.0.0.1:13714"; // Node.js SSR server
    options.Ssr.ThrowOnError = false;           // Fall back to CSR on failure
    options.Ssr.ExcludePaths = ["/admin/*"];  // Skip SSR for matching routes
});
```

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
   Inertia XHR requests receive JSON. Initial page loads render the root Razor view, optionally with SSR markup injected via `<inertia-head />` and `<inertia />`.

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

### Conditional Props

Use `When()` / `Unless()` to conditionally share props:

```csharp
inertia.When(user.IsAdmin, "adminSettings", adminSettings);
inertia.Unless(user.IsGuest, "notifications", async (sp, ct) =>
    await sp.GetRequiredService<INotificationService>().GetAsync(ct));
```

### CSRF/XSRF Token

When `IAntiforgery` is registered, InertiaNet automatically shares an `xsrfToken` prop on every request. Override `ShareCsrfToken` in your middleware subclass to customise or disable:

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

## Analyzers

`InertiaNet.Analyzers` adds Roslyn diagnostics for common InertiaNet and Pathfinder mistakes.

Current diagnostics:

- `INERTIA001` invalid Inertia component names
- `INERTIA002` `JsonSerializerOptions` naming policies that do not affect the Inertia envelope
- `INERTIA003` missing page component files when page validation is enabled
- `PATHFINDER001` Minimal API route templates that Pathfinder cannot resolve statically
- `PATHFINDER002` Minimal API method-group handlers that Pathfinder does not currently support

Add it to your app project like any other analyzer package:

```xml
<ItemGroup>
  <PackageReference Include="InertiaNet.Analyzers" Version="0.1.0-alpha.1" PrivateAssets="all" />
</ItemGroup>
```

## Templates

`InertiaNet.Templates` ships starter templates for React and Vue.

Available templates:

- `inertianet-react`
- `inertianet-vue`

Install and use them:

```bash
dotnet new install InertiaNet.Templates

dotnet new inertianet-react -n MyReactApp
dotnet new inertianet-vue -n MyVueApp
```

Each starter includes:

- an ASP.NET Core app configured with `AddInertia`, `AddViteHelper`, and a root Razor view
- a `ClientApp/` frontend workspace with a minimal entrypoint and `Home` page
- Vite configuration wired to `wwwroot`
- `MapInertia` plus a fallback route for SPA-style navigation

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

## Validation

`InertiaValidationFilter` automatically serializes `ModelState` errors to TempData when a controller action redirects with invalid state. The middleware restores them as the `errors` prop on the next request:

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

### Error Bags

Scope errors for pages with multiple forms:

```javascript
router.post('/users', data, { errorBag: 'createUser' });
```

## Error Handling

Use `HandleExceptionsUsing` to render custom error pages for Inertia requests:

```csharp
builder.Services.AddInertia(options =>
{
    options.HandleExceptionsUsing = (exception, context) =>
        InertiaResults.Inertia("Errors/ServerError", new
        {
            message = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? exception.Message
                : "Something went wrong.",
        });
});
```

The handler may also return a normal ASP.NET Core result when that is more appropriate:

```csharp
options.HandleExceptionsUsing = (exception, context) =>
    Results.StatusCode(StatusCodes.Status500InternalServerError);
```

This hook runs for both MVC and Minimal API Inertia responses.

---

## Precognition

Precognition lets the frontend validate form data against server-side rules in real-time without submitting the form. When the client sends `Precognition: true`, the server runs model binding and validation only:

- **Valid** → 204 No Content + `Precognition-Success: true`
- **Invalid** → 422 Unprocessable Entity with validation errors as JSON

```csharp
[HttpPost]
public IActionResult Store([FromBody] CreateUserRequest request)
{
    // Action body never executes for precognition requests —
    // the InertiaPrecognitionFilter short-circuits after validation.
    await _userService.CreateAsync(request);
    return RedirectToAction("Index");
}
```

The `InertiaPrecognitionFilter` is registered globally and runs before other filters.

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
<vite-input src="src/main.tsx" />
```

The `vite-input` tag helper:

- **HMR mode** (when `PublicDirectory/HotFile` exists): injects the Vite dev-server client and the requested module.
- **Production**: reads `wwwroot/build/manifest.json`, resolves the hashed filename, and emits `<script type="module">` for JS entries plus any associated `<link rel="stylesheet">` CSS chunks.

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
      input: ['src/main.tsx'],
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
// In an xUnit / NUnit integration test
var response = await _client.GetAsync("/posts");
var page = await response.AssertInertiaAsync();

page.HasComponent("Posts/Index")
    .HasProp("posts")
    .HasUrl("/posts");

var redirect = await _client.GetAsync("/stale-assets", HttpCompletionOption.ResponseHeadersRead);
redirect.AssertVersionRedirect().To("/stale-assets");
```

---

## License

MIT
