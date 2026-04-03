# InertiaNet Roadmap

## Direction

InertiaNet will be built as a long-term, .NET-first Inertia platform.

That means:

- Keep compatibility with the Inertia v3 protocol and official client adapters.
- Optimize the server-side experience for ASP.NET Core, not for Laravel parity at any cost.
- Treat Laravel as a reference implementation, not as the product model.
- Invest in reliability, testing, typing, tooling, and templates as first-class product features.

The rule going forward is simple:

- Compatibility-first on the wire.
- .NET-first in APIs, tooling, typing, docs, and platform integrations.

## North Star

- Best Inertia experience on ASP.NET Core 10+.
- First-class support for MVC, Minimal APIs, Razor, Vite, SSR, validation, and testing.
- A reliable Wayfinder-style route platform for .NET via `InertiaNet.Pathfinder`.
- Strong documentation and sample apps that match real behavior.
- A platform foundation that can later support analyzers, templates, and richer type generation.

## Non-Goals

- Forking `@inertiajs/core`.
- Inventing a custom Inertia wire protocol.
- Claiming support for patterns that are not covered by automated tests.
- Chasing Laravel feature symmetry when the .NET-native approach is better.

## Current Findings Summary

- Request-scoped `Version` and `RootView` behavior is inconsistent and partially implemented through mutation of shared options.
- JSON customization can currently break the Inertia protocol envelope.
- SSR uses a different serialization path than normal responses and has dead or inconsistent configuration.
- Testing helpers and public docs are out of sync with actual runtime behavior.
- Test coverage is too thin for the current compliance claims.
- Pathfinder's generated API shape is good, but its discovery/runtime layer is too brittle for the breadth of claims in the README.
- Pathfinder `.form()` output and parameter validation behavior do not currently match the documented contract.
- Pathfinder has no dedicated test suite and has known file collision and import generation risks.

## Guiding Principles

- Never sacrifice protocol correctness for configurability.
- Prefer explicit request-scoped state over mutating global options.
- Prefer fewer supported patterns with strong guarantees over broad claims with weak reliability.
- Treat docs and examples as part of the product surface.
- Build long-term platform pieces only on top of a stable core.

## Milestone 1: Core Hardening

### 1. Rendering Pipeline And Request-Scoped State

- [ ] Introduce an internal request render settings object that carries resolved request-scoped values such as `Version`, `RootView`, SSR exclusion state, and any future per-request render flags.
- [ ] Stop mutating `IOptions<InertiaOptions>.Value` inside `src/InertiaNet/Middleware/InertiaMiddleware.cs`.
- [ ] Ensure the version used for mismatch detection in `src/InertiaNet/Middleware/InertiaMiddleware.cs` is the same version serialized into the page object in `src/InertiaNet/Core/InertiaResult.cs`.
- [ ] Ensure root view overrides from `GetRootView()` flow into HTML rendering without touching shared options state.
- [ ] Add integration tests covering custom `GetVersion()` and `GetRootView()` middleware overrides.

### 2. Protocol-Safe Serialization

- [ ] Replace raw `JsonSerializerOptions` replacement in `src/InertiaNet/Core/InertiaOptions.cs` with a safer customization model.
- [ ] Preserve protocol envelope field names and casing for `component`, `props`, `url`, `version`, `flash`, `deferredProps`, `mergeProps`, and the rest of the Inertia page contract.
- [ ] Continue allowing custom converters and safe serializer tuning for prop values.
- [ ] Keep script-tag-safe behavior in `src/InertiaNet/TagHelpers/InertiaTagHelper.cs` while preventing users from breaking the envelope.
- [ ] Rewrite tests in `tests/InertiaNet.Tests/Core/InertiaJsonOptionsTests.cs` so they enforce protocol safety instead of allowing PascalCase envelopes.

### 3. SSR Consistency

- [ ] Make `src/InertiaNet/Ssr/HttpSsrGateway.cs` use the same serializer pipeline as normal Inertia responses.
- [ ] Wire `SsrOptions.ExcludePaths` from `src/InertiaNet/Core/InertiaOptions.cs` into actual SSR exclusion logic in `src/InertiaNet/Core/InertiaResult.cs`, or remove the option if per-request exclusion is the only supported model.
- [ ] Stop hardcoding `wwwroot/hot` in `src/InertiaNet/Ssr/HttpSsrGateway.cs`; reuse `ViteOptions.PublicDirectory` and `ViteOptions.HotFile`.
- [ ] Add integration tests for SSR success, SSR fallback, `ThrowOnError`, configured exclusions, and Vite hot-mode SSR endpoint resolution.

### 4. Runtime Correctness And Test Helpers

- [ ] Fix `AssertVersionRedirect()` in `src/InertiaNet/Testing/InertiaTestExtensions.cs` to match actual version mismatch behavior.
- [ ] Review all testing helpers to ensure they match the protocol implemented by `InertiaMiddleware` and `InertiaResult`.
- [ ] Expand the test assertion surface to make integration tests readable without drifting from real behavior.
- [ ] Keep the testing helpers aligned with actual response semantics before adding more fluent APIs.

