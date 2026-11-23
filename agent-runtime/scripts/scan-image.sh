#!/usr/bin/env bash
# The release and the weekly base rebuild gate on this scan, so both read one Trivy version and one
# rule: a high or critical finding that has a fix stops the run.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <image-ref>" >&2
  exit 2
fi

TRIVY_IMAGE="aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f"

docker run --rm \
  -v "$HOME/.docker/config.json:/root/.docker/config.json:ro" \
  "$TRIVY_IMAGE" \
  image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 "$1"
