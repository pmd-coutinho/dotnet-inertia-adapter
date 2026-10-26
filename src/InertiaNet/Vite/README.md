# Vite Manifest Reader

## Overview

An internal reader shares parsed Vite manifests between asset tag generation and page validation.

## Interface

- Internal `ViteManifest.Read(path, cache)` returns a cloned `JsonElement` root and accepts an optional `IMemoryCache`.
- `ViteOptions.CacheManifest` controls whether callers supply the shared manifest cache.

## Invariants

- Paths become absolute before cache lookup. Missing manifests throw `InvalidOperationException`; a non-object JSON root throws `JsonException`.
- Cache reuse requires matching file length and last-write time. The cache key includes the reader type and full path.
- Parsed roots are cloned before disposing the JSON document. A manifest changed during reading is not published to the cache.
- Cache entries have ten-minute sliding expiration and size one. Deployments preserving both length and timestamp must disable caching to detect changed contents reliably.

## Side Effects

Reads file existence, metadata, and text; reads and updates the supplied memory cache. The reader does not generate or modify assets.

## Verification

Run from the repository root:

```sh
dotnet test tests/InertiaNet.Tests/InertiaNet.Tests.csproj
```

The test project targets `net8.0`, `net9.0`, and `net10.0`.
