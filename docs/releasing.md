# Releasing

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

Default local builds still use the shared `alpha.1` suffix.

## CI Prerelease Version Format

On every successful `master` merge, the publish workflow computes:

- `<InertiaNetPlatformVersionPrefix>-alpha.<CI run number>`

Examples:

- `InertiaNet` -> `3.0.0-alpha.142`
- `InertiaNet.Pathfinder` -> `3.0.0-alpha.142`
- `InertiaNet.Templates` -> `3.0.0-alpha.142`

The repository itself is not mutated during publishing. CI computes the final versions at pack time.

## Manual Stable Publishing

Stable releases use the same workflow file via `workflow_dispatch`.

Run the workflow manually and choose:

- `release_kind = stable`
- `version_prefix = 3.0.0` (or the next platform version you intend to publish)

That publishes all packages at the exact stable version:

- `InertiaNet` -> `3.0.0`
- `InertiaNet.Pathfinder` -> `3.0.0`
- `InertiaNet.Pathfinder.Build` -> `3.0.0`
- `InertiaNet.Analyzers` -> `3.0.0`
- `InertiaNet.Templates` -> `3.0.0`

You can also use `workflow_dispatch` with `release_kind = prerelease` if you ever need a manual prerelease.

## Templates And Version Stamping

`InertiaNet.Templates` contains starter projects that depend on `InertiaNet`.

Before packing templates, CI stages the template sources and replaces the internal `__INERTIANET_PACKAGE_VERSION__` token with the just-computed platform version. This keeps generated starter projects aligned with the package version published in the same workflow.

The staging helper script is:

- `scripts/stage-templates.sh`

## Notes

- Keep `publish-prerelease.yml` as the trusted-publishing workflow file unless you also update the nuget.org trusted publishing policy.
- Stable releases are intentionally manual.
