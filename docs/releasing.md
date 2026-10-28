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
- `InertiaNetLaravelBaselineVersion` (compatibility reference only)

All publishable packages now use the same resolved package version.

InertiaNet uses its own Semantic Versioning sequence. Increment the patch for
compatible fixes, the minor for compatible features, and the major for breaking
changes. All five packages stay on this shared sequence. Their versions do not
need to equal an upstream adapter or JavaScript client version.

The separately recorded compatibility baseline is the reviewed
[inertia-laravel v3.5.2 release](https://github.com/inertiajs/inertia-laravel/releases/tag/v3.5.2).
Change `InertiaNetLaravelBaselineVersion` when an upstream release has been
reviewed; it does not change NuGet precedence. JavaScript client compatibility is
tested independently (currently 3.0.2 and 3.9.1).

The current development prefix is `3.5.3`; local builds use `3.5.3-alpha.0`.
The local suffix is not a published release number. Publication assigns its own
suffix and stamps every package, including template dependencies, with the same
resolved version.

A preview of `3.5.3` sorts above stable `3.5.2`; every `3.5.2-alpha.*` sorts
below stable `3.5.2`, regardless of its publication date. See
[NuGet version precedence](https://learn.microsoft.com/nuget/concepts/package-versioning).
Users must still opt into prereleases to receive previews.

## CI Prerelease Version Format

On every successful `master` merge, the publish workflow computes:

- `<InertiaNetPlatformVersionPrefix>-alpha.<publishing workflow run number>`

Examples:

- `InertiaNet` -> `3.5.3-alpha.142`
- `InertiaNet.Pathfinder` -> `3.5.3-alpha.142`
- `InertiaNet.Templates` -> `3.5.3-alpha.142`

Automatic and manual previews both use the publishing workflow's
`github.run_number`, rather than mixing CI and publishing counters. GitHub
[increments this number per workflow run and retains it for reruns](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#github-context).
Reruns intentionally reuse the same package version: `--skip-duplicate` allows
retrying a partially completed push. Gaps are expected because stable runs and
failed runs also consume numbers.

`scripts/compute-package-version.sh` validates the prefix and optional manual
suffix, then produces the workflow outputs. Manual suffix overrides such as
`rc.1` are explicit exceptions to automatic alpha numbering; use unique suffixes
and account for SemVer ordering when switching prerelease channels. The workflow
refuses to publish a preview when the runtime package's stable base version is
already on NuGet. It fails if the feed cannot be checked.

The repository itself is not mutated during publishing. CI computes the final
versions at pack time. Verify the calculation with
`bash scripts/test-package-version.sh`; CI runs the same checks.

## Manual Stable Publishing

Stable releases use the same workflow file via `workflow_dispatch`.

Run the workflow manually and choose:

- `release_kind = stable`
- `version_prefix = 3.5.3` (or the next platform version you intend to publish)

That publishes all packages at the exact stable version:

- `InertiaNet` -> `3.5.3`
- `InertiaNet.Pathfinder` -> `3.5.3`
- `InertiaNet.Pathfinder.Build` -> `3.5.3`
- `InertiaNet.Analyzers` -> `3.5.3`
- `InertiaNet.Templates` -> `3.5.3`

You can also use `workflow_dispatch` with `release_kind = prerelease` if you ever need a manual prerelease.

After releasing stable `3.5.3`, advance `InertiaNetPlatformVersionPrefix` to the
next planned version (for example `3.5.4`) before the next preview publication.
An ordinary bug fix can be published as the next stable patch without releasing
an alpha first. Keep the reviewed Laravel baseline unchanged unless the upstream
compatibility review also changes it. Publishing an older source snapshot still
uses that snapshot's prefix, so do not use old commits to publish new previews.

## Templates And Version Stamping

`InertiaNet.Templates` contains starter projects that depend on `InertiaNet`.

Before packing templates, CI stages the template sources and replaces the internal `__INERTIANET_PACKAGE_VERSION__` token with the just-computed platform version. This keeps generated starter projects aligned with the package version published in the same workflow.

The staging helper script is:

- `scripts/stage-templates.sh`

## Notes

- Keep `publish-prerelease.yml` as the trusted-publishing workflow file unless you also update the nuget.org trusted publishing policy.
- Stable releases are intentionally manual.
