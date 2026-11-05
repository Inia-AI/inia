#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
credentials_json=$(aws configure export-credentials --profile "${AWS_PROFILE:-default}")
AWS_ACCESS_KEY_ID=$(printf '%s' "$credentials_json" | python3 -c 'import json, sys; print(json.load(sys.stdin)["AccessKeyId"])')
AWS_SECRET_ACCESS_KEY=$(printf '%s' "$credentials_json" | python3 -c 'import json, sys; print(json.load(sys.stdin)["SecretAccessKey"])')
AWS_SESSION_TOKEN=$(printf '%s' "$credentials_json" | python3 -c 'import json, sys; print(json.load(sys.stdin).get("SessionToken", ""))')
AWS_CREDENTIAL_EXPIRATION=$(printf '%s' "$credentials_json" | python3 -c 'import json, sys; print(json.load(sys.stdin).get("Expiration", ""))')
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_CREDENTIAL_EXPIRATION
if [ -z "$AWS_SESSION_TOKEN" ]; then
  echo "error: the exported credentials carry no session token, and the imds-mock sidecar serves session credentials only" >&2
  echo "use an SSO profile (aws sso login); for trusted documents only, the ambient break-glass in README.md remains" >&2
  exit 1
fi
if [ -n "$AWS_CREDENTIAL_EXPIRATION" ]; then
  python3 - "$AWS_CREDENTIAL_EXPIRATION" <<'PY' || true
import datetime
import sys

expiry = datetime.datetime.fromisoformat(sys.argv[1].replace("Z", "+00:00"))
remaining_minutes = int((expiry - datetime.datetime.now(datetime.timezone.utc)).total_seconds() // 60)
if remaining_minutes < 0:
    print(
        "warning: the exported AWS credentials have already expired; "
        "runs will fail — run 'aws sso login' and restart ./scripts/dev.sh",
        file=sys.stderr,
    )
elif remaining_minutes < 15:
    print(
        f"warning: the exported AWS credentials expire in {remaining_minutes} minute(s); "
        "runs will start failing then — run 'aws sso login' and restart ./scripts/dev.sh",
        file=sys.stderr,
    )
PY
fi
exec docker compose "$@"
