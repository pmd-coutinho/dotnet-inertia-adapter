# Playground client and SSR entries

## Overview

React/Vue browser entries and shared Node SSR entry.

## Interface

- Source/configuration: [react.tsx](./react.tsx), [vue.ts](./vue.ts), [ssr.ts](./ssr.ts), [vue/Todos.vue](./vue/Todos.vue).
- `react.tsx` initializes only `React/Todos`; `vue.ts` initializes only `Vue/Todos`.
- `ssr.ts` initializes both Inertia render functions and starts `createServer` on `127.0.0.1` with `SSR_PORT` default `13714`.
- `react/Todos.tsx` and `vue/Todos.vue` implement equivalent typed todo UI using generated Pathfinder contracts.
- [style.css](./style.css) supplies shared presentation for both clients.
- [react](./react/README.md): Typed React todo page exercising InertiaNet/Pathfinder integration.

## Invariants

- All browser/SSR entries and Razor view agree on `playground-root`.
- Browser `setup` hydrates only roots marked `data-server-rendered`.
- SSR initialization must yield functions; `forceSsrFailure` deliberately throws; `React/Todos` selects React renderer, other pages select Vue renderer.

## Side Effects

- Browser entries mount/hydrate DOM and import styles; SSR entry opens a loopback listener and renders page markup; requested failure exercises .NET CSR fallback.

## Verification

Working directory: `samples/Playground/ClientApp`. Requires .NET 10 and Node 24; the build creates the generated Pathfinder contracts and browser/SSR bundles.

```sh
npm ci
npm run build
npm run test:manifest
npx playwright install chromium
npm run test:browser
```

Playwright starts its own .NET and Node servers. Override their ports with `PLAYGROUND_PORT` and `PLAYGROUND_SSR_PORT`; an installed browser can be selected with `PLAYWRIGHT_CHROMIUM_EXECUTABLE`.
