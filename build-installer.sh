#!/usr/bin/env bash
# Builds a one-click VPM installer .unitypackage for a package in this repo, using anatawa12's
# VPM Package Auto Installer (github.com/anatawa12/vpm-package-auto-installer, MIT licensed).
#
# Usage:
#   ./build-installer.sh <package-id> [min-version]
#
# Example:
#   ./build-installer.sh dev.zeroscalecutter 1.0.0
#   -> installers/dev.zeroscalecutter.unitypackage
#
# What it actually does: a .unitypackage is just a gzipped tar of GUID-named folders, each
# holding an "asset" file, an "asset.meta" file, and a "pathname" file. Every installer we ship
# is three of those folders: the containing Assets/ subfolder, the auto-installer DLL itself
# (byte-identical across every package - it's generic, unmodified third-party code), and a
# config.json that names which VPM repository to add and which package to install. Only that
# last one actually differs per package, so this script takes any existing installer as a
# template, finds the config.json entry inside it (by pathname, not a hardcoded GUID, so it
# still works if the template changes), replaces its content, and re-packs the archive.
#
# Run this once per NEW package. You never need to re-run it for a new VERSION of a package
# that's already got an installer - config.json uses an open ">=" version range, so VCC always
# offers whatever the latest release actually is. Attach the output to that package's FIRST
# GitHub Release only (gh release upload <first-tag> installers/<package-id>.unitypackage) -
# every later release can leave it alone.

set -euo pipefail

PACKAGE_ID="${1:?Usage: $0 <package-id> [min-version]}"
MIN_VERSION="${2:-1.0.0}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATE="${TEMPLATE:-$REPO_ROOT/installers/dev.materialatlaser.unitypackage}"
OUTPUT="$REPO_ROOT/installers/${PACKAGE_ID}.unitypackage"
VPM_REPO_URL="https://raw.githubusercontent.com/pom-vrc/503/master/index.json"

if [ ! -f "$TEMPLATE" ]; then
    echo "Template installer not found at $TEMPLATE" >&2
    echo "Point \$TEMPLATE at any existing installer .unitypackage in this repo, e.g.:" >&2
    echo "  TEMPLATE=installers/dev.bonemerger.unitypackage ./build-installer.sh $PACKAGE_ID" >&2
    exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

tar --force-local -xzf "$TEMPLATE" -C "$WORK"

CONFIG_DIR=""
for d in "$WORK"/*/; do
    if [ -f "$d/pathname" ] && grep -q "config\.json$" "$d/pathname"; then
        CONFIG_DIR="$d"
        break
    fi
done
if [ -z "$CONFIG_DIR" ]; then
    echo "Could not find the config.json entry inside $TEMPLATE - has the installer format changed?" >&2
    exit 1
fi

cat > "${CONFIG_DIR}asset" << EOF
{
  "vpmRepositories": [
    "$VPM_REPO_URL"
  ],
  "vpmDependencies": {
    "$PACKAGE_ID": ">=$MIN_VERSION"
  }
}
EOF

( cd "$WORK" && tar --force-local -czf "$OUTPUT" */ )

echo "Built $OUTPUT"
echo "vpmDependencies: { \"$PACKAGE_ID\": \">=$MIN_VERSION\" }"
