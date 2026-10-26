# Playground frontend

## Overview

Playground frontend generation, dual-adapter builds, SSR service and Playwright orchestration.

## Interface

- Source/configuration: [package.json](./package.json), [vite.config.ts](./vite.config.ts), [playwright.config.ts](./playwright.config.ts), [tsconfig.json](./tsconfig.json).
- `package.json` exposes `generate`, `typecheck`, `build`, `dev`, `ssr`, `test:browser` and `test:manifest`.
- `generate` restores `../Playground.csproj` and runs Pathfinder `net10.0` in `--strict --quiet` mode with `../contracts.json` into `src/pathfinder`.
- `build` runs generation/typecheck, Vite browser `build` and SSR bundle into `ssr`; `ssr` executes `node ssr/ssr.js`.
- `vite.config.ts` combines React/Vue/Laravel plugins and entries `src/react.tsx`, `src/vue.ts` with `ssr` `src/ssr.ts`; `noExternal` includes Inertia clients/core.
- `playwright.config.ts` uses one worker, no retries, and configurable app/SSR ports `5197/5198`, launches Node and .NET servers.
- [src](./src/README.md): React/Vue browser entries and shared Node SSR entry.

## Invariants

- Requires .NET 10 and Node 24 as existing documentation/CI specifies.
- Build must precede SSR service/browser tests.
- Generated `Int64/UInt64` props/actions must match server `String` serialization.

## Side Effects

- Generation restores .NET packages and writes generated TypeScript; builds write browser and SSR artifacts; `dev`, `ssr` and browser tests start local servers; Playwright writes failure traces/screenshots.

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
