# InertiaNet

A full-featured ASP.NET Core server adapter for [Inertia.js v3](https://inertiajs.com/docs/v3/getting-started).

The first .NET adapter with complete Inertia v3 protocol compliance — including deferred props, merge props, once props, infinite scroll, history encryption, flash data, SSR with Vite hot-mode, fragment preservation, prefetch support, and testing utilities.

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
    options.Pages.Paths = ["src/pages"];                           // source tree (HMR/dev mode)
    options.Pages.ManifestPaths = ["wwwroot/build/manifest.json"]; // build output (production)
});
```

### SSR

```csharp
builder.Services.AddInertiaWithSsr(options =>
{
    options.Ssr.Url = "http://127.0.0.1:13714"; // Node.js SSR server
    options.Ssr.ThrowOnError = false;           // Fall back to CSR on failure
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

// Static route shorthand
app.MapInertia("/about", "About");
```

### MVC Controller

```csharp
public class PostsController : Controller
{
    public IActionResult Index()
        => this.Inertia("Posts/Index", new { posts = _db.Posts.ToList() });
}
```

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

### Error Bags

Scope errors for pages with multiple forms:

```javascript
router.post('/users', data, { errorBag: 'createUser' });
```

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

- **HMR mode** (when `wwwroot/hot` exists): injects the Vite dev-server client and the requested module.
- **Production**: reads `wwwroot/build/manifest.json`, resolves the hashed filename, and emits `<script type="module">` for JS entries plus any associated `<link rel="stylesheet">` CSS chunks.

### Vite config

Use `laravel-vite-plugin` (or any plugin that writes a `hot` file):

```js
// vite.config.ts
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
var page = await response.AssertInertia("Posts/Index");

page.AssertHasProp("posts");
page.AssertPropCount("posts", 3);
page.AssertProp<string>("posts[0].title", t => t.StartsWith("Hello"));
```

---

## License

MIT
