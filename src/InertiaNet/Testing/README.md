# Runtime Testing Helpers

## Overview

Public helpers shipped with the runtime provide fluent page/redirect assertions and HTTP requests for reloads and deferred groups.

## Interface

- `AssertableInertiaPage` wraps a page, supports `FromJson`, and exposes `Page`.
- Fluent assertions cover component, props, explicit null, counts, shared keys, URL/version, deferred groups, merge/prepend/deep-merge/matching/rescued paths, once/scroll metadata, flash, history flags, and exact large integers.
- `InertiaReloadOptions` supplies `Only`, `Except`, `Reset`, and `ExceptOnceProps` lists.
- `GetInertiaAsync`, `ReloadInertiaAsync`, and `LoadDeferredPropsAsync` extend `HttpClient`.
- `AssertInertiaAsync`, `AssertInertiaRedirect`, `AssertFragmentRedirect`, and `AssertVersionRedirect` extend `HttpResponseMessage`.
- `AssertableInertiaRedirect`, `AssertableInertiaFragmentRedirect`, and `AssertableInertiaVersionRedirect` expose captured destinations and `To(expected)` assertions.

## Invariants

- Fluent mismatches throw `AssertionException`; successful checks return their wrapper.
- `AssertInertiaAsync` requires status `200`, `X-Inertia: true`, JSON or `+json` content type, and a valid page containing component, props, and URL.
- `HasProp`/`HasFlash` with a null expected argument check existence; `HasNullProp` explicitly checks JSON null. Supplied once/scroll expectations compare complete metadata shape.
- Exact integer checks accept numeric and decimal-string transport and only unwrap `$bigint` markers when the page enables revival; they avoid floating-point conversion.
- Redirect assertions require `409` and the appropriate location/redirect header.
- Request helpers add per-request headers without changing client defaults. Reload returns the server response without simulating client-side prop merging.
- Deferred loading validates all selected group names before requests, deduplicates names, then sends one sequential partial request per group and checks the component.
- Header path lists reject blank entries, commas, or newlines. Cancellation propagates through requests and content reads.

## Side Effects

Assertions parse/serialize values and read response content. HTTP helpers send real GET requests and dispose the responses they obtain; handlers exercised by those requests retain their normal server effects.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
