# Prop Capability Interfaces

## Overview

Public contracts let prop wrappers and application providers describe resolution, caching, merging, pagination, and failure handling.

## Interface

- `IDeferrable`: `ShouldDefer()` and `Group()`; `IIgnoreFirstLoad`: marker for exclusion from initial non-partial output.
- `IMergeable`: merge/deep-merge flags, matching keys, and root or nested append/prepend paths.
- `IOnceable`: `ShouldResolveOnce()`, `ShouldBeRefreshed()`, `GetKey()`, and `ExpiresAt()`.
- `IProvidesInertiaProperties.ToInertiaProperties(RenderContext)` supplies multiple named props; `IProvidesInertiaProperty.ToInertiaProperty(PropertyContext)` supplies one value.
- `IProvidesScrollMetadata`: page parameter name and previous, next, and current page identifiers.
- `IRescuable.ShouldRescue`: whether eligible non-cancellation resolution failures can be reported as rescued props.

## Invariants

- Capability interfaces describe behavior; the resolver applies their exclusion and metadata rules.
- Once keys may be customized; an absent key falls back to the prop name/path. Expiry is nullable Unix-epoch milliseconds.
- Multi-prop providers receive render context; single-prop providers receive the prop path, sibling values, and request.
- Pagination identifiers support numbers, cursors, and `null`; the page parameter name is nullable.

## Side Effects

Declarations have no side effects. Invoking an application provider can execute application logic through its implementation.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
