# InertiaNet Runtime

## Overview

ASP.NET Core adapter runtime for Inertia.js v3, shared by MVC controllers and Minimal API endpoints. `InertiaNet.csproj` targets .NET 8, 9, and 10 and references the ASP.NET Core shared framework.

## Interface

Application integration starts with `AddInertia`, `UseInertia`, and `IInertiaService` or endpoint/controller helpers. Child contracts describe the public capabilities and internal implementation boundaries.

- [Context](./Context/README.md): Request and sibling-prop context for custom providers.
- [Core](./Core/README.md): Scoped service, page/result contracts, options, typed components, and render events.
- [Diagnostics](./Diagnostics/README.md): Opt-in prop/render measurements and explicit endpoint-manifest export.
- [Extensions](./Extensions/README.md): Service registration, middleware, controller/endpoint helpers, validation, and health checks.
- [Interfaces](./Interfaces/README.md): Prop/provider capability contracts.
- [Middleware](./Middleware/README.md): Request lifecycle, TempData, redirects, Precognition, and history flags.
- [Props](./Props/README.md): Always, optional, deferred, merge, once, and infinite-scroll wrappers.
- [Resolution](./Resolution/README.md): Internal lazy traversal, filtering, and protocol metadata collection.
- [Ssr](./Ssr/README.md): Renderer transport, failure classification, health probes, and telemetry.
- [Support](./Support/README.md): Protocol constants, version hashing, and response-header normalization.
- [TagHelpers](./TagHelpers/README.md): Razor app/page/SSR markup and Vite asset tags.
- [Testing](./Testing/README.md): Public page assertions and HTTP reload/deferred-group helpers.
- [Vite](./Vite/README.md): Internal parsed-manifest reader and cache.

## Invariants

- Shared props, render settings, flash data, and history flags belong to the current request.
- Presence of `X-Inertia` selects protocol JSON; initial requests render Razor HTML with optional SSR. Protocol envelope names remain fixed under application JSON naming policies.
- Props follow request filters, lazy-wrapper capabilities, serializer contracts, and cancellation. Once/merge metadata describes behavior applied by the client.
- SSR, Vite helpers, diagnostics, and manifest export have explicit registration/configuration boundaries. Manifest export does not create a public endpoint.
- The library targets `net8.0`, `net9.0`, and `net10.0` with nullable annotations and a shared implementation.

## Side Effects

Configured runtime execution writes HTTP responses, updates request state and TempData, invokes application factories/providers/hooks, reads component/manifest/hot files, updates memory cache, sends SSR requests, and emits logs/telemetry. Explicit manifest export writes JSON; testing helpers send HTTP requests.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
