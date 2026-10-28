#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"

expect_version() {
  local expected="$1"
  shift
  local actual
  actual="$(bash "$script_dir/compute-package-version.sh" "$@")"
  if ! printf '%s\n' "$actual" | grep -Fxq "platform_version=$expected"; then
    printf 'Expected %s, received:\n%s\n' "$expected" "$actual" >&2
    exit 1
  fi
}

expect_invalid() {
  if bash "$script_dir/compute-package-version.sh" "$@" >/dev/null 2>&1; then
    printf 'Unexpected success for arguments: %s\n' "$*" >&2
    exit 1
  fi
}

# Both entry points use the publisher's counter; automatic runs ignore dispatch inputs.
expect_version 3.5.3-alpha.42 3.5.3 workflow_run stable 9.0.0 rc.1 42
expect_version 3.5.3-alpha.42 3.5.3 workflow_dispatch prerelease '' '' 42
expect_version 3.5.3-alpha.43 3.5.3 workflow_dispatch prerelease '' '' 43
expect_version 3.5.3-alpha.44 3.5.3 workflow_run '' '' '' 44
# A rerun deliberately reuses its version so partial pushes can be retried.
expect_version 3.5.3-alpha.44 3.5.3 workflow_run '' '' '' 44
expect_version 3.5.3 3.5.3 workflow_dispatch stable '' 'ignored invalid suffix' ''
expect_version 3.6.0 3.5.3 workflow_dispatch stable 3.6.0 '' 45
expect_version 3.6.0-alpha.46 3.5.3 workflow_dispatch prerelease 3.6.0 '' 46
expect_version 3.5.3-rc.1 3.5.3 workflow_dispatch prerelease '' rc.1 47

expect_invalid 3.5.3 push prerelease '' '' 42
expect_invalid 3.5.3 workflow_dispatch unknown '' '' 42
expect_invalid 3.5.3 workflow_dispatch prerelease '' '' ''
expect_invalid 3.5.3 workflow_dispatch prerelease '' '' 0
expect_invalid 3.5.3 workflow_dispatch prerelease '' '' 01
expect_invalid 3.5.3 workflow_dispatch stable 3.5 '' 42
expect_invalid 3.5.3 workflow_dispatch stable 03.5.3 '' 42
expect_invalid 3.5.3 workflow_dispatch stable '$(echo unexpected)' '' 42
expect_invalid 3.5.3 workflow_dispatch prerelease '' 'alpha.1+metadata' 42
expect_invalid 3.5.3 workflow_dispatch prerelease '' 'alpha..1' 42
expect_invalid 3.5.3 workflow_dispatch prerelease '' 'alpha.01' 42

echo "Package-version checks passed."
