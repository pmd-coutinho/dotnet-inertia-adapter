# Vue starter bootstrap

## Overview

Vue starter browser bootstrap and page resolver.

## Interface

- Source/configuration: [app.ts](./app.ts), [pages/Home.vue](./pages/Home.vue).
- `app.ts` calls `createInertiaApp` with eager `./pages/**/*.vue` resolution.
- Vue: `setup` installs Inertia plugin and selects `createSSRApp` for missing/server-rendered roots, otherwise `createApp`.
- `pages/Home.vue` renders the fixed InertiaNet Vue Starter heading.

## Invariants

- Unknown page names throw; page names and server mappings must agree.

## Side Effects

- Initializes the Inertia client and mounts or hydrates the DOM when a root element exists.

## Verification

Working directory: `src/InertiaNet.Templates/templates/vue-starter/ClientApp`. Install frontend dependencies, then use the configured type check and Vite build:

```sh
npm install
npm run typecheck
npm run build
```

Use the [template package verification](./../../../../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.
