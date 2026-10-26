# Prop Contexts

## Overview

Context objects expose the current request and render state to custom prop providers.

## Interface

- `PropertyContext` supplies `Key`, sibling `Props`, `HttpContext`, and `Request` to `IProvidesInertiaProperty`.
- `RenderContext` supplies `Component`, `HttpContext`, and `Request` to `IProvidesInertiaProperties`.
- Both expose `Referer`, `PartialComponent`, and `IsInertiaRequest`; `RenderContext` also exposes `IsPartialReload`.

## Invariants

- Constructor values are retained through getter-only properties; sibling values and the HTTP context are shared references.
- `IsInertiaRequest` checks for the presence of `X-Inertia`. `IsPartialReload` compares the partial-component header to `Component` independently.
- An absent or empty Referer header produces `null`; request-derived properties read the current headers.

## Side Effects

These objects retain references and read request headers. They do not invoke prop providers or mutate sibling props or responses.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
