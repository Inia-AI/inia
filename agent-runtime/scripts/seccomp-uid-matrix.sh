#!/usr/bin/env bash
# Proves on the running kernel that the supervisor cannot become uid 0 under
# sandbox/seccomp-bwrap.json, and that the uid changes the runtime makes still work.
#
#   ./scripts/seccomp-uid-matrix.sh [image]              a container of the image, with the deployed flags
#   ./scripts/seccomp-uid-matrix.sh --container <name>   a running runtime container, under its own profile
#   ssh <host> 'bash -s -- --container <name>' < scripts/seccomp-uid-matrix.sh
#
# Every process it starts takes the container's own entrypoint, so it has the
# supervisor's identity: uid 1000 with SETUID and SETGID in its ambient set.
#
# The matrix calls every uid syscall with every mix of three kinds of argument,
# each call in a forked child:
#   - uid 0 as the kernel reads it: 0, and 2^32 and 7 * 2^32, whose low 32 bits are 0;
#   - a uid the runtime never uses: 5, 65536 (uid 0 to the 16-bit calls of 32-bit
#     x86) and 2^32 + 1000;
#   - a uid the profile allows: 1000, both ends of its range, and -1 for "leave
#     unchanged".
# Only a call whose every argument is of the third kind may work. The ends of the
# range are never a live run's uid, so the matrix is safe inside a running
# container: the runtime kills every process of a uid whose run ends. The script
# then reads the loopback uid and the pool from the image's own config and requires
# them to sit inside the range.

set -uo pipefail

read -r -d '' MATRIX << 'PYTHON'
import ctypes
import errno
import itertools
import os
import platform
import sys

SYSCALL_NUMBERS = {
    "aarch64": {"setuid": 146, "setreuid": 145, "setresuid": 147, "setfsuid": 151},
    "x86_64": {"setuid": 105, "setreuid": 113, "setresuid": 117, "setfsuid": 122},
}
UID_ARG_COUNTS = {"setuid": 1, "setfsuid": 1, "setreuid": 2, "setresuid": 3}
HIGH_BIT = 1 << 32
ROOT_UIDS = [0, HIGH_BIT, 7 * HIGH_BIT]
UNUSED_UIDS = [5, 1 << 16, HIGH_BIT + 1000]
LEAVE_UNCHANGED = 0xFFFFFFFF
ALLOWED_UIDS = [int(uid) for uid in sys.argv[1:]] + [LEAVE_UNCHANGED]
KERNEL_FILES = [
    "/proc/sys/kernel/core_pattern",
    "/proc/sys/kernel/modprobe",
    "/proc/sysrq-trigger",
    "/proc/sys/vm/drop_caches",
]

libc = ctypes.CDLL(None, use_errno=True)


def opens_for_writing(path):
    # Opens and closes without writing: the kernel's permission check runs at open.
    try:
        os.close(os.open(path, os.O_WRONLY | os.O_APPEND))
        return True
    except OSError:
        return False


def call_in_child(number, args):
    read_end, write_end = os.pipe()
    pid = os.fork()
    if pid == 0:
        result = libc.syscall(ctypes.c_long(number), *[ctypes.c_ulong(arg) for arg in args])
        failure = ctypes.get_errno()
        writable = sum(opens_for_writing(path) for path in KERNEL_FILES)
        os.write(write_end, f"{result} {failure} {writable}".encode())
        os._exit(0)
    # Closed before the read, so a child that died without writing gives an empty
    # read, not a wait that never ends.
    os.close(write_end)
    os.waitpid(pid, 0)
    answer = os.read(read_end, 64).decode().split()
    os.close(read_end)
    return [int(field) for field in answer] if len(answer) == 3 else None


numbers = SYSCALL_NUMBERS[platform.machine()]
wrong = 0
total = 0
for name, arg_count in UID_ARG_COUNTS.items():
    for args in itertools.product(ROOT_UIDS + UNUSED_UIDS + ALLOWED_UIDS, repeat=arg_count):
        total += 1
        shown = f"{name}({', '.join(hex(arg) for arg in args)})"
        answer = call_in_child(numbers[name], args)
        if answer is None:
            wrong += 1
            print(f"NO ANSWER from {shown}")
            continue
        result, failure, writable = answer
        may_work = all(arg in ALLOWED_UIDS for arg in args)
        refused = result == -1 and failure == errno.EPERM
        if refused and may_work:
            wrong += 1
            print(f"WRONGLY REFUSED {shown}")
        elif not refused and not may_work:
            wrong += 1
            print(f"NOT REFUSED {shown}: returned {result}, errno {failure}")
        if writable:
            wrong += 1
            print(f"KERNEL FILE WRITABLE after {shown}: {writable} of {len(KERNEL_FILES)}")
print(f"uid matrix on {platform.machine()} as uid {os.getuid()}: {total} calls, {wrong} wrong")
sys.exit(1 if wrong else 0)
PYTHON

