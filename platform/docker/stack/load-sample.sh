#!/bin/sh
# Loads the sample workflows of samples/ on the first start, so a fresh install lists them ready to
# run. A later start leaves a loaded sample as it is, edited or deleted, and signs in only when a
# sample is still to load, so a changed admin password does not stop it. A failed load exits 0,
# because the Vue app waits for this service: the platform starts without that sample, and the next
# start tries again.
set -eu

API=http://api:80/api/v1
TENANT=IniaAI
STATE=/state
RESPONSE=/tmp/response
SAMPLES="SampleAnnualReport:annual-report SampleBlockShowcase:block-showcase SampleAgentRun:agent-run"

starts_without() {
    echo "$1 The platform starts without $2, and the next start tries again."
}

pending=""
for sample in $SAMPLES; do
    type="${sample%%:*}"
    if [ -f "$STATE/$type" ]; then
        echo "The sample workflow $type was loaded on an earlier start."
    else
        pending="$pending $sample"
    fi
done
if [ -z "$pending" ]; then
    exit 0
fi

token="$(curl -fsS -X POST "$API/connect/token" -H "X-Tenant-Id: $TENANT" \
    --data-urlencode grant_type=password \
    --data-urlencode "username=${INIA_ADMIN_EMAIL:-superadmin@example.com}" \
    --data-urlencode password@/run/configs/admin_password \
    --data-urlencode scope=api \
    | sed -n 's/.*"access_token": *"\([^"]*\)".*/\1/p')"
if [ -z "$token" ]; then
    starts_without "The first admin could not sign in with INIA_ADMIN_PASSWORD." "the sample workflows that are not loaded yet"
    exit 0
fi

for sample in $pending; do
    type="${sample%%:*}"
    content="$API/workflow-templates/$type/content"
    status="$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" -H "X-Tenant-Id: $TENANT" "$content" || true)"
    if [ "$status" = 200 ]; then
        echo "The sample workflow $type is already there."
    elif curl -sS --fail-with-body -o "$RESPONSE" -X PUT -H "Authorization: Bearer $token" -H "X-Tenant-Id: $TENANT" \
        -H 'Content-Type: application/json' --data @"/samples/${sample#*:}/workflow.json" "$content"; then
        echo "Loaded the sample workflow $type."
    else
        cat "$RESPONSE" 2>/dev/null && echo
        starts_without "The platform refused the sample workflow $type." "it"
        continue
    fi
    touch "$STATE/$type"
done
