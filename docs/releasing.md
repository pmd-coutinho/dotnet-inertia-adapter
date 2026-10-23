# Releasing

Build with a current patched .NET SDK. SourceLink is included in SDK 8 and newer;
do not add the old explicit `Microsoft.SourceLink.GitHub` 8.0.0 reference, which
overrides the SDK tooling and brings back the vulnerable build-task dependency.
See [the SDK SourceLink guidance](https://github.com/dotnet/sourcelink#using-source-link-in-net-projects)
and [Microsoft's advisory](https://github.com/advisories/GHSA-23fw-v26w-5fgq).

## Publishing Model

This repository uses one **platform-wide version train** for all publishable packages:

- `InertiaNet`
- `InertiaNet.Pathfinder`
- `InertiaNet.Pathfinder.Build`
- `InertiaNet.Analyzers`
- `InertiaNet.Templates`

Publishing is split into two modes:

- **Prerelease**: published automatically after successful `master` merges
- **Stable**: published manually from GitHub Actions via `workflow_dispatch`

The publish workflow is:

- `.github/workflows/publish-prerelease.yml`

It uses **NuGet trusted publishing** through GitHub OIDC and publishes all packable packages to nuget.org.

## Trusted Publishing Setup

### 1. Configure nuget.org

In nuget.org:

1. Open **Trusted Publishing**.
2. Add a policy for this repository.
3. Point it to:
   - repository owner: `pmd-coutinho`
   - repository: `dotnet-inertia-adapter`
   - workflow file: `publish-prerelease.yml`
   - environment: `nuget-prerelease`

### 2. Configure GitHub

In GitHub:

1. Create an environment named `nuget-prerelease`.
2. Add a repository or environment variable named `NUGET_USERNAME`.

`NUGET_USERNAME` must be your nuget.org profile name, not your email address.

No long-lived NuGet API key secret is required.

## Shared Versioning

The shared platform version is centralized in `Directory.Build.props`:

- `InertiaNetPlatformVersionPrefix`
- `InertiaNetDefaultVersionSuffix`
- `InertiaNetPlatformPackageVersion`

All publishable packages now use the same resolved package version.

The base is aligned with the reviewed [inertia-laravel v3.5.2 release](https://github.com/inertiajs/inertia-laravel/releases/tag/v3.5.2), rechecked on 2026-10-23. This identifies the server reference release; JavaScript client compatibility is tested independently (currently 3.0.2 and 3.9.1).

Default local builds use `3.5.2-alpha.1`. Successful master CI publishes `3.5.2-alpha.<CI run number>` through the existing workflow. The version base does not assert complete feature parity.

## CI Prerelease Version Format

On every successful `master` merge, the publish workflow computes:

- `<InertiaNetPlatformVersionPrefix>-alpha.<CI run number>`

Examples:

- `InertiaNet` -> `3.5.2-alpha.142`
- `InertiaNet.Pathfinder` -> `3.5.2-alpha.142`
- `InertiaNet.Templates` -> `3.5.2-alpha.142`

The repository itself is not mutated during publishing. CI computes the final versions at pack time.

## Manual Stable Publishing

Stable releases use the same workflow file via `workflow_dispatch`.

Run the workflow manually and choose:

- `release_kind = stable`
- `version_prefix = 3.5.2` (or the next platform version you intend to publish)

That publishes all packages at the exact stable version:

- `InertiaNet` -> `3.5.2`
- `InertiaNet.Pathfinder` -> `3.5.2`
- `InertiaNet.Pathfinder.Build` -> `3.5.2`
- `InertiaNet.Analyzers` -> `3.5.2`
- `InertiaNet.Templates` -> `3.5.2`

You can also use `workflow_dispatch` with `release_kind = prerelease` if you ever need a manual prerelease.

## Templates And Version Stamping

`InertiaNet.Templates` contains starter projects that depend on `InertiaNet`.

Before packing templates, CI stages the template sources and replaces the internal `__INERTIANET_PACKAGE_VERSION__` token with the just-computed platform version. This keeps generated starter projects aligned with the package version published in the same workflow.

The staging helper script is:

- `scripts/stage-templates.sh`

## Notes

- Keep `publish-prerelease.yml` as the trusted-publishing workflow file unless you also update the nuget.org trusted publishing policy.
- Stable releases are intentionally manual.