failures=0
expect() {
  local verdict="$1" label="$2"
  shift 2
  local output status
  output=$("$@" 2>&1)
  status=$?
  if { [ "$verdict" = pass ] && [ "$status" -eq 0 ]; } || { [ "$verdict" = fail ] && [ "$status" -ne 0 ]; }; then
    echo "ok: $label"
    if [[ "$output" == *"uid matrix on"* ]]; then
      echo "    ${output##*$'\n'}"
    fi
  else
    failures=$((failures + 1))
    echo "WRONG: $label (wanted $verdict, exit $status)"
    echo "$output" | tail -20
  fi
}

start_container() {
  docker run -d --rm --network none --read-only --tmpfs /tmp:size=64m,exec \
    --security-opt "seccomp=$1" --security-opt systempaths=unconfined \
    --security-opt apparmor=unconfined --security-opt no-new-privileges:true \
    --cap-drop ALL --cap-add SETUID --cap-add SETGID --cap-add KILL \
    "$IMAGE" sleep infinity
}

STARTED=()
remove_started() {
  if [ "${#STARTED[@]}" -gt 0 ]; then
    docker rm -f "${STARTED[@]}" > /dev/null
  fi
}
trap remove_started EXIT

UNCONFINED=""
if [ "${1:-}" = "--container" ]; then
  CONTAINER="${2:?--container needs a container name}"
else
  IMAGE="${1:-agent-runtime:dev}"
  PROFILE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/../sandbox/seccomp-bwrap.json"
  CONTAINER=$(start_container "$PROFILE") || exit 1
  STARTED+=("$CONTAINER")
  UNCONFINED=$(start_container unconfined) || exit 1
  STARTED+=("$UNCONFINED")
fi

ENTRYPOINT=()
while IFS= read -r word; do
  if [ -n "$word" ]; then
    ENTRYPOINT+=("$word")
  fi
done < <(docker inspect -f '{{range .Config.Entrypoint}}{{println .}}{{end}}' "$CONTAINER")
if [ "${#ENTRYPOINT[@]}" -eq 0 ]; then
  echo "WRONG: could not read the entrypoint of container $CONTAINER"
  exit 1
fi

# stdin is /dev/null unless a caller pipes something in: under `bash -s` the script
# itself is stdin, and `docker exec -i` would eat the rest of it.
as_supervisor_in() {
  local container="$1"
  shift
  docker exec -i "$container" "${ENTRYPOINT[@]}" "$@"
}
as_supervisor() { as_supervisor_in "$CONTAINER" "$@" < /dev/null; }

ALLOWED_RANGE=(16384 32767)
matrix_in() { printf '%s\n' "$MATRIX" | as_supervisor_in "$1" python3 - 1000 "${ALLOWED_RANGE[@]}"; }
# A pass must be the profile's doing: without it, the same matrix has to see uid 0 taken.
control_reaches_root() {
  local report
  report=$(matrix_in "$1")
  grep -q '^NOT REFUSED setuid(0x0)' <<< "$report"
}
runtime_uids_in_range() {
  local loopback_uid last_pool_uid
  read -r loopback_uid last_pool_uid < <(as_supervisor bun -e '
    import { UID_POOL_BASE, UID_POOL_SIZE } from "/app/src/config.ts";
    import { LOOPBACK_PROCESS_UID } from "/app/src/loopback-process.ts";
    console.log(LOOPBACK_PROCESS_UID, UID_POOL_BASE + UID_POOL_SIZE - 1);
  ')
  echo "loopback uid ${loopback_uid:-unread}, last pool uid ${last_pool_uid:-unread}, allowed ${ALLOWED_RANGE[*]}"
  [ -n "${last_pool_uid:-}" ] && [ "$loopback_uid" -ge "${ALLOWED_RANGE[0]}" ] && [ "$last_pool_uid" -le "${ALLOWED_RANGE[1]}" ]
}
AS_RUN=(setpriv "--reuid=${ALLOWED_RANGE[0]}" "--regid=${ALLOWED_RANGE[0]}" --clear-groups
  --inh-caps=-all --ambient-caps=-all --)

if [ -n "$UNCONFINED" ]; then
  expect pass "control: with seccomp unconfined the matrix reaches uid 0" control_reaches_root "$UNCONFINED"
fi
expect pass "only a uid the profile allows works, in every uid call" matrix_in "$CONTAINER"
expect pass "the runtime's loopback uid and run pool sit inside the allowed range" runtime_uids_in_range
expect fail "setpriv cannot switch the supervisor to uid 0" \
  as_supervisor setpriv --reuid=0 --regid=0 --clear-groups -- true
expect pass "the supervisor drops a run to its own uid" as_supervisor "${AS_RUN[@]}" true
expect pass "a run uid is root in its own user namespace" \
  as_supervisor "${AS_RUN[@]}" unshare --user --map-root-user true
expect pass "a run uid starts bubblewrap, and a nested user namespace inside it" \
  as_supervisor "${AS_RUN[@]}" \
  bwrap --unshare-user --unshare-pid --unshare-net --ro-bind / / --dev /dev --proc /proc --tmpfs /tmp -- \
  unshare --user --map-root-user true

if [ "$failures" -ne 0 ]; then
  echo "SECCOMP-UID VERDICT: FAIL ($failures wrong)"
  exit 1
fi
echo "SECCOMP-UID VERDICT: PASS"
