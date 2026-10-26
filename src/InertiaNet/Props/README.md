# Prop Wrappers

## Overview

Concrete wrappers pair values or lazy callbacks with the metadata needed for optional, deferred, merged, once-cached, and infinite-scroll props.

## Interface

- `AlwaysProp` includes a value even when partial filters would exclude ordinary props.
- `OptionalProp` is excluded on initial loads and supports `Once`, `Fresh`, `As`, and `Until` configuration.
- `DeferProp` adds deferred groups, optional `Rescue`, merge/deep-merge, append/prepend, matching keys, and once behavior.
- `MergeProp` supports root append/prepend, nested `Append`/`PrependAt`, `DeepMerge`, `MatchOn`, and once behavior.
- `OnceProp` supports `Fresh`, `As`, and `Until`.
- `ScrollProp` exposes `Wrapper`, `ResolveAsync`, `ResolveMetadataAsync`, `ConfigureMergeIntent`, `Defer`, `DeepMerge`, and `MatchOn`.
- `ScrollMetadata` implements `IProvidesScrollMetadata` and exports `pageName`, `previousPage`, `nextPage`, and `currentPage` through `ToInertiaFormat()`.

## Invariants

- Constructors retain values/callbacks without invoking callbacks. `ResolveAsync` returns the value or invokes the callback with services and cancellation.
- Fluent methods mutate and return the same wrapper. These objects describe client caching and merging; they do not store a persistent server cache or merge client data.
- `DeferProp` always defers and defaults to group `default`. `OnceProp` always resolves once; other once-capable wrappers require enabling that behavior.
- `MergeProp` defaults to root append. `Prepend()` disables root append; nested append/prepend methods record their target paths.
- `ScrollProp` merges at its wrapper path, default `data`. `ConfigureMergeIntent` clears previous path lists and selects prepend only for the exact header value `prepend`, otherwise append.
- Expiry uses Unix milliseconds; `Until(TimeSpan)` computes it when configured. Rescue is opt-in on `DeferProp`, and cancellation still propagates through resolution.

## Side Effects

Resolution executes stored application callbacks. Fluent methods mutate wrapper state; relative expiry reads the clock. Scroll merge configuration reads request headers and changes merge-path lists.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
