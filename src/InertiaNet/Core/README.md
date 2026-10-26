# Core Runtime

## Overview

The scoped service and MVC/Minimal API result coordinate props, protocol pages, navigation, configuration, and render lifecycle events.

## Interface

- `IInertiaService` accumulates shared props, typed props, once props, flash data, history flags, and SSR exclusions. It creates `Always`, `Optional`, `Defer`, `Merge`, `DeepMerge`, `Once`, and `Scroll` wrappers and supports conditional `When`/`Unless` sharing.
- `Render` accepts component names, objects/dictionaries, or `InertiaComponent<TProps>` contracts. `Location` returns external navigation results.
- `InertiaResult` implements `IActionResult` and `IResult`; `WithViewData` adds Razor-only data.
- `InertiaComponent<TProps>` declares a nonblank component name. `InertiaPropTypeAttribute` supplies a resolved-type generation hint on a property or field.
- `IInertiaEventHandler` exposes `OnAfterResolveProps` and `OnBeforeRender` hooks with default no-op implementations.
- `InertiaPage` contains required component/props/URL plus optional version, shared/deferred/merge/scroll/once/rescued metadata, flash data, and history/large-integer flags.
- `InertiaOptions`, `SsrOptions`, `PagesOptions`, `PropDiagnosticsOptions`, and `ViteOptions` configure runtime behavior.
- Internal service, request-settings, and JSON helpers implement accumulation and serialization; they are not application extension points.

## Invariants

- `IInertiaService` is scoped per request. `Render` snapshots shared props; accumulated flags and flash data remain request-local.
- Execution validates page existence when enabled, resolves props, runs `OnAfterResolveProps`, builds the page, runs `OnBeforeRender`, and writes the selected response. Cancellation comes from `RequestAborted`.
- `X-Inertia` header presence selects `200` JSON with `X-Inertia: true`; initial requests render the root Razor view with optional SSR. Responses vary on `X-Inertia`.
- Middleware-resolved root view/version settings are reused. The page URL includes path base, path, and query string.
- Protocol envelope names remain fixed under application JSON naming policies; null optional fields are omitted. History and large-integer flags are set only when enabled by response construction.
- Application converters retain precedence over the numeric policy. Props and flash use value serialization; protocol metadata bypasses large-integer transport conversion.
- SSR applies only to HTML rendering through a registered gateway, after combining global and request-local exclusions. Page and SSR state are stored in request items for Razor helpers.
- `Location` returns `409` with `X-Inertia-Location` for an active Inertia request, otherwise a standard redirect. `WithoutSsr()` without paths records the current request path.
- Defaults include root view `App`, disabled SSR, a three-second SSR timeout, disabled page-file checks, disabled diagnostics, and enabled Vite manifest caching.
- Prefetch sets private cache-control using a parsed client duration or the configured default. Custom exception handling accepts `IResult`, `IActionResult`, or `null`, prevents recursive handling, and preserves caller cancellation.

## Side Effects

Mutates scoped service state and request items; executes factories, providers, event hooks, and custom exception handlers; writes response headers/body; renders Razor views; optionally dispatches SSR. Version delegates and enabled page checks can read application/file state, and diagnostics can emit logs.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
