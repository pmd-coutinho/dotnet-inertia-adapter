# InertiaNet and Pathfinder: upstream comparison and improvement plan

Implementation and verification follow-through: [implementation progress](implementation-progress.md). Findings below describe the original baseline.

Research date: 23 October 2026. Local revision: `03165be79cecec09518951c09e6c91ef80f8a236`.

## Recommendation

Invest first in protocol correctness and generated TypeScript correctness. The adapter already implements most of the major Inertia concepts, but several edge cases produce incompatible responses, and even a simple generated GET route fails TypeScript checking with default form generation enabled.

After those fixes, the most useful expansion is a shared C# contract pipeline for routes, request DTOs, page props, and shared props. Keep named endpoints as the durable frontend contract and keep the CLI/build integration. This agrees with the existing `docs/pathfinder-future-direction.md`; it does not require replacing the tool with a source generator.

This is a research report, not an implementation. Findings below distinguish executed reproductions, source inspection, and proposed features. Priority labels mean P1 = address before expanding/recommending the affected feature; P2 = next hardening work; P3 = follow-up improvement.

## Scope and evidence

Reviewed the runtime adapter, prop wrappers/resolver, middleware, Razor/Vite helpers, SSR gateway, testing helpers, Pathfinder discovery/generation/runtime, analyzers, templates, CI, and the existing roadmap.

Compared against these pinned upstream snapshots:

