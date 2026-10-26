# Starter templates

## Overview

React and Vue dotnet-new starter source group.

## Interface

- [react-starter](./react-starter/README.md): `inertianet-react`, an ASP.NET Core .NET 10 InertiaNet project with a React frontend.
- [vue-starter](./vue-starter/README.md): `inertianet-vue`, an ASP.NET Core .NET 10 InertiaNet project with a Vue frontend.

## Invariants

- Both starters use `InertiaNetStarter` as their `sourceName` and a package version placeholder replaced during staging.
- Both use `ClientApp` for frontend sources and `wwwroot/build` for Vite artifacts.

## Side Effects

- Template instantiation copies starter files; generated apps run HTTP servers and their frontend builds emit assets.

## Verification

Use the [template package verification](./../README.md#verification) for staging, packaging, installation and .NET smoke builds. The unprocessed starter project contains a package-version placeholder.

Working directory: repository root; run the complete command sequence in that linked section for both starters.
