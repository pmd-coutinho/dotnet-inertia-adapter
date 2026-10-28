# InertiaNet roadmap

## Direction

Keep the official Inertia v3 wire contract and provide ASP.NET Core APIs for MVC, Minimal APIs, Razor, Vite, validation and testing. Laravel is the reference implementation for protocol behavior. InertiaNet packages follow their own shared Semantic Versioning sequence; the reviewed Laravel baseline and tested JavaScript client versions are tracked separately. Stable packages are version 3.5.3; the next development prefix is 3.5.4 and the reviewed Laravel baseline remains 3.5.2.

## Shipped foundations

- Request-scoped rendering settings, protocol-safe envelopes and script-safe initial HTML.
- Nested partial/deferred/once/merge/scroll resolution, flash and validation forwarding, cancellation and deferred rescue.
- Typed rendering, serializer-aware props, explicit large-integer transport and matching generated types.
- SSR fallback/timeout/health/telemetry, official SSR markup, custom roots and cached Vite imports/CSS.
- Pathfinder source/project evaluation, semantic DTO/enum/page/shared/request contracts, strict/check modes, safe owned output, build/watch integration and optional registered-endpoint validation.
- Analyzer, template and build-companion packages on the shared version train.
- A [React/Vue playground](samples/Playground/README.md) exercised in a real browser, alongside .NET integration tests and installed-package gates.

The evidence and exact implementation acceptance criteria live in [implementation progress](docs/implementation-progress.md). The original [upstream research](docs/inertia-pathfinder-research-2026-10-23.md) records baseline reproductions and pinned source references. [TODO.md](TODO.md) tracks release follow-through without duplicating this strategy.

## Supported scope

Named routes and the documented action/URL/native-form contract remain the stable Pathfinder surface. Semantic type generation and Wolverine discovery retain their experimental designation. Explicit serializer profiles describe application customization; unsupported converters, polymorphism, custom binders, grouped parameters and ambiguous implicit JSON/service binding produce diagnostics or `unknown`.

The CLI remains the delivery mechanism, with an optional MSBuild companion. Source analysis is supplemented by semantic project/reference metadata and explicit endpoint snapshots. Application startup is owned by the application when exporting a snapshot.

## Further work

- Expand binding/type support through fixtures against real ASP.NET behavior, including grouped parameters and application validation providers.
- Add broader serializer/converter contracts when their wire shape can be represented and verified safely.
- Extend framework/client matrices deliberately as supported versions change.
- Evaluate testing-package extraction only if its size or dependencies justify it.

SignalR schemas, broad ORM entity generation and environment-variable generation remain outside the current focus.

## Release gates

Every release must pass the supported .NET matrix, strict generated TypeScript and executable client contracts, React/Vue browser flows, installed CLI/build integration, template creation/builds and package inspection. Public examples must match shipped APIs. CI publishing is automatic only after the complete CI workflow succeeds; stable publishing remains an explicit manual workflow. See [releasing](docs/releasing.md).
