# Repository Scripts

## Overview

These helpers stage the starter-template package and verify the installed
Pathfinder tool with its MSBuild companion. They are invoked by the
[CI workflow](../.github/workflows/ci.yml).

## Interface

- [compute-package-version.sh](./compute-package-version.sh) accepts
  `<default-prefix> <event> <release-kind> <prefix-override> <suffix-override>
  <publish-run-number>`. It validates SemVer and prints release kind, prefix and
  final package version as GitHub Actions outputs. Automatic and manual previews
  use the same publishing-workflow run number unless manually overriding a suffix.
- [test-package-version.sh](./test-package-version.sh) verifies automatic/manual
  numbering, stable releases, explicit overrides, reruns and invalid inputs.
- [stage-templates.sh](./stage-templates.sh) accepts exactly three arguments:
  `<source-dir> <staging-dir> <inertianet-package-version>`. It copies template
  sources, shared version properties, and the package icon into staging, then
  replaces `__INERTIANET_PACKAGE_VERSION__` in starter project files.
- [test-pathfinder-package.cjs](./test-pathfinder-package.cjs) accepts
  `<feed> <version> <tfm...>`. For each framework it installs the packaged tool,
  exercises generation and `--check`, verifies incremental build behavior and
  output repair, and exercises the default local-tool command.

## Invariants

- The staging directory must be disposable and separate from the source tree:
  staging removes its destination before copying.
- Package tests require matching Pathfinder tool and build-companion versions
  in the supplied feed. Package-source mapping sends `InertiaNet.*` to that
  feed and `Microsoft.*` to nuget.org.
- Test subprocesses use isolated `NUGET_PACKAGES` and `DOTNET_CLI_HOME`
  directories and a 120-second timeout. Failed commands or assertions fail the
  script.
- An unchanged rebuild must preserve generated module timestamps; deleting a
  generated module must trigger regeneration on the next build.

## Side Effects

- Staging recursively deletes and recreates its destination, copies files, and
  substitutes package-version tokens with Perl.
- Package verification creates temporary projects, installs tools and NuGet
  dependencies, runs builds and generator processes, and deletes a generated
  module to test repair. It prints results and removes its temporary root in a
  `finally` block, including its package cache and CLI home.

## Verification

From the repository root, check script syntax without running staging or
package installation:

```bash
bash -n scripts/stage-templates.sh
bash -n scripts/compute-package-version.sh scripts/test-package-version.sh
bash scripts/test-package-version.sh
node --check scripts/test-pathfinder-package.cjs
```

After packing the tool and build companion into `artifacts`, run the installed
integration check, replacing `<version>` with their shared package version:

```text
node scripts/test-pathfinder-package.cjs artifacts <version> net8.0 net9.0 net10.0
```

See [release staging](../docs/releasing.md#templates-and-version-stamping) and
the CI workflow for the complete pack/stage/smoke-build sequence.
