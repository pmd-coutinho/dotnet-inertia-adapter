# React starter pages

## Overview

React starter Inertia page component.

## Interface

- Source/configuration: [Home.tsx](./Home.tsx).
- `Home.tsx` default export `Home({ title })` renders the starter landing page; optional `title` defaults to InertiaNet React Starter.

## Invariants

- Server component `Home` resolves to `pages/Home.tsx`.

## Side Effects

- Renders page markup; no network or persistence.

## Verification

Working directory: `src/InertiaNet.Templates/templates/react-starter/ClientApp`. Install frontend dependencies, then use the configured type check and Vite build:

```sh
npm install
npm run typecheck
npm run build
```

Use the [template package verification](./../../../../../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.