| Source | Branch / revision | Purpose |
|---|---|---|
| [inertia-laravel](https://github.com/inertiajs/inertia-laravel/tree/1e8706260b975c1c652dd886a53657e0fae2102d) | `3.x`, `1e870626…` | Server behavior and upstream regression cases |
| [Inertia client](https://github.com/inertiajs/inertia/tree/348b01aae8d8da2df3227dec20bc4607e51d5d62) | `3.x`, `348b01aa…` | Actual wire contract and SSR HTML generation |
| [Wayfinder](https://github.com/laravel/wayfinder/tree/81df21e16dc161aa2936188b8a65720760f62119) | `main`, `81df21e1…` | Current route/form generation reference |
| [Wayfinder next](https://github.com/laravel/wayfinder/tree/be273f802dbcc3c9a4a411ab1eea7586b922f7c9) | `next`, `be273f80…` | Experimental direction; not the current main-branch API |

These are source snapshots, not a claim that every inspected change is in a released package. The inspected Laravel changelog lists `v3.5.2` on 21 October. Its `v3.3.4` entry specifically records escaping HTML in initial-page JSON; `v3.5.0` adds BigInt preservation. [Release notes](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/CHANGELOG.md).

Executed checks:

- Adapter tests on .NET 10: **99 passed**.
- Pathfinder tests on .NET 10: **27 passed**.
- Analyzer tests on .NET 10: **11 passed**.
- The solution-wide test command compiled the projects, but its .NET 8 and .NET 9 test hosts could not start because those runtimes are absent here. Those targets have not been runtime-validated by this review.
- Separate temporary .NET probes exercised the resolver, real response serialization, tag helper output, middleware, and a TestServer endpoint. Pathfinder was run as its actual CLI, with `DOTNET_ROLL_FORWARD=Major` because the tool targets .NET 9.
- Generated TypeScript was checked using TypeScript **6.0.2**, matching the starter's declared compiler version. Emitted JavaScript was also exercised with Node **24.21.0** to inspect runtime behavior despite the compiler errors.
- Upstream PHP suites and a complete React/Vue browser application were not run. Browser/hydration consequences below are supported by the HTML and client source, rather than a full browser reproduction.

Temporary reproduction projects and output were created under `/tmp/inertia-review-probes`; production source was left unchanged.

## What already exists and is worth keeping

| Area | Current strengths | Next investment |
|---|---|---|
| Runtime | Deferred, optional, always, once, merge, scroll, shared props | Correct combinations and nested behavior |
| Protocol | Versioning, redirects, history flags, flash, partial reloads | Match actual client data structures and lifecycle |
| ASP.NET integration | MVC and Minimal API results; request-scoped render settings | Consistent serialization and validation across both styles |
| SSR | HTTP gateway, hot mode, exclusions, fallback and error classification | Correct SSR markup, timeout and health-check ergonomics |
| Pathfinder | Named routes, action helpers, defaults, query support, forms, diagnostics | Compile and execute output as part of CI |
| Tooling | Dedicated tests, golden files, watch mode, build package, analyzers | Stronger contracts, build inputs, strict generation |
| Packaging | Multi-target adapter, templates, shared package versions | Test the installed tool and real frontend builds |

The largest opportunity is stronger guarantees around existing functionality. A longer feature list would currently hide these gaps.

## Adapter findings

### A1 — P1: initial HTML allows a prop string to terminate its JSON script

**Executed reproduction.** `InertiaJsonOptions.GetTagHelperOptions()` explicitly selects `UnsafeRelaxedJsonEscaping`. `InertiaTagHelper` inserts the resulting JSON into a script element with `AppendHtml`.

A harmless marker value of `</script><div id=probe>marker</div>` survived literally in the output. HTML parsing recognizes the closing script tag even when its type is `application/json`. With attacker-controlled prop content, this creates a script-injection path; the probe only inspected markup and did not execute a payload.

Local entry points: `src/InertiaNet/Core/InertiaPageJsonConverter.cs:104` and `src/InertiaNet/TagHelpers/InertiaTagHelper.cs:55`.

**Change:** make script-safe escaping a hard guarantee of HTML embedding, including when user JSON options are supplied. Keep raw JSON semantics so the client can still parse the script contents. Add cases for closing script tags, mixed casing, HTML comments, Unicode, and SSR fallback.

Laravel uses `JSON_HEX_TAG` in its [directive](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/Directive.php); the client also escapes `<` in its [SSR body builder](https://github.com/inertiajs/inertia/blob/348b01aae8d8da2df3227dec20bc4607e51d5d62/packages/core/src/ssrUtils.ts). Microsoft documents the HTML-sensitive characters left unescaped by [UnsafeRelaxedJsonEscaping](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodings.web.javascriptencoder.unsaferelaxedjsonescaping?view=net-10.0).

### A2 — P1: top-level typed props bypass JSON property attributes

**Executed reproduction.** `InertiaService.ObjectToDictionary()` reflects over public properties and camel-cases their CLR names before the JSON serializer sees the containing object.

```csharp
public class ProfileProps
{
    [JsonIgnore] public string Secret { get; set; } = "hidden";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "Ana";
}
```

Rendering this object emitted `{"secret":"hidden","displayName":"Ana"}` under `props`. `[JsonIgnore]` did not protect the property and `[JsonPropertyName]` did not rename it. This is a concrete information-exposure risk when an application relies on serializer attributes for a top-level props DTO.

Local: `src/InertiaNet/Core/InertiaService.cs`, `ObjectToDictionary`.

**Change:** define one explicit serialization contract for typed props, respecting supported property metadata while preserving lazy prop wrappers. Avoid solving this by eagerly JSON-round-tripping all props, which would destroy callbacks/wrappers. Share this contract with Pathfinder's DTO generation; diagnose custom converters whose output cannot be inferred.

### A3 — P1: `matchPropsOn` has an incompatible wire shape

**Executed reproduction, verified against client source.** A merge prop using `.MatchOn("id")` produces:

```json
{"matchPropsOn":{"items":["id"]}}
```

The client expects `string[]`, such as `["items.id"]`, and calls `.find()` on it when merging arrays. The current object therefore causes a client runtime exception on the relevant merge path. This affects a feature beyond simply losing deduplication.

Local: `PropsResolver.cs:33`, `PropsResolver.cs:385`, `InertiaPage.cs`, and `InertiaPageJsonConverter.cs:26`.

**Change:** represent matching paths as a flat string list throughout resolution, page DTO, serialization, and assertions. Verify root arrays and nested paths such as `users.data.id` against the [client merge implementation](https://github.com/inertiajs/inertia/blob/348b01aae8d8da2df3227dec20bc4607e51d5d62/packages/core/src/response.ts#L543).

Also reproduced: `.DeepMerge()` currently advertises both `mergeProps` and `deepMergeProps` for the same property. Make merge strategy emission exclusive, following the [upstream metadata collector](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/PropsResolver.php).

### A4 — P1: standard SSR output is wrapped in a second app root

**Executed HTML reproduction; browser hydration not run.** The official client SSR body contains its own JSON script and root div. InertiaNet treats the entire body as inner content and adds another `<div id="app">` plus another page script.

The probe using an official-format SSR body produced **two `id="app"` elements**. This also hides the inner root's `data-server-rendered` marker from code looking up the outer element.

Local: `src/InertiaNet/TagHelpers/InertiaTagHelper.cs:52`.

**Change:** render successful official SSR bodies directly; generate the app root and page JSON only for CSR/fallback. Validate the custom root-ID contract too. Add real React/Vue SSR smoke tests before calling the integration complete. See [buildSSRBody](https://github.com/inertiajs/inertia/blob/348b01aae8d8da2df3227dec20bc4607e51d5d62/packages/core/src/ssrUtils.ts) and Laravel's [render directive](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/Directive.php).

### A5 — P2: partial filtering and wrapper resolution differ from upstream

**Executed reproductions:**

| Case | Observed behavior | Intended behavior |
|---|---|---|
| `only=a,b`, `except=b` | Both `a` and `b` returned | Include `a`, exclude `b` |
| `except=auth.password` | Entire `auth` object removed | Keep its other children |
| Delegate returns `DeferProp` | Deferred callback runs on the initial response; metadata is lost | Reapply initial exclusion to the returned wrapper |
| Initial `DeferProp.Merge().Once()` | Only deferred metadata survives | Preserve the appropriate merge/once metadata too |
| Shared key `auth.user` | `sharedProps: ["auth.user"]` | Advertise the deduplicated top-level key `auth` |

Local: `src/InertiaNet/Resolution/PropsResolver.cs`, particularly `ShouldIncludeInPartialResponse`, `PathMatches`, `UnwrapPropTypeAsync`, and `ShouldExcludeFromInitialResponse`.

**Change:** separate inclusion traversal from exclusion matching. Reassign the effective wrapper after callbacks return another prop type, reapply filtering, and collect metadata from that wrapper. Model these as resolver invariants rather than isolated special cases.

Upstream has targeted cases for these behaviors in [PropsResolverTest.php](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/tests/PropsResolverTest.php). Port their scenarios into .NET tests with attribution; adapting the tests is more useful than translating implementation mechanically.

Additional source-inspected cases to cover in that work: nested property providers receive a full property path; an `AlwaysProp` returning a dictionary should retain its always-included semantics for children; generic/read-only dictionaries and typed nested containers need an explicit supported policy.

### A6 — P2: scroll metadata works only for a narrow case

**Executed response reproduction:** a direct page-level `ScrollProp` with concrete `ScrollMetadata` serialized correctly. All three of these serialized as `null`:

- A scroll prop registered through `Share()`.
- A nested scroll prop at `nested.items`.
- A scroll prop whose provider implements `IProvidesScrollMetadata` without being the concrete `ScrollMetadata` class.

The backfill receives only the original page dictionary, looks up a full dotted path as a flat key, and casts the provider to one concrete class. The emitted metadata also lacks the reset state included upstream. Separately, source inspection shows `ScrollProp.Defer(group)` ignores its `group` argument.

Local: `PropsResolver.cs:407`, `InertiaResult.cs:88`, `Props/ScrollProp.cs`, and `Props/ScrollMetadata.cs`.

**Change:** collect scroll metadata while resolving the actual prop instance, using the interface methods. Preserve custom groups and reset state. Exercise shared, nested, cursor-based, and custom-provider pagination. Use the [upstream ScrollProp](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/ScrollProp.php) as the reference, without importing Laravel paginator dependencies.

### A7 — P2: redirect, flash, errors, and versioning need lifecycle tests

**Executed reproductions:**

- Restored flash is removed from TempData before a version conflict returns `409`. It is absent from TempData for the following full-page request. This affects restored errors and navigation flags through the same ordering too.
- Nested flash objects become JSON **strings** after restoration rather than preserving their JSON object shape.
- A configured server version with no client version header proceeds to the endpoint. Upstream checks that mismatch too.
- A PUT endpoint returning `307` is rewritten to `303`. The middleware currently rewrites every redirect status for every non-GET request, including deliberately method-preserving redirects.
- Successful requests do not automatically share an empty `AlwaysProp` for `errors`. On a partial response, previously merged errors can therefore persist when the server has no new errors to send. The missing empty prop was reproduced; the stale-client-error consequence follows from the client's prop merge behavior.

Local: `src/InertiaNet/Middleware/InertiaMiddleware.cs` and `src/InertiaNet/Extensions/HttpRequestExtensions.cs`.

**Change:** preserve consumed transient state on version conflicts; retain nested flash values as cloned `JsonElement`s or equivalent structured values; always share `errors`; narrow redirect conversion deliberately. Normalize `Vary` case-insensitively while preserving existing values. The current early version response bypasses the later `OnStarting` registration, and existing non-Inertia `Vary` values are not appended by that middleware path.

Also source-confirmed: `OnEmptyResponse` changes `204` to an empty `200`; upstream handles a truly empty `200` with a back redirect. Choose and test a client-compatible empty-response policy rather than claiming those behaviors are equivalent. Keep streamed responses out of generic empty-body handling. [Upstream middleware](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/Middleware.php).

### A8 — P2: Precognition is a partial implementation

**Source inspection; full client integration not run.** The filter skips the MVC action and returns 204/422, but it omits the `Precognition: true` response header, sets `Precognition-Success: true` even on failure, returns a bare field dictionary for 422, and does not handle field-selection headers. There is no equivalent Minimal API Precognition filter. The existing unit test checks the failure result type but not its body contract.

Inspected the published `laravel-precognition@2.1.0` package, matching the inspected Inertia client's dependency baseline: its `validatePrecognitionResponse` requires the `precognition` response header, and its validator reads errors from `response.data.errors`. This confirms concrete incompatibilities in both response recognition and the failure payload, beyond missing conveniences.

Local: `src/InertiaNet/Middleware/InertiaPrecognitionFilter.cs`.

**Change:** verify the exact contract against the pinned `laravel-precognition` client used by the supported Inertia release. Add success/failure, field selection, error envelope, and side-effect-prevention tests through MVC and Minimal APIs. Test MVC automatic validation/filter ordering explicitly. Do not advertise full interoperability based only on 204/422 status codes.

## Pathfinder findings

### P1 — P1: ordinary generated code does not type-check

**Executed with the CLI and TypeScript 6.0.2.** A single `app.MapGet("/home", () => "Home").WithName("Home")` is enough to reproduce:

```text
error TS2339: Property 'get' does not exist on type '... => FormDefinition<"get">'.
error TS2339: Property 'head' does not exist on type '... => FormDefinition<"get">'.
error TS18049: 'value' is possibly 'null' or 'undefined'.
```

The writers attach extra properties to `route.form` after assigning it as a property on another function. TypeScript does not infer these nested function-property additions as the generated code assumes. The runtime also admits nullable array entries but dereferences them unconditionally.

With a body DTO containing an enum, the generated model additionally fails with `Cannot find name 'Status'`. Boolean route arguments expose another narrowing error.

Local: `Generation/ActionFileWriter.cs`, `Generation/RouteFileWriter.cs`, `Generation/RuntimeFileWriter.cs`, and `Generation/ModelFileWriter.cs` under `src/InertiaNet.Pathfinder`.

**Change:** construct a separately named form helper with all verb properties before assigning it to the route, or explicitly type the full callable contract. Centralize the route/action emitter so fixes apply to both. Compile generated fixtures under `strict`, `exactOptionalPropertyTypes`, and `noUncheckedIndexedAccess`, and test imports with the actual Inertia packages. Snapshot equality alone is insufficient.

The existing Wayfinder [method template](https://github.com/laravel/wayfinder/blob/81df21e16dc161aa2936188b8a65720760f62119/resources/method.blade.ts) already demonstrates assigning a separately assembled form function.

### P2 — P1: route grammar and bindings can generate unusable URLs silently

**Executed CLI/runtime reproductions:**

| Valid input / call | Current generated behavior |
|---|---|
| `{id:int?}` | Loses optionality; omitted argument throws |
| `{id:int=1}` | Loses the route default; omitted argument throws |
| `{**path}` | No argument generated; literal `{**path}` remains in the URL |
| `{id}` bound to `int id` | Argument is typed as `string` |
| `[FromRoute(Name="id")] int value` | Replaces `{value}`, leaving `{id}` unresolved |
| `{locale}` in the route, absent from action arguments | Placeholder remains even with a global locale default |
| `[HttpGet("~/absolute")]` on an `items` controller | Produces `/items/~/absolute` |
| `{enabled:bool}` with scalar `true` / `false` | Type advertises booleans, but both scalar calls throw |
| Slug `a/b?c#d` | Embedded unchanged; separators change URL structure |

Local: `Analysis/RouteTemplateParser.cs:7`, `Analysis/ControllerRouteDiscoverer.cs:298`, and both route writers.

**Change:** derive parameters from the route pattern first, enrich from bindings second, and distinguish “no constraint-derived type” from an actual string constraint. Use ASP.NET Core's `RoutePatternFactory.Parse`/route-pattern model where practical rather than growing the current regex. Preserve catch-all kind, defaults, optionality, and binding aliases in the intermediate representation. Encode ordinary path segments and explicitly implement the differing catch-all rules. Do not change strings blindly for host parameters.

Validate generated URLs against ASP.NET `LinkGenerator` for the supported subset. ASP.NET documents constrained optional/default parameters and the different escaping behavior of `*` and `**` in [routing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0). These are .NET-specific semantics that copying a PHP URL writer will not supply.

### P3 — P2: discovery misses this adapter's own shorthand and invents Minimal API HEAD support

**Executed:** a fixture with `MapGet("/home", ...)` and `MapInertia("/about", "About")` generated only one route, without an unsupported-pattern warning for `MapInertia`.

The Minimal API method table also automatically emits `head` for `MapGet`. A real .NET 10 TestServer endpoint registered only with `MapGet` returned **405** to HEAD. The generated helper therefore advertises a method the endpoint does not accept.

Local: `Analysis/MinimalApiRouteDiscoverer.cs:9`; `Extensions/EndpointRouteBuilderExtensions.cs` in the runtime adapter.

**Change:** add `MapInertia` explicitly and extract its component metadata as a future typing hook. Add explicit `MapMethods`, HEAD, and OPTIONS support where statically resolvable; keep actual methods faithful to ASP.NET. Keep fallbacks separate from concrete named page routes. Cross-file handlers and extension-method endpoint registration belong in the semantic or optional runtime-metadata expansion already anticipated by the design document.

### P4 — P2: `.form()` needs an explicit ASP.NET integration contract

**Executed output, source-inspected consumption.** Updating item 42 generates:

```ts
{ action: '/items/42', method: 'post', data: { _method: 'put' } }
```

Spreading this object into `<form>` or Inertia `<Form>` does not turn `data._method` into a submitted field. The README's manual hidden-input example can work with the appropriate server setup; the broad direct-use compatibility claim needs qualification.

Laravel's form helper puts spoofing into the action URL; copying that alone also fails with stock ASP.NET method override, which does not read a query parameter. ASP.NET's [override middleware](https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Middleware/HttpOverrides/src/HttpMethodOverrideMiddleware.cs) reads a configured form field or a header, and the [default options](https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Middleware/HttpOverrides/src/HttpMethodOverrideOptions.cs) do not select `_method` automatically.

**Change:** preserve direct `{url, method}` usage for Inertia requests. For native forms, provide a documented hidden-field helper/component and configure `UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" })` before routing; emit uppercase HTTP method names. If direct spread compatibility is a requirement, design and test a dedicated integration rather than relying on an ignored extra property.

Use a real PUT-only endpoint to test both Inertia submissions and native form submissions. Do not infer that one working path proves the other.

### P5 — P2: query merging leaves stale filter parameters

**Executed against generated JavaScript.** Starting with `?filter[a]=1&filter[b]=2&filter[c]=3&tags[]=old`:

- `mergeQuery: { filter: null }` leaves every nested filter entry.
- Replacing `filter` with a new object leaves `filter[b]=2` behind because the loop deletes while iterating over `URLSearchParams`.
- Replacing `tags` with a scalar leaves the old `tags[]` alongside the new scalar.
- A query array containing `null` throws even though the generated type permits it.

Local: `Generation/RuntimeFileWriter.cs:44`.

**Change:** collect the complete parameter family (`key` and `key[...]`) before deleting it, then write the replacement. Align accepted array values and runtime null handling. Add executable JS tests for scalar/array/object transitions, null removal, nested arrays, and SSR use without `window`. Wayfinder's current [runtime](https://github.com/laravel/wayfinder/blob/81df21e16dc161aa2936188b8a65720760f62119/resources/js/wayfinder.ts) has a `clearParamFamily` helper for this exact class of problem.

### P6 — P2: experimental type output does not reliably describe JSON

**Executed generated output:**

```csharp
public enum Status { Pending, Active, Archived = 5, Next, Negative = -1 }
```

The output uses strings for `Pending`, `Active`, `Next`, and `Negative`, with numeric `5` only for `Archived`. It does not represent the CLR enum values or default numeric JSON serialization. The body model references the enum without importing it.

The DTO probe also generated `secret` despite `[JsonIgnore]`, generated `displayName` despite `[JsonPropertyName("display_name")]`, and made nullable properties optional irrespective of the configured serializer's omission policy.

**Source inspection:** class/model indexes use short names as identity; same-named DTOs from different namespaces can overwrite one another. Inheritance, referenced-assembly DTOs, aliases, generic contracts, field selection, and custom converters require stronger backing. `PropsDiscoverer` skips anonymous shorthand members without `NameEquals` and resolves repeated render shapes by taking the one with the most properties, which cannot safely represent incompatible shapes for the same page.

The Pathfinder README recommends `Render<DashboardProps>("Dashboard")`, but `IInertiaService` currently has no generic `Render<T>` overload. Correct the example or deliberately add a real typed rendering API.

**Change:** build the semantic type pipeline before expanding this feature. Use `INamedTypeSymbol` identity and constant evaluation for enums; distinguish output DTOs from input/request contracts; define nullability separately from absence; reflect supported System.Text.Json metadata. Emit diagnostics or `unknown` where runtime shape cannot be inferred safely. Keep these features experimental until their generated output is compiled and checked against actual serialized fixtures.

### P7 — P2/P3: generation/build behavior should be safer and more observable

**Source inspection:**

- The CLI recursively scans all C# files below a directory rather than the project's evaluated `Compile` items. Excluded files, test fixtures, and configuration-specific code can enter discovery; referenced or linked source outside that directory can be missed.
- Unsupported-pattern warnings disappear with `--quiet`; the build package enables quiet by default. Generation can return success while missing requested routes.
- Generation deletes the entire output directory before writing. A failure can remove the previously valid output, and no-change runs still churn files.
- The MSBuild target tracks only `index.ts` as output. Removing another generated file need not trigger regeneration. Input-list deletions and future referenced DTO/config inputs also need explicit invalidation coverage.
- Only routes/actions/forms have feature flags; experimental models, enums, and props are still generated automatically.
- The tool targets .NET 9 while the adapter supports .NET 8–10 and the roadmap favors .NET 10+. Test the installed tool on intended host runtimes; test projects running on .NET 10 do not prove the packaged net9 CLI starts there.

**Change:** add warnings on stderr even in quiet mode, stable diagnostic IDs, `--strict`/`--fail-on-warning`, a machine-readable manifest and `--check` mode. Validate output location, stage generation, retain last-good files on failure, update only changed files, and remove only files owned by a generated-file manifest. Give type features explicit switches. Cover add/edit/delete/config-change and missing-output scenarios in build integration tests.

## Best additions after the fixes

### 1. Typed page props and shared props

**Highest-value Pathfinder expansion.** Introduce an explicit, real typed rendering surface that carries both a component name and props type. Add semantic discovery and a component-to-props map. Generate shared prop typing and the appropriate `@inertiajs/core` module augmentation for the supported client version, so application code gets typed `usePage()` without copying auth/error structures manually.

Wayfinder `next` has a dedicated [shared-data converter](https://github.com/laravel/wayfinder/blob/be273f802dbcc3c9a4a411ab1eea7586b922f7c9/src/Converters/InertiaSharedData.php). The transferable idea is a first-class shared contract; its PHP analysis machinery and exact output API are not requirements for .NET.

### 2. Request, query, and validation contracts

Generate distinct route params, query DTOs, and request-body DTOs, including `[FromQuery]`, `[FromBody]`, `[FromForm]`, and supported Minimal API binding sources. Export useful payload types without requiring users to access an artificial runtime `.body` value.

Start validation metadata with a small explicit subset of DataAnnotations. A FluentValidation integration can be a separate provider with diagnostics for dynamic rules. Generate frontend hints or optional schemas while keeping the server authoritative. File uploads, nullable vs optional inputs, nested field paths, and error bags should be included in the contract tests. Wayfinder's experimental [form request converter](https://github.com/laravel/wayfinder/blob/be273f802dbcc3c9a4a411ab1eea7586b922f7c9/src/Converters/FormRequests.php) is a useful comparison.

### 3. Optional endpoint manifest validation

Keep fast source discovery for supported static patterns. Add an opt-in app-provided endpoint manifest using `EndpointDataSource`, endpoint names, HTTP methods, `RoutePattern`, and component metadata. Compare that manifest against source-generated routes in CI.

This provides a check against MVC conventions, endpoint extension methods, groups, and conditional registrations that source syntax alone cannot reproduce. It should be explicit: booting an app can require configuration or infrastructure, so it is not a suitable mandatory pre-build step. ASP.NET documents endpoint enumeration and URL generation in its [routing architecture](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0).

### 4. Deferred-prop recovery

Add opt-in per-prop rescue behavior and `rescuedProps` metadata, allowing an expensive deferred section to fail while the page remains usable and can offer retry. Keep cancellation and unrelated failures distinguishable, and report errors through normal .NET logging/telemetry.

The current global exception handler does not supply the same behavior. Upstream defines [Rescuable](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/Rescuable.php) and implements it on [DeferProp](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/DeferProp.php).

### 5. Large integer preservation

Add an explicit serialization policy for `long`, `ulong`, and possibly `BigInteger`. Values beyond JavaScript's safe integer range need a deliberate representation. Offer the supported Inertia BigInt marker/flag only when the client version supports it; offer string IDs for applications that prefer them. Keep generated TS consistent with the configured policy. Treat decimal/money precision as a separate decision.

Laravel's [PreservesBigIntegers](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/PreservesBigIntegers.php) and the client's [JSON codec](https://github.com/inertiajs/inertia/blob/348b01aae8d8da2df3227dec20bc4607e51d5d62/packages/core/src/json.ts) show the interoperable mechanism.

### 6. SSR operations and diagnostics

Add configurable timeout, an SSR health-check integration, and an obvious named HttpClient configuration surface. Avoid making an SSR outage delay CSR fallback for the default long HTTP timeout. Record render duration, failure classification, and fallback counts through normal .NET telemetry. Add Vite manifest caching/invalidation and imported-CSS coverage after correctness.

For diagnostics, start with development-only prop resolution timing/classification, route names, deferred groups, once hits, and output sizes. Build on `IInertiaEventHandler` and add finer resolver instrumentation only when needed. Laravel's [DevTools collector](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/DevTools/Collector.php) is a reference for useful data; a full clone of its UI/storage is a larger separate project.

### 7. Testing APIs and a working product sample

Add test-client helpers for partial reloads and loading deferred groups, plus assertions for merge, once, scroll, and rescued metadata. Laravel's [AssertableInertia](https://github.com/inertiajs/inertia-laravel/blob/1e8706260b975c1c652dd886a53657e0fae2102d/src/Testing/AssertableInertia.php) contains real follow-up request helpers, beyond asserting a single response.

Provide one small CRUD sample with validation/error bags, flash redirects, auth/shared props, infinite scroll, Pathfinder imports, and an optional SSR configuration. Use it as a browser test target and as the template reference. Confirm antiforgery end to end: sharing `xsrfToken` alone is not proof the client submits it under the header ASP.NET validates. The client defaults to a cookie/header convention in its [HTTP client](https://github.com/inertiajs/inertia/blob/348b01aae8d8da2df3227dec20bc4607e51d5d62/packages/core/src/xhrHttpClient.ts), while the current starter does not configure the matching application flow.

## Suggested architecture

Use a versioned internal contract model between analysis and code emission:

```text
Source discovery / semantic analysis / optional endpoint manifest
                         |
                         v
        Route + binding + serialized-type contract model
                         |
            validation and stable diagnostics
                         |
                         v
      TypeScript runtime + named routes + action helpers + DTOs
```

Track symbol identity, route template/parts, parameter source/name/default/catch-all kind, accepted methods, component identity, serialization names/omission, and generated-file ownership explicitly. Both named-route and action writers should consume the same emitter. Keep language-independent contracts separate from the TypeScript formatting layer.

Do not add SignalR event typing, environment-variable generation, broad ORM entity generation, or arbitrary client-side validation translation ahead of this foundation. Those may be useful later, but they have weaker immediate value than a trustworthy route/page/request contract. This sequencing also avoids inheriting experimental Wayfinder APIs wholesale.

## Proposed work sequence

| Order | Deliverable | Acceptance criteria |
|---|---|---|
| 1 | HTML/serialization security fixes | Script terminators stay data; `[JsonIgnore]` and serialized property names work on top-level props; SSR produces one root/script |
| 2 | Prop protocol conformance | Actual client accepts match metadata; nested partials, deferred/once combinations, scroll providers/reset, and flash/version reloads behave correctly |
| 3 | Pathfinder output correctness | Simple and complex generated fixtures pass `tsc`; generated URLs agree with ASP.NET for documented patterns; form/query runtime tests pass |
| 4 | Real integration gates | Browser CRUD flow passes with React and Vue; native forms, antiforgery, validation, SSR success/fallback and installed-tool smoke tests pass |
| 5 | Generator/build hardening | Structured diagnostics, strict/check modes, safe writes, owned-file cleanup, and reliable build invalidation |
| 6 | Semantic contract pipeline | Referenced DTOs, namespace collisions, enums, JSON metadata, shared/page props, and request/query DTOs have compiled/runtime fixtures |
| 7 | Selective new runtime features | Rescue and BigInt support have pinned-client conformance tests; SSR health/timeout behavior is observable |

Before a broad parity claim, document minimum and latest tested Inertia client versions. The starter's `^3.0.2` dependency can admit newer minor releases; pin the test matrix and deliberate upgrade policy independently from the .NET package version.

## CI and documentation follow-ups

- Add `tsc --noEmit` over generated output and both frontend starters. CI currently installs frontend dependencies and builds the .NET starters, but does not run their frontend build/type-check scripts.
- Add executable generated-runtime tests and a browser integration job. Keep golden files as review aids.
- Keep .NET 8/9/10 adapter execution in CI, and test the installed Pathfinder package independently of its net10 test host.
- Exercise generated file deletion, source deletion, project exclusions, configuration changes, and namespace collisions.
- Reconcile `ROADMAP.md` with `TODO.md`: the former still describes absent test projects/fixes that already exist, while the latter marks some guarantees complete that the reproductions above disprove. Use acceptance criteria tied to checks, not duplicate status lists.
- Audit README examples against the shipped public API, particularly generic `Render<T>`, Minimal API output filename/casing, native form setup, and the TempData/session explanation. MVC's cookie TempData provider is another supported persistence mechanism; session should not be described as the only possible one.
- Restore reported `NU1902` for transitive `Microsoft.Build.Tasks.Git` 8.0.0 through SourceLink. Review build/package dependency remediation against the [Microsoft advisory](https://github.com/advisories/GHSA-23fw-v26w-5fgq), including SDK-bundled behavior, then inspect produced packages. This observation is a build dependency finding, not proof of an exploit in the application runtime.

## Useful starting points for implementation

| Change | Local entry points |
|---|---|
| Script safety / SSR markup | `src/InertiaNet/Core/InertiaPageJsonConverter.cs`, `src/InertiaNet/TagHelpers/InertiaTagHelper.cs` |
| Typed prop serialization | `src/InertiaNet/Core/InertiaService.cs` |
| Prop conformance | `src/InertiaNet/Resolution/PropsResolver.cs`, `src/InertiaNet/Core/InertiaPage.cs`, `src/InertiaNet/Props/` |
| Flash/errors/version/redirects | `src/InertiaNet/Middleware/InertiaMiddleware.cs` |
| Precognition | `src/InertiaNet/Middleware/InertiaPrecognitionFilter.cs` |
| Route grammar/binding | `src/InertiaNet.Pathfinder/Analysis/RouteTemplateParser.cs`, `ControllerRouteDiscoverer.cs`, `MinimalApiRouteDiscoverer.cs` |
| TS forms/URLs | `src/InertiaNet.Pathfinder/Generation/ActionFileWriter.cs`, `RouteFileWriter.cs`, `RuntimeFileWriter.cs` |
| Type contracts | `src/InertiaNet.Pathfinder/Analysis/PropsDiscoverer.cs`, `ModelDiscoverer.cs`, `EnumDiscoverer.cs`, corresponding writers |
| Build/output safety | `src/InertiaNet.Pathfinder/Program.cs`, `Generation/TypeScriptGenerator.cs`, `src/InertiaNet.Pathfinder.Build/buildTransitive/` |

