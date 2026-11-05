#!/usr/bin/env bash
# GitHub's dependency graph does not read bun.lock, and Dependabot's alerts on the toolbelt's
# uv.lock can lag by days (ADR 0147), so this scan reads three library trees of the image: the
# app's npm tree, and the toolbelt's Python venv and npm tree, which a pull request builds from
# source (ADR 0149). It reads the packages installed there, whatever lockfile produced them.
# Each floor proves the scan saw its tree: a scan that reads nothing passes trivially.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <image-ref>" >&2
  exit 2
fi

IMAGE_REF="$1"
TRIVY_IMAGE="aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"
APP_NPM_TREE="/app/node_modules"
TOOLBELT_PYTHON_TREE="/opt/venv"
TOOLBELT_NPM_TREE="/opt/node/node_modules"
MIN_APP_NPM_PACKAGES=50
MIN_TOOLBELT_PYTHON_PACKAGES=30
MIN_TOOLBELT_NPM_PACKAGES=10
WORK_DIR="$(mktemp -d)"
CONTAINER_ID=""
cleanup() {
  if [ -n "$CONTAINER_ID" ]; then
    docker rm "$CONTAINER_ID" >/dev/null
  fi
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

CONTAINER_ID="$(docker create "$IMAGE_REF")"
mkdir -p "$WORK_DIR/out"
for tree in "$APP_NPM_TREE" "$TOOLBELT_PYTHON_TREE" "$TOOLBELT_NPM_TREE"; do
  mkdir -p "$WORK_DIR/tree$(dirname "$tree")"
  docker cp "$CONTAINER_ID:$tree" "$WORK_DIR/tree$tree"
done

docker run --rm \
  -v "$WORK_DIR/tree:/tree:ro" \
  -v "$WORK_DIR/out:/out" \
  "$TRIVY_IMAGE" \
  rootfs --quiet --scanners vuln --pkg-types library --list-all-pkgs \
  --severity MEDIUM,HIGH,CRITICAL --ignore-unfixed \
  --format json --output /out/scan.json /tree

packages_under() {
  jq --arg tree "${1#/}/" \
    '[.Results[]? | .Packages[]? | select((.FilePath // "") | startswith($tree))] | length' \
    "$WORK_DIR/out/scan.json"
}

app_npm_packages="$(packages_under "$APP_NPM_TREE")"
toolbelt_python_packages="$(packages_under "$TOOLBELT_PYTHON_TREE")"
toolbelt_npm_packages="$(packages_under "$TOOLBELT_NPM_TREE")"
findings="$(jq -r '
  [.Results[]? | .Target as $target | .Vulnerabilities[]?
    | "| \(.Severity) | \(.PkgName) \(.InstalledVersion) | \(.FixedVersion) | \(.VulnerabilityID) | \(.PkgPath // $target) |"]
  | unique | .[]' "$WORK_DIR/out/scan.json")"

report="### Library scan of ${IMAGE_REF} (${APP_NPM_TREE}, ${TOOLBELT_PYTHON_TREE}, ${TOOLBELT_NPM_TREE})

app npm packages read: ${app_npm_packages}
toolbelt Python packages read: ${toolbelt_python_packages}
toolbelt npm packages read: ${toolbelt_npm_packages}
"
if [ -n "$findings" ]; then
  report+="
| Severity | Package | Fixed in | Advisory | Path |
|---|---|---|---|---|
${findings}
"
fi
printf '%s\n' "$report"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  printf '%s\n' "$report" >>"$GITHUB_STEP_SUMMARY"
fi

unread=0
require_floor() {
  local read="$1" floor="$2" what="$3" tree="$4"
  if [ "$read" -lt "$floor" ]; then
    echo "the scan read ${read} ${what}, under the floor of ${floor}: it did not see ${tree}" >&2
    unread=1
  fi
}
require_floor "$app_npm_packages" "$MIN_APP_NPM_PACKAGES" "app npm packages" "$APP_NPM_TREE"
require_floor "$toolbelt_python_packages" "$MIN_TOOLBELT_PYTHON_PACKAGES" "toolbelt Python packages" "$TOOLBELT_PYTHON_TREE"
require_floor "$toolbelt_npm_packages" "$MIN_TOOLBELT_NPM_PACKAGES" "toolbelt npm packages" "$TOOLBELT_NPM_TREE"
if [ "$unread" -ne 0 ]; then
  exit 1
fi
if [ -n "$findings" ]; then
  echo "fixable medium-or-worse vulnerabilities in the scanned library trees; move each package to its fixed version, in its lock under toolbelt/ for a path under opt/" >&2
  exit 1
fi
