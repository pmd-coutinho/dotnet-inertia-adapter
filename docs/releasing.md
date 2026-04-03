# Releasing

## Prerelease Publishing

This repository publishes prerelease packages automatically from successful `master` branch merges using **NuGet trusted publishing**.

The publish workflow is:

- `.github/workflows/publish-prerelease.yml`

It runs after the `CI` workflow succeeds on a `master` push, requests a short-lived NuGet API key via GitHub OIDC, and publishes all packable packages to nuget.org.

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

Version prefixes are centralized in `Directory.Build.props`:

- `InertiaNetCoreVersionPrefix`
- `InertiaNetToolingVersionPrefix`

Current package families:

- Core adapter family:
  - `InertiaNet`
- Tooling family:
  - `InertiaNet.Pathfinder`
  - `InertiaNet.Pathfinder.Build`
  - `InertiaNet.Analyzers`
  - `InertiaNet.Templates`

Default local versions still use the shared `alpha.1` suffix.

## CI Prerelease Version Format

On every successful `master` merge, the publish workflow computes:

- core: `<InertiaNetCoreVersionPrefix>-alpha.<CI run number>`
- tooling: `<InertiaNetToolingVersionPrefix>-alpha.<CI run number>`

Examples:

- `InertiaNet` -> `3.0.0-alpha.142`
- `InertiaNet.Pathfinder` -> `0.1.0-alpha.142`

The repository itself is not mutated during publishing. CI computes the final versions at pack time.

## Templates And Version Stamping

`InertiaNet.Templates` contains starter projects that depend on `InertiaNet`.

Before packing templates, CI stages the template sources and replaces the internal `__INERTIANET_PACKAGE_VERSION__` token with the just-computed core prerelease version. This keeps generated starter projects aligned with the package version published in the same workflow.

The staging helper script is:

- `scripts/stage-templates.sh`

## Stable Releases

Stable publishing is intentionally not automated yet.

Recommended future direction:

- keep prerelease publishing on every `master` merge
- add a separate tag-driven stable workflow later
