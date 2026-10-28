# Razor Tag Helpers

## Overview

Razor helpers emit the Inertia app container, page data, SSR head/body content, and Vite development or production assets.

## Interface

- `InertiaTagHelper` targets `<inertia />`; `Id` defaults to `app`.
- `InertiaHeadTagHelper` targets `<inertia-head />`.
- `ViteInputTagHelper` targets `<vite-input src="..." />` and accepts Vite options, an optional memory cache, and the current HTTP context accessor.
- `ViteReactRefreshTagHelper` targets `<vite-react-refresh />`. Both Vite helpers accept an explicit `Nonce` or use `ViteOptions.NonceProvider` for the current request.

## Invariants

- Helpers suppress their custom wrapper tags. The app helper requires an active HTTP context and reads page/SSR state from request items.
- A successful SSR body is emitted directly because it owns the root element and page script. The root ID must agree with the SSR entry point.
- Without SSR, the app helper emits an encoded root ID and an `application/json` page script when a page is available. It escapes `<`, `>`, and `&` after serialization to protect the HTML boundary.
- SSR head/body are inserted as HTML supplied by the renderer.
- Hot mode emits a stylesheet link for CSS entries, or the Vite client and requested module for JavaScript entries, from the hot URL. Put CSS entries in the document head so SSR markup is styled before JavaScript runs. Production requires matching manifest entries and traverses static imports while deduplicating CSS, preloads, and visited entries.
- Generated asset URLs are HTML-encoded. The React refresh preamble must appear before the Vite input during development and emits nothing without a hot file.
- Production asset URLs include `Request.PathBase`; dev-server URLs do not. Vite's frontend build base must match the deployment prefix.
- Nonces propagate to scripts, stylesheets, preloads, and the React preamble. A matching manifest asset's optional `IntegrityKey` (default `integrity`) supplies SRI plus `crossorigin="anonymous"`. CSS integrity is looked up by output filename. Missing hashes remain optional; null `IntegrityKey` disables SRI.

## Side Effects

Mutates generated HTML, reads hot/manifest files, and can update the shared manifest cache. Emitted scripts and links trigger browser asset requests and module execution when the page loads.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
