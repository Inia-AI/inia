#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
docker build --quiet --target dev -t agent-runtime:dev . > /dev/null
exec docker run --rm -v "$PWD:/repo:ro" -w /repo agent-runtime:dev bun scripts/check-docs.ts
