#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 6 ]; then
  echo "Usage: $0 <default-prefix> <event> <release-kind> <prefix-override> <suffix-override> <publish-run-number>" >&2
  exit 1
fi

default_prefix="$1"
event="$2"
release_kind="$3"
version_prefix="${4:-$default_prefix}"
version_suffix="$5"
publish_run_number="$6"

case "$event" in
  workflow_run)
    release_kind="prerelease"
    version_prefix="$default_prefix"
    version_suffix=""
    ;;
  workflow_dispatch) ;;
  *) echo "Unsupported publishing event: $event" >&2; exit 1 ;;
esac

if [[ ! "$version_prefix" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo "Version prefix must be a SemVer major.minor.patch: $version_prefix" >&2
  exit 1
fi

case "$release_kind" in
  stable) platform_version="$version_prefix" ;;
  prerelease)
    if [ -z "$version_suffix" ]; then
      if [[ ! "$publish_run_number" =~ ^[1-9][0-9]*$ ]]; then
        echo "A positive publishing-workflow run number is required for automatic suffixes." >&2
        exit 1
      fi
      version_suffix="alpha.$publish_run_number"
    fi
    if [[ ! "$version_suffix" =~ ^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$ ]]; then
      echo "Invalid SemVer prerelease suffix: $version_suffix" >&2
      exit 1
    fi
    IFS='.' read -r -a identifiers <<< "$version_suffix"
    for identifier in "${identifiers[@]}"; do
      if [[ "$identifier" =~ ^[0-9]+$ && "$identifier" =~ ^0[0-9]+$ ]]; then
        echo "Numeric prerelease identifiers cannot have leading zeroes: $version_suffix" >&2
        exit 1
      fi
    done
    platform_version="$version_prefix-$version_suffix"
    ;;
  *) echo "Release kind must be stable or prerelease: $release_kind" >&2; exit 1 ;;
esac

printf 'release_kind=%s\nversion_prefix=%s\nplatform_version=%s\n' "$release_kind" "$version_prefix" "$platform_version"
