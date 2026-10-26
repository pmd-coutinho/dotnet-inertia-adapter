# Razor Tag Helpers

## Overview

Razor helpers emit the Inertia app container, page data, SSR head/body content, and Vite development or production assets.

## Interface

- `InertiaTagHelper` targets `<inertia />`; `Id` defaults to `app`.
- `InertiaHeadTagHelper` targets `<inertia-head />`.
- `ViteInputTagHelper` targets `<vite-input src="..." />` and accepts Vite options plus an optional memory cache.
- `ViteReactRefreshTagHelper` targets `<vite-react-refresh />`.

## Invariants

- Helpers suppress their custom wrapper tags. The app helper requires an active HTTP context and reads page/SSR state from request items.
- A successful SSR body is emitted directly because it owns the root element and page script. The root ID must agree with the SSR entry point.
- Without SSR, the app helper emits an encoded root ID and an `application/json` page script when a page is available. It escapes `<`, `>`, and `&` after serialization to protect the HTML boundary.
- SSR head/body are inserted as HTML supplied by the renderer.
- Hot mode emits the Vite client and requested module from the hot URL. Production requires matching manifest entries and traverses static imports while deduplicating CSS, preloads, and visited entries.
- Generated asset URLs are HTML-encoded. The React refresh preamble must appear before the Vite input during development and emits nothing without a hot file.

## Side Effects

Mutates generated HTML, reads hot/manifest files, and can update the shared manifest cache. Emitted scripts and links trigger browser asset requests and module execution when the page loads.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
