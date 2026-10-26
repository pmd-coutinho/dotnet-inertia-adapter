# InertiaNet templates

## Overview

NuGet template package for the React and Vue dotnet-new starters.

## Interface

- Source/configuration: [InertiaNet.Templates.csproj](./InertiaNet.Templates.csproj).
- `InertiaNet.Templates.csproj`: `PackageType` `Template`; target `net8.0`; no default compilation; no build output in package; `templates/**` packed under content excluding `bin` and `obj` directories.
- `templates` subtree includes `inertianet-react` and `inertianet-vue` identities.
- [templates](./templates/README.md): React and Vue dotnet-new starter source group.

## Invariants

- Version is inherited from `InertiaNetPlatformPackageVersion`.
- Package is a content/template distribution; nested starter `Program.cs` files are not compiled into this project.
- `scripts/stage-templates.sh` replaces package placeholders in a copy before packing.

## Side Effects

- Packaging writes NuGet artifacts; staging copies the source subtree and shared version/icon files to a chosen staging directory.

## Verification

Working directory: repository root. Requires .NET 10 and Node 24. This follows the template smoke build sequence in CI; it installs templates in the local .NET template registry and downloads dependencies.

```sh
version=$(dotnet msbuild src/InertiaNet/InertiaNet.csproj -getProperty:Version)
work=$(mktemp -d)
dotnet pack src/InertiaNet/InertiaNet.csproj -c Release -o "$work/artifacts"
bash scripts/stage-templates.sh src/InertiaNet.Templates "$work/templates" "$version"
dotnet pack "$work/templates/InertiaNet.Templates.csproj" -c Release -o "$work/artifacts"
dotnet new install "$work/artifacts/InertiaNet.Templates.$version.nupkg"
dotnet new inertianet-react -n SmokeReact -o "$work/smoke-react"
dotnet new inertianet-vue -n SmokeVue -o "$work/smoke-vue"
npm --prefix "$work/smoke-react/ClientApp" install
npm --prefix "$work/smoke-react/ClientApp" run build
npm --prefix "$work/smoke-vue/ClientApp" install
npm --prefix "$work/smoke-vue/ClientApp" run build
dotnet restore "$work/smoke-react/SmokeReact.csproj" --source "$work/artifacts" --source https://api.nuget.org/v3/index.json
dotnet build "$work/smoke-react/SmokeReact.csproj" --no-restore
dotnet restore "$work/smoke-vue/SmokeVue.csproj" --source "$work/artifacts" --source https://api.nuget.org/v3/index.json
dotnet build "$work/smoke-vue/SmokeVue.csproj" --no-restore
```

The staging script removes its destination before copying; the commands use a newly created temporary directory. Keep `work` until inspection is complete. Starter source projects contain `__INERTIANET_PACKAGE_VERSION__`, so stage and instantiate them before .NET restore/build.
