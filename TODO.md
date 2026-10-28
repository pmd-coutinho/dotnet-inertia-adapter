# InertiaNet release tracker

Strategy lives in [ROADMAP.md](ROADMAP.md); detailed implementation evidence lives in [implementation progress](docs/implementation-progress.md). This file tracks the current upstream-alignment release.

- [x] Implement adapter protocol, serialization, resolver and validation corrections.
- [x] Implement semantic Pathfinder contracts, safe generation/build/watch behavior and endpoint validation.
- [x] Add rescue, large-integer policies, SSR operations, diagnostics and testing helpers.
- [x] Add a React/Vue CRUD playground with browser coverage for hydration/fallback/custom roots, antiforgery, validation, auth/shared props, flash, deferred loading, scroll and native forms.
- [x] Add CI gates for generated code, installed packages, starter builds and browser integration.
- [x] Complete installed-package/template checks and final regression runs.
- [x] Complete the public documentation/example audit and align the version prefix to Laravel v3.5.2.

Publication follows the completed push through [CI](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/ci.yml) and [automatic NuGet publishing](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/publish-prerelease.yml). Stable packages are version 3.5.3; local development builds now use `3.5.4-alpha.0`, while preview publication assigns `3.5.4-alpha.<publishing workflow run>`. The reviewed Laravel baseline remains 3.5.2 and no longer determines the package patch version.

## Audit follow-up (2026-10-26)

- [x] Scope automatic MVC validation to Inertia requests and add optional request predicates.
- [x] Make CSRF tokens lazy and add an opt-out for empty-response redirects while preserving the default.
- [x] Support Vite PathBase, CSP nonces, and optional manifest integrity hashes.
- [x] Use framework TempData semantics in tests and stop retaining unrelated application values.
- [x] Resolve controller route constants and warn instead of generating unresolved URLs.
- [x] Verify runtime, Pathfinder, and generated TypeScript contracts; build all supported framework assets.
- [x] Advance the local prerelease version to `3.5.2-alpha.13` for the next CI publication.

Supported runtime tests execute on .NET 8, 9 and 10. TypeScript/client fixtures pin Inertia 3.0.2 and 3.9.1; the playground uses 3.9.1. BigInt marker revival is verified with 3.9.1, while string transport is verified with both clients.

## Independent package versioning (2026-10-28)

- [x] Move the next package train to 3.5.3 with local suffix `alpha.0`.
- [x] Track the reviewed Laravel 3.5.2 baseline independently.
- [x] Use the publishing workflow's counter for automatic and manual preview defaults.
- [x] Validate release versions and reject previews of an already stable base.
- [x] Add release-version calculation checks to CI and document stable patch releases.
- [x] Publish stable 3.5.3 from CI-verified commit `63aae75` and advance development to 3.5.4.
