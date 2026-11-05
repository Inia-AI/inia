#!/usr/bin/env bash
# EXPECT_VERSION / EXPECT_REVISION, when set, additionally require /healthz to
# name that build identity, so a release can't publish an image that silently
# misreports its own build.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <image-ref> <platform> [extra docker-run args...]" >&2
  exit 2
fi

IMAGE_REF="$1"
PLATFORM="$2"
shift 2
NAME="smoke-${PLATFORM//\//-}"

cleanup() {
  docker rm -f "$NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT
cleanup

docker run -d --name "$NAME" --platform "$PLATFORM" \
  --read-only \
  --tmpfs /tmp:size=512m,exec \
  --tmpfs /home/bun:uid=1000,gid=1000,mode=700,size=64m \
  --tmpfs /workspaces:mode=1777,size=3g \
  --cap-drop ALL --cap-add SETUID --cap-add SETGID \
  --security-opt no-new-privileges \
  -e CLAUDE_CODE_USE_BEDROCK=1 \
  -e ANTHROPIC_MODEL=smoke-test-pin \
  -e AUTH_DISABLED=true \
  -e AGENT_RUNTIME_AMBIENT_CREDENTIALS=true \
  "$@" \
  "$IMAGE_REF" >/dev/null

STATUS="unknown"
for _ in $(seq 1 90); do
  RUNNING="$(docker inspect -f '{{.State.Running}}' "$NAME" 2>/dev/null || echo false)"
  STATUS="$(docker inspect -f '{{.State.Health.Status}}' "$NAME" 2>/dev/null || echo unknown)"
  if [ "$STATUS" = "healthy" ]; then
    break
  fi
  if [ "$RUNNING" != "true" ] || [ "$STATUS" = "unhealthy" ]; then
    break
  fi
  sleep 2
done

if [ "$STATUS" != "healthy" ]; then
  echo "$PLATFORM: container never became healthy (running=${RUNNING:-unknown}, health=${STATUS})" >&2
  docker logs "$NAME" >&2 || true
  exit 1
fi

echo "$PLATFORM: healthy"

if [ -z "${EXPECT_VERSION:-}" ] && [ -z "${EXPECT_REVISION:-}" ]; then
  echo "$PLATFORM: build identity not asserted (EXPECT_VERSION/EXPECT_REVISION unset)"
  exit 0
fi

if ! SERVED="$(docker exec "$NAME" curl -fsS http://127.0.0.1:8130/healthz)"; then
  echo "$PLATFORM: could not fetch /healthz from the healthy container" >&2
  exit 1
fi
echo "$PLATFORM healthz: ${SERVED}"
SERVED_VERSION="$(jq -r '.version // "absent"' <<< "$SERVED" 2>/dev/null || echo "absent")"
SERVED_REVISION="$(jq -r '.revision // "absent"' <<< "$SERVED" 2>/dev/null || echo "absent")"
if [ -n "${EXPECT_VERSION:-}" ] && [ "$SERVED_VERSION" != "$EXPECT_VERSION" ]; then
  echo "$PLATFORM: healthz version is ${SERVED_VERSION}, expected ${EXPECT_VERSION}" >&2
  exit 1
fi
if [ -n "${EXPECT_REVISION:-}" ] && [ "$SERVED_REVISION" != "$EXPECT_REVISION" ]; then
  echo "$PLATFORM: healthz revision is ${SERVED_REVISION}, expected ${EXPECT_REVISION}" >&2
  exit 1
fi
echo "$PLATFORM: build identity confirmed"
