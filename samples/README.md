# Samples

## Overview

The samples demonstrate complete applications using the runtime adapter and
Pathfinder-generated frontend contracts.

## Interface

- [Playground](./Playground/README.md) hosts equivalent React and Vue todo pages
  in one ASP.NET Core application. Its [controllers](./Playground/Controllers/README.md)
  expose typed page props and request DTOs; its
  [frontend](./Playground/ClientApp/README.md) builds both clients and a Node SSR
  renderer.

## Invariants

- The playground requires .NET 10 and Node 24. Its in-memory store lasts for the
  process lifetime; cookie sign-in supplies a display-name identity for the
  example.
- Server component names, generated Pathfinder props/actions, browser entries,
  and the SSR renderer must agree. The server and contract profile both use
  string transport for large integer IDs.
- Build frontend assets and the SSR bundle before running the renderer or the
  browser integration checks. Native form method overrides run before routing,
  and unsafe MVC requests use the configured antiforgery flow.

## Side Effects

- Running the sample starts HTTP/SSR listeners, writes authentication and
  antiforgery cookies, mutates its shared in-memory todo store, and sends SSR
  requests.
- Frontend generation/build writes TypeScript, browser assets, and the SSR
  bundle. Browser checks start local services and may write test artifacts.

## Verification

From the repository root:

```bash
dotnet build samples/Playground/Playground.csproj
```

Use the [playground verification instructions](./Playground/README.md#verify)
for frontend generation, endpoint-manifest comparison, and browser integration
with Chromium.