### 5. Documentation Truthfulness

- [ ] Update `README.md` testing examples to use the shipped API instead of nonexistent methods.
- [ ] Audit the entire root `README.md` for any claims that are not yet covered by tests.
- [ ] Reduce or qualify the "complete Inertia v3 protocol compliance" language until protocol integration coverage exists.
- [ ] Add a short architecture section explaining middleware flow, prop resolution, flash/error forwarding, and SSR.

### 6. Packaging Hygiene

- [ ] Fix package metadata in `src/InertiaNet/InertiaNet.csproj`, including `RepositoryUrl`.
- [ ] Add NuGet README metadata.
- [ ] Add SourceLink and symbol package support.
- [ ] Review package descriptions and tags so they reflect the compatibility-first, .NET-first positioning.

## Milestone 2: Test Matrix And Release Confidence

### 1. Multi-Target Confidence

- [ ] Change `tests/InertiaNet.Tests/InertiaNet.Tests.csproj` to run across `net8.0`, `net9.0`, and `net10.0`.
- [ ] Add CI coverage across all supported target frameworks.
- [ ] Add CI checks for build, test, packaging, and sample validation.

### 2. Protocol Integration Suite

- [ ] Add `WebApplicationFactory`-based end-to-end tests for initial HTML render.
- [ ] Add `WebApplicationFactory`-based end-to-end tests for XHR Inertia JSON responses.
- [ ] Add tests for version mismatch behavior, external redirects, fragment redirects, flash forwarding, validation forwarding, clear history, preserve fragment, deferred props, once props, merge props, and prefetch behavior.
- [ ] Add tests for Vary header behavior and any required response headers.

### 3. Sample-Driven Verification

- [ ] Add one MVC sample app used by tests or smoke validation.
- [ ] Add one Minimal API sample app used by tests or smoke validation.
- [ ] Use the sample apps to validate real SSR, Vite, validation, and route generation behavior.

## Milestone 3: Pathfinder Hardening

### 1. Contract Corrections

- [ ] Fix `.form()` generation in `src/InertiaNet.Pathfinder/Generation/ActionFileWriter.cs` and `src/InertiaNet.Pathfinder/Generation/RouteFileWriter.cs` so the generated output matches the documented contract.
- [ ] Decide whether `.form()` returns hidden body data or encodes `_method` another way, then apply that consistently across runtime types, generated code, and docs.
- [ ] Fix runtime parameter validation in `src/InertiaNet.Pathfinder/Generation/RuntimeFileWriter.cs` so required parameter checks actually exist and produce clear errors.
- [ ] Update `src/InertiaNet.Pathfinder/README.md` to match the implemented contract exactly.

### 2. Scope Reset For Reliability

- [ ] Reposition Pathfinder as a route-generation platform first.
- [ ] Mark page prop generation, model generation, enum generation, and Wolverine support as experimental until they are properly tested and hardened.
- [ ] Prefer named endpoints as the primary stable contract for .NET route generation.
- [ ] Keep compatibility with Inertia Wayfinder-style objects as the stable frontend contract.

### 3. Route Discovery Reliability

- [ ] Document the exact set of supported route discovery patterns for MVC and Minimal APIs.
- [ ] Improve `src/InertiaNet.Pathfinder/Analysis/MinimalApiRouteDiscoverer.cs` so support is not limited to only the current subset of lambda-based, literal-template patterns.
- [ ] Decide whether the long-term route source of truth should remain syntax-tree based or move toward semantic analysis or endpoint metadata.
- [ ] If remaining syntax-tree based for now, fail clearly on unsupported patterns instead of silently generating partial or wrong output.

### 4. Generated Code Quality

- [ ] Fix missing imports and transitive model discovery for generated TS models.
- [ ] Fix payload/body type generation so unknown complex types do not silently generate invalid TypeScript.
- [ ] Add collision-safe file naming instead of relying only on controller short names.
- [ ] Review naming conflict handling across exports, files, routes, and named route groups.
- [ ] Add deterministic generation guarantees so repeated runs produce stable output.

### 5. Pathfinder Testing

- [ ] Add a dedicated Pathfinder test project.
- [ ] Add golden-file tests for generated actions, routes, `.form()` output, imports, naming conflicts, query parameter behavior, route constraints, and named route layout.
- [ ] Add tests for supported MVC patterns.
- [ ] Add tests for supported Minimal API patterns.
- [ ] Add tests for unsupported patterns so failures are explicit and intentional.

### 6. Build And Watch Integration

- [ ] Improve watch mode beyond the current `FileSystemWatcher` implementation in `src/InertiaNet.Pathfinder/Program.cs`.
- [ ] Add an MSBuild integration story and/or a clean `dotnet watch` integration path.
- [ ] Decide whether Pathfinder should remain CLI-only or also expose build-target integration for local projects.

## Milestone 4: .NET-First Platform Surface

### 1. Minimal API Ergonomics

