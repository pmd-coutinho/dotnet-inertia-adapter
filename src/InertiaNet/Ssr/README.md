# Server-Side Rendering

## Overview

The SSR boundary dispatches pages to a Node/Vite renderer, classifies failures, and exposes optional availability checks and telemetry.

## Interface

- `ISsrGateway.DispatchAsync(InertiaPage, CancellationToken)` returns `Task<SsrResponse?>`; `null` permits client rendering fallback.
- `SsrResponse` carries rendered `Head` and `Body` strings.
- `SsrErrorType`: `BrowserApi`, `ComponentResolution`, `Render`, `Connection`, `Unknown`, and `Timeout`.
- `SsrException` exposes `ErrorType` and `Component` and retains the underlying exception.
- `InertiaSsr.HttpClientName`, `ActivitySourceName`, and `MeterName` use `InertiaNet.SSR`.
- Internal `HttpSsrGateway`, `InertiaSsrHealthCheck`, and `SsrPathMatcher` implement HTTP transport, health probes, and exclusions.

## Invariants

- Disabled SSR returns `null`. Rendering uses the named client with linked request cancellation and the configured timeout.
- A Vite hot file selects `{hot-url}/__inertia_ssr`; otherwise rendering posts to `{SsrOptions.Url}/render` using the adapter's page serializer.
- Successful responses require a string body. Head may be a string, an array of strings joined with newlines, or absent/null.
- Failures fall back by default; `ThrowOnError` produces `SsrException`. Caller cancellation propagates; timeout is classified separately.
- `InertiaResult` applies global and request-local path exclusions before dispatch. Matching normalizes slashes, ignores case, and supports wildcard patterns.
- Health checks probe the dedicated server's configured `/health` endpoint. Disabled SSR is healthy; the Vite render bridge is not used for health probes.

## Side Effects

Performs HTTP POST renders and GET health probes, reads the hot file, logs failures, and emits spans and `inertianet.ssr.renders`/`inertianet.ssr.duration` metrics. It does not start an SSR process.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
