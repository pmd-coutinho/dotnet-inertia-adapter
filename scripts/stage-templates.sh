#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "Usage: $0 <source-dir> <staging-dir> <inertianet-package-version>" >&2
  exit 1
fi

SOURCE_DIR="$1"
STAGING_DIR="$2"
PACKAGE_VERSION="$3"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"
cp -R "$SOURCE_DIR"/. "$STAGING_DIR"/
cp "$REPO_ROOT/Directory.Build.props" "$STAGING_DIR/Directory.Build.props"
mkdir -p "$STAGING_DIR/assets/nuget"
cp "$REPO_ROOT/assets/nuget/icon.png" "$STAGING_DIR/assets/nuget/icon.png"

while IFS= read -r -d '' project_file; do
  perl -0pi -e "s/__INERTIANET_PACKAGE_VERSION__/$PACKAGE_VERSION/g" "$project_file"
done < <(find "$STAGING_DIR/templates" -name 'InertiaNetStarter.csproj' -print0)
