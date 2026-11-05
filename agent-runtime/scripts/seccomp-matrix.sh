#!/usr/bin/env bash
# A case passes only if the sentinel reaches stdout AND the exit code is 0 --
# bwrap exits non-zero for unrelated reasons too, so a non-zero exit alone is
# not evidence of anything.
#
# Run `sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0` on the
# host first, or every case fails regardless of seccomp/systempaths.

set -uo pipefail

IMAGE="${1:-agent-runtime:dev}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROFILE="$SCRIPT_DIR/../sandbox/seccomp-bwrap.json"
TIMEOUT_S=30

# macOS ships no `timeout`; cases are sub-second, so run unguarded there
# rather than refusing to run at all.
WATCHDOG=""
if command -v timeout >/dev/null 2>&1; then
  WATCHDOG="timeout $TIMEOUT_S"
fi

seccomp_opt() {
  case "$1" in
    default) echo "" ;;
    custom) echo "--security-opt seccomp=$PROFILE" ;;
    custom_unconfined_paths) echo "--security-opt seccomp=$PROFILE --security-opt systempaths=unconfined" ;;
    unconfined_unconfined_paths) echo "--security-opt seccomp=unconfined --security-opt systempaths=unconfined" ;;
  esac
}

fs_opt() {
  case "$1" in
    plain) echo "" ;;
    readonly_tmp) echo "--read-only --tmpfs /tmp" ;;
  esac
}

bwrap_flags() {
  case "$1" in
    full_matrix) echo "--unshare-user --unshare-pid --unshare-net --ro-bind / / --dev /dev --proc /proc --tmpfs /tmp --" ;;
    sdk_flags_bare) echo "--unshare-user --unshare-pid --unshare-net --" ;;
    sdk_flags_with_bind) echo "--unshare-user --unshare-pid --unshare-net --ro-bind / / --" ;;
  esac
}

# Expected verdict per (seccomp, command). The filesystem mode deliberately
# does not appear: that it never changes the outcome is itself a finding.
#
# sdk_flags_bare is a negative control: bwrap starts from an empty mount
# namespace, so /bin/echo cannot resolve without a bind regardless of seccomp
# -- distinguishing this structural failure from an actually blocked syscall.
expected_verdict() {
  case "$1__$2" in
    default__*) echo FAIL ;;
    custom__full_matrix) echo FAIL ;;
    custom__sdk_flags_bare) echo FAIL ;;
    custom__sdk_flags_with_bind) echo PASS ;;
    custom_unconfined_paths__sdk_flags_bare) echo FAIL ;;
    custom_unconfined_paths__*) echo PASS ;;
    unconfined_unconfined_paths__sdk_flags_bare) echo FAIL ;;
    unconfined_unconfined_paths__*) echo PASS ;;
  esac
}

printf '%-30s %-14s %-20s %-8s %-8s %s\n' SECCOMP FS COMMAND VERDICT EXPECTED RESULT
mismatches=0
total=0

for seccomp_id in default custom custom_unconfined_paths unconfined_unconfined_paths; do
  for fs_id in plain readonly_tmp; do
    for cmd_id in full_matrix sdk_flags_bare sdk_flags_with_bind; do
      total=$((total + 1))
      sentinel="SECCOMP_OK_${seccomp_id}_${fs_id}_${cmd_id}_$$"
      # shellcheck disable=SC2086
      stdout=$($WATCHDOG docker run --rm --user 1000:1000 \
        --entrypoint bwrap \
        --security-opt apparmor=unconfined \
        $(fs_opt "$fs_id") $(seccomp_opt "$seccomp_id") \
        "$IMAGE" $(bwrap_flags "$cmd_id") /bin/echo "$sentinel" 2>/tmp/seccomp-matrix-err)
      code=$?

      verdict=FAIL
      if [[ $code -eq 0 && "$stdout" == *"$sentinel"* ]]; then
        verdict=PASS
      fi
      want=$(expected_verdict "$seccomp_id" "$cmd_id")
      result=ok
      if [[ "$verdict" != "$want" ]]; then
        result="MISMATCH: $(head -c 120 /tmp/seccomp-matrix-err | tr -d '\n')"
        mismatches=$((mismatches + 1))
      fi
      printf '%-30s %-14s %-20s %-8s %-8s %s\n' \
        "$seccomp_id" "$fs_id" "$cmd_id" "$verdict" "$want" "$result"
    done
  done
done

echo
echo "$total cases, $mismatches mismatched against the recorded aarch64 baseline"
[[ $mismatches -eq 0 ]]
