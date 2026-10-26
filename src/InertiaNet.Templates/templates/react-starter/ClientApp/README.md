# React starter frontend

## Overview

React starter frontend package, compiler configuration, and Vite build boundary.

## Interface

- Source/configuration: [package.json](./package.json), [vite.config.ts](./vite.config.ts), [tsconfig.json](./tsconfig.json).
- `package.json` exposes `dev` for Vite, `typecheck` for `tsc --noEmit`, and `build` to type-check before bundling with Vite.
- `vite.config.ts` uses `react` and `laravel-vite-plugin`, `input` `src/app.tsx`, `publicDirectory` `../wwwroot`, `buildDirectory` `build`, `refresh` `true`.
- [src](./src/README.md): React starter browser bootstrap and page resolver.

## Invariants

- TypeScript uses strict checks and `Bundler` module resolution.
- Vite entry path must match the Razor `vite-input` entry.

## Side Effects

- `dev` starts Vite; `build` writes assets and the manifest into `../wwwroot/build`; `npm install` writes dependency installation and a lockfile when absent.

## Verification

Working directory: `src/InertiaNet.Templates/templates/react-starter/ClientApp`. Install frontend dependencies, then use the configured type check and Vite build:

```sh
npm install
npm run typecheck
npm run build
```

Use the [template package verification](./../../../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.