- [ ] Add first-class Minimal API helpers beyond `MapInertia()`.
- [ ] Introduce a `Results.Inertia(...)` or equivalent typed result surface if the API shape feels right in ASP.NET Core.
- [ ] Add better route mapping helpers for common static page scenarios.
- [ ] Add docs and examples that show Minimal APIs as a first-class programming model, not a secondary port of MVC behavior.

### 2. MVC And Razor Ergonomics

- [ ] Review whether current controller extensions are the right long-term API surface.
- [ ] Improve Razor and TagHelper documentation.
- [ ] Consider stronger view prerequisites validation and clearer error messages when Razor or TempData support is missing.

### 3. Validation And Problem Details

- [ ] Strengthen the bridge between `ModelState`, `ValidationProblemDetails`, and Inertia error bags.
- [ ] Add clearer guidance and helpers for Minimal API validation workflows.
- [ ] Evaluate first-class support for common ASP.NET Core validation/result patterns.

### 4. Error Handling

- [ ] Expand the exception handling surface around `HandleExceptionsUsing`.
- [ ] Add production-ready docs and examples for custom Inertia error pages.
- [ ] Ensure custom exception rendering works consistently in MVC, Minimal APIs, and SSR scenarios.

### 5. Vite And SSR Platform Story

- [ ] Tighten the Vite helper story so it feels native for ASP.NET Core apps.
- [ ] Decide what the official SSR deployment story is for ASP.NET Core users.
- [ ] Add end-to-end docs for development SSR, production SSR, and fallback-to-CSR behavior.

## Milestone 5: Platform Tooling

### 1. Analyzer Package

- [ ] Introduce `InertiaNet.Analyzers` as a future package.
- [ ] Add diagnostics for invalid page component names.
- [ ] Add diagnostics for protocol-breaking serializer configuration.
- [ ] Add diagnostics for unsupported Pathfinder patterns.
- [ ] Add diagnostics for missing page files when page validation is enabled.

### 2. Template And Sample Package

- [ ] Introduce `dotnet new` templates after the core contracts stabilize.
- [ ] Ship at least one official React starter.
- [ ] Ship at least one official Vue starter.
- [ ] Keep sample apps in sync with docs and CI.

### 3. Optional Type Generation Expansion

- [ ] Revisit page prop and model generation only after route generation is stable.
- [ ] Consider a stronger semantic-model-based approach for type generation.
- [ ] Evaluate whether some generation should move toward source generators or analyzers rather than remaining in a CLI-only tool.

## Candidate Package Structure

- `InertiaNet`: core ASP.NET Core adapter.
- `InertiaNet.Pathfinder`: route generation and compatibility-first Wayfinder surface.
- `InertiaNet.Testing`: optional future extraction if the testing API grows enough to warrant a separate package.
- `InertiaNet.Analyzers`: future Roslyn analyzers package.
- `InertiaNet.Templates`: future templates or starter kit packaging.

## Pathfinder Product Positioning

Pathfinder should become the .NET answer to Wayfinder, but with a clear staged rollout.

Stable first:

- Named endpoint and route helper generation.
- Inertia-compatible `{ url, method }` helpers.
- Reliable `.url()` and `.form()` semantics.
- Predictable query parameter and route parameter behavior.

Experimental until hardened:

- Page prop generation.
- Model generation.
- Enum generation.
- Wolverine discovery.
- Broad Minimal API pattern support outside explicitly documented cases.

## Release Gates

Do not advance to a broader public positioning until these are true:

- Core protocol behavior is covered by integration tests.
- README examples compile or are validated against sample apps.
- SSR behavior is covered by tests.
- Pathfinder has a dedicated test suite and deterministic generation.
- There are no known docs/contracts that intentionally differ from runtime behavior.

## Proposed Delivery Order

### Phase A

- Fix request-scoped rendering state.
- Fix protocol-safe serialization.
- Fix SSR consistency.
- Fix testing helper mismatches.
- Correct docs and package metadata.

### Phase B

- Add multi-target integration coverage.
- Add sample-backed verification.
- Reduce compliance claims until coverage supports them.

### Phase C

- Fix Pathfinder `.form()` and parameter validation.
- Add Pathfinder tests.
- Narrow Pathfinder's stable surface to route generation first.

### Phase D

- Improve Minimal API and MVC ergonomics.
- Add error handling and validation polish.
- Strengthen SSR and Vite platform docs.

### Phase E

- Add analyzers, templates, and broader platform tooling.
- Revisit advanced type generation once the route platform is solid.

## Immediate Next Batch

The first implementation batch should focus only on correctness and confidence:

- [ ] Fix request-scoped `Version` and `RootView` flow.
- [ ] Replace unsafe JSON envelope customization with a protocol-safe approach.
- [ ] Align SSR serializer behavior and SSR config usage.
- [ ] Fix `AssertVersionRedirect()` and README testing examples.
- [ ] Add end-to-end integration tests that prove the corrected behavior.

## Strategic Decision Recorded

The project is officially optimized for the long-term .NET-first platform path.

That means we will accept some short-term scope reduction in Pathfinder and some discipline around docs/claims if that is what it takes to make the platform durable.
