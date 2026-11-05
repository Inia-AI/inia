#!/bin/sh
# Makes the stack's secrets on its first start and keeps them in the secrets volume for every later
# start. Postgres takes its password only when it creates its data directory, so a new password
# would lock the platform out of the data it already has. Prints no secret.
set -eu

ROOT=/run/inia
DATABASE=inia

mkdir -p "$ROOT/postgres" "$ROOT/platform" "$ROOT/runtime"

keep() {
    if [ ! -s "$1" ]; then
        head -c 48 /dev/urandom | base64 | tr -d '\n/+=' > "$1.new"
        mv "$1.new" "$1"
        echo "Made $(basename "$(dirname "$1")")/$(basename "$1")"
    fi
}

keep "$ROOT/postgres/password"
keep "$ROOT/platform/Jwt__SecretKey"
keep "$ROOT/platform/OpenIddict__ClientSecret"
keep "$ROOT/runtime/AUTH_TOKEN"

cp "$ROOT/runtime/AUTH_TOKEN" "$ROOT/platform/PlatformSettings__AgentRuntime__AuthToken"
for context in CoreDbConnection UserDataDbConnection; do
    printf 'Host=postgres;Port=5432;Database=%s;Username=%s;Password=%s' \
        "$DATABASE" "$DATABASE" "$(cat "$ROOT/postgres/password")" > "$ROOT/platform/ConnectionStrings__$context"
done

# Compose cannot hand a file to the runtime, whose file system is read-only, so the runtime reads
# this copy of the key. Every start refreshes it.
cp /run/configs/anthropic_api_key "$ROOT/runtime/ANTHROPIC_API_KEY"

# The runtime reads its files as uid 1000; every other service reads as root.
chmod 0400 "$ROOT"/postgres/* "$ROOT"/platform/* "$ROOT"/runtime/*
chown 1000:1000 "$ROOT"/runtime/*
