# React starter bootstrap

## Overview

React starter browser bootstrap and page resolver.

## Interface

- Source/configuration: [app.tsx](./app.tsx).
- `app.tsx` calls `createInertiaApp` with eager `./pages/**/*.tsx` resolution.
- React: `setup` returns `app` when no element, otherwise hydrates `data-server-rendered` roots or renders with `createRoot`.
- [pages](./pages/README.md): React starter Inertia page component.

## Invariants

- Unknown page names throw; page names and server mappings must agree.

## Side Effects

- Initializes the Inertia client and mounts or hydrates the DOM when a root element exists.

## Verification

Working directory: `src/InertiaNet.Templates/templates/react-starter/ClientApp`. Install frontend dependencies, then use the configured type check and Vite build:

```sh
npm install
npm run typecheck
npm run build
```

Use the [template package verification](./../../../../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.
