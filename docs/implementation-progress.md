# Research implementation progress

Scope: every finding and proposed addition in [the research report](inertia-pathfinder-research-2026-10-23.md). An unchecked item remains required; this file records implementation and verification rather than replacing the objective.

Release follow-through requested on 2026-10-23: after the implementation and verification are complete, update the README, bump the shared version, commit and push so CI publishes to NuGet. The requested version train mirrors `inertiajs/inertia-laravel`; its latest release was verified as **v3.5.2** (tag commit `4e91cf45cad5ef97862d1ede40fe41616d4e4f91`, published 2026-10-21). Rechecked on 24 October before release: v3.5.2 remains latest. The shared prefix is now `3.5.2`. Existing successful-master-CI publishing produces `<prefix>-alpha.<CI run>`; stable publishing is manual. Publication is recorded by the live [CI](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/ci.yml) and [publishing](https://github.com/pmd-coutinho/dotnet-inertia-adapter/actions/workflows/publish-prerelease.yml) workflows rather than a pre-push completion claim.

## Adapter correctness

- [x] A1: Script-safe initial JSON, including custom converters/options.
- [x] A2: Serializer metadata for typed props, preserving lazy wrappers.
- [x] A3: Compatible matching paths and exclusive merge strategies.
- [x] A4: Official SSR markup and custom root IDs.
- [x] A5: Nested filtering, providers, wrappers, shared keys, deferred/once combinations.
- [x] A6: Shared/nested/custom scroll metadata, groups, reset, pagination.
- [x] A7: Flash/errors/version/redirect/Vary/empty-response lifecycle.
- [x] A8: MVC and Minimal API Precognition with client-compatible payloads and no action side effects.

## Pathfinder correctness and delivery

- [x] P1: Generated actions/routes/forms/models compile under strict TypeScript.
- [x] P2: ASP.NET route grammar, aliases/defaults/catch-alls, parameters, escaping.
- [x] P3: MapInertia, accepted HTTP methods, explicit discovery diagnostics.
- [x] P4: Native forms and direct Inertia submissions against real endpoints.
- [x] P5: Query parameter family replacement and accepted runtime values.
- [x] P6: Semantic serialization-aware DTO/enum/page inference and collision handling.
- [x] P7: Evaluated project inputs, quiet diagnostics, strict/check modes, safe output, feature flags, build invalidation, installed CLI smoke tests.

## Additions and integration

- [x] Real typed rendering surface, component-to-props map, shared-prop module augmentation.
- [x] Distinct request/query/body/form contracts and supported validation metadata.
- [x] Optional endpoint manifest validation against registered ASP.NET routes.
- [x] Deferred-prop rescue and retry metadata, logging and cancellation behavior.
- [x] Large-integer serialization policy and corresponding TypeScript types.
- [x] SSR timeout, named client configuration, health checks and telemetry.
- [x] Vite manifest caching/invalidation and imported CSS coverage.
- [x] Development prop-resolution diagnostics and timing.
- [x] Test-client reload/deferred helpers and metadata assertions.
- [x] CRUD sample, React/Vue browser tests, SSR success/fallback, antiforgery and validation.
- [x] Frontend builds/type checks and executable generated-runtime CI gates.
- [x] Supported .NET/client matrix and installed-package validation.
- [x] Reconcile roadmap/TODO and audit all public documentation/examples.
- [x] SourceLink/build dependency remediation and package inspection.

## Implemented scope and deliberate boundaries

- A4's official SSR-body/root contract has unit coverage and real React/Vue hydration, SSR success/fallback and custom-root browser coverage in the playground.
- Typed rendering, semantic discovery, component maps and shared-prop augmentation are implemented and checked with actual Inertia declarations. Shared props remain optional because source discovery does not prove request-wide execution.
- The semantic graph covers inherited/partial/record/generic and referenced DTOs, selected-framework attributes, nullable elements versus omitted fields, serializer metadata, numeric/string/flags enums and deterministic type/module/export collisions. Output and JSON-input profiles are independent; query/form bindings retain their native names. Unknown converters, polymorphism, custom binders and ambiguous implicit body/service parameters are diagnosed rather than treated as known DTO-shaped wire data.
- P7 evaluates `Compile` items and actual nested/property-based MSBuild imports, with selected framework/configuration symbols, project/package references and linked-file watching. Contract profiles and endpoint snapshots are tracked inputs, including after an initial strict failure in watch mode.
- Explicit body/query/form contracts, named prefixes, collection arguments and standalone multipart files have real HTTP binding fixtures. DataAnnotations provide a documented subset of hints; dynamic validation/custom binder execution remains server-owned and is not advertised as generated client enforcement.
- Registered-endpoint snapshots are explicitly exported from application-owned `EndpointDataSource` instances. Source generation never boots the app. Comparison reports missing/mismatched routes and undiscovered named endpoints, including method/pattern/default/host/component differences.
- Docs have been updated for changed forms/query/enum/TempData behavior and the new runtime APIs. The roadmap/TODO reconciliation and public example audit are complete; concrete generated Vue page interfaces are documented for SFC compiler compatibility.

## Verification log

The checkpoints below are historical. Their remaining-work notes describe that checkpoint, not the final release state. See the final verification entry for current evidence.

- Baseline research: .NET 10 adapter 99, Pathfinder 27, analyzer 11 tests passed; .NET 8/9 runtimes unavailable locally. Generated TypeScript failed compilation as recorded in the research report.

### First implementation checkpoint

- Full solution: **158 adapter tests passed on each of net8.0, net9.0, net10.0**, **40 Pathfinder tests**, **11 analyzer tests**. This includes native HTTP/query/form checks and framework `LinkGenerator` cases.
- Local multi-runtime command: `DOTNET_ROOT=/tmp/inertia-dotnet-runtimes VSTEST_DOTNET_PATH=/tmp/inertia-dotnet-runtimes/dotnet /tmp/inertia-dotnet-runtimes/dotnet test InertiaNet.slnx --no-restore --nologo --verbosity minimal`.
- The temporary runtime root combines official ASP.NET runtimes 8.0.32 and 9.0.21 with the installed SDK/runtime 10.0.401/10.0.12. It changes no system installation.
- `npm test --prefix tests/InertiaNet.Pathfinder.TypeScript`: strict TypeScript 6.0.2, exact optional properties, unchecked indexed access, actual Inertia declarations, and **12 executable JS cases**. Passed against **Inertia 3.0.2 and 3.9.1**. The checked-in lock and current install use 3.9.1; axios is present for the older client's declaration dependency.
- Both starters' updated frontend sources passed their type checks and Vite production builds in `/tmp/inertia-template-checks/{react,vue}/ClientApp`. Package-install/template-creation and browser verification remain open.
- Runtime package packing succeeded for all three TFMs; nupkg/snupkg structure and actual portable-PDB SourceLink records were inspected. The SDK-provided SourceLink remains present after removal of the explicit vulnerable build dependency; restore no longer reports NU1902 for it.
- CI now runs generated-code checks against both pinned client versions and frontend starter type checks/builds. These workflow changes were exercised locally through their underlying commands; hosted CI has not been dispatched.
- The complete objective remains active. Next work is semantic contracts and generator/build delivery, followed by SSR/BigInt/diagnostics and the sample/browser integrations.

### Generator/build and operations checkpoint

- Pathfinder now has **66 passing tests**, including CLI subprocesses, project evaluation without application builds, quiet/strict/JSON/check behavior, ownership/rollback/symlink protections, feature switches, linked-source/configuration watching, and actual MSBuild add/edit/delete/options/missing-output/no-op behavior. Build tests cover project paths containing spaces, quotes, and shell-like text, semicolon filters, relative project paths, and nested external imports.
- Both the tool and build companion were packed and exercised with isolated NuGet and CLI-home directories via `scripts/test-pathfinder-package.cjs`. **.NET 8, 9, and 10** installed-tool startup, generation/check, real ASP.NET builds, no-op builds, missing-output repair, and the default local-tool command passed. CI now includes this package gate.
- Strict generated-TypeScript compilation now covers the default output plus disabled-model, disabled-enum, and route-only variants. The 12 executable route/runtime tests pass against the current 3.9.1 install.
- SSR has a three-second default timeout, public named-client configuration, an optional health-check registration, preserved request cancellation, classified server errors, and ActivitySource/Meter telemetry. Tests cover these behaviors and malformed success payloads.
- Vite caches parsed manifests through the shared memory cache, invalidates on length/mtime changes, and traverses static imports for deduplicated CSS and module preloads. Tests cover cycles, dynamic-import exclusion, cache opt-out, file deletion, separate manifests, hot/production transitions, and DI activation.
- The expanded adapter suite passed **185 tests on each of .NET 8, 9, and 10**, alongside 66 Pathfinder and 11 analyzer tests (**632 .NET executions**). Subsequent test-client work is being verified separately before the next full rerun.
- Test-client helpers now issue real partial/deferred-group requests, preserve client defaults and page version, and expose merge/match/scroll/once/rescue/null assertions. Tests exercise real TestServer routes, filters, reset behavior, deferred group selection and no-request failure for unknown groups. Response assertions now reject missing/invalid content types and malformed/null page contracts consistently.
- Development prop diagnostics are opt-in, restricted to Development by default, bounded per resolution, and available as request snapshots plus debug/slow-prop logs. Tests cover nested paths, wrapper/callback classification, exclusions, cached once props, rescue/failure/cancellation, entry limits and value-free logging.
- Final checkpoint rerun: **200 adapter tests on each of .NET 8, 9, and 10**, **66 Pathfinder tests**, and **11 analyzer tests** passed (**677 .NET executions**). Strict TypeScript compilation (including all feature-switch variants) and all **12 JS runtime cases** passed against **Inertia 3.0.2 and 3.9.1**. The current npm install and checked-in lock are back on 3.9.1. `git diff --check` passed.
- The overall objective remains incomplete: semantic DTO/page/request contracts, BigInt policy, sample/browser integration, final documentation/matrix work, and the requested version bump/push/publishing verification are still required.

### Semantic contracts and endpoint validation checkpoint

- Adapter correctness/operations were committed as `7b292bd`; serializer-owned literal names and dictionary-policy handling were committed as `3c01909`; explicit large-integer policies and exact-value test assertions were committed as `a33519b`; the registered-endpoint export API was committed as `689f118`.
- The CLI builds an in-memory Roslyn symbol graph without emitting/loading application assemblies. It uses restored compile references and the selected target framework's reference packs; tests distinguish .NET 8, 9 and 10 attribute availability even when the tool runs on .NET 10.
- Serialization-aware fixtures are compared with actual adapter JSON and native ASP.NET JSON/query/multipart binding. Input aliases are type aliases compatible with `router.visit(..., { data })`. Custom CLR types/attributes with framework-like names retain their actual symbol contracts.
- Strict TypeScript covers colliding namespaces, case/punctuation variants, file/directory barrel shadowing, helper/payload/validation names, reserved map names, prototype-named enum/validation fields, nullable collections and each feature-switch variant. Executable tests verify that colliding routes remain reachable.
- Large-integer modes cover Int64, UInt64, BigInteger, nullable/nested/lazy/shared values, enums, numeric dictionary keys and flash. Explicit converters/number handling retain precedence; decimals and protocol timestamps retain their representation. Actual JSON, initial HTML and SSR requests are parsed with Inertia 3.9.1's codec; history/request reserialization retains digits. The legacy 3.0.2 gate verifies string transport and explicitly excludes unsupported marker revival.
- The combined solution passed **218 adapter tests on each of .NET 8, 9 and 10**, **102 Pathfinder tests**, and **11 analyzer tests** (**767 .NET executions**). This includes actual application-exported manifests, CLI strict failure/last-good output, build invalidation and watch recovery for both local and external JSON snapshots. Strict TypeScript and all **14 executable route/protocol tests** passed with the final fixtures on **Inertia 3.0.2 and 3.9.1**. The current install and checked-in lock use 3.9.1.
- Remaining required work: the CRUD/browser integration (React/Vue, SSR hydration/fallback/custom roots, antiforgery, validation, flash/shared/auth/scroll), current installed-package/template/matrix gates, final documentation/roadmap reconciliation, and version bump/push/CI publishing verification. Keep the goal active and do not push an incomplete checkpoint.


### Final local release verification — 24 October 2026

- All research findings A1–A8, P1–P7 and proposed additions above are implemented. Unsupported dynamic serializers/binders/validators remain explicitly diagnosed within the documented scope, rather than inferred as known contracts.
- Final .NET suite: **221 adapter tests per target (.NET 8/9/10)**, **106 Pathfinder tests**, **12 analyzer tests**, totaling **781 executions**. The directional-ignore fixture compares .NET 10 serializer behavior with actual adapter output and JSON input; endpoint matching is checked against real case-insensitive ASP.NET requests.
- Strict TypeScript 6.0.2, all feature-switch variants and **14 executable generated-route/protocol tests** passed against both **Inertia 3.0.2 and 3.9.1**. The committed lock uses 3.9.1.
- **Four Playwright tests passed**, covering React and Vue CRUD, ordinary/Precognition validation without side effects, real antiforgery headers and rejection, auth/shared props, flash, deferred props, infinite scroll, native forms, script-terminator data, SSR hydration, CSR fallback and custom roots. Registered-endpoint export plus strict generation/check also passed for the actual playground.
- Render diagnostics now include route names, deferred groups, once keys, outcome, render duration and actual UTF-8 output bytes. Diagnostics never re-evaluate prop callbacks; browser tests ran with development diagnostics enabled.
- All **five packages** packed as `3.5.2-alpha.1` for local verification. Package contents, embedded framework assets, icons, template version substitution and portable-PDB SourceLink records were inspected. The runtime and CLI contain the supported .NET 8/9/10 assets; the analyzer targets netstandard2.0. SourceLink remains SDK-provided, without the old vulnerable explicit build dependency.
- Freshly packed Pathfinder and build companion passed isolated install/startup, strict generation/check, actual SDK.Web builds, unchanged-build no-op, deleted-output repair and default local-tool installation/restore/execution on **.NET 8, 9 and 10**.
- The packed templates were installed into an isolated CLI home. Both generated starters passed dependency installation, strict type checking, Vite production builds, isolated local-feed restore and .NET builds. Both actual running apps returned HTML with Razor roots/assets, readable antiforgery cookies and valid Inertia JSON with the `Home` component.
- README, Pathfinder documentation, release instructions, roadmap and TODO were audited and updated. Changes are separated into implementation, integration and documentation commits. Hosted CI and automatic NuGet publication are the remaining external release gates; their authoritative results are linked above.
