#!/usr/bin/env bash
# Captures the runner-side facts behind the sandbox-sensitive CI flakes: the kernel
# and runner image, the user-namespace sysctls, this process's thread/seccomp/capability state,
# AppArmor and the docker daemon's security options, then two bwrap probes inside the gate
# container under the exact security options the quality gates run with — a direct one and a
# nested user namespace from inside the sandbox that writes its setgroups and id maps, which is the
# step the SDK's apply-seccomp helper takes and the runtime's own pre-run probe repeats. Runs only
# when `check` fails, or on demand via the workflow's capture_diagnostics input.
set -u

image="${1:-agent-runtime:dev}"
seccomp_profile="${2:-$PWD/sandbox/seccomp-bwrap.json}"

group() { printf '\n::group::%s\n' "$1"; }
endgroup() { printf '::endgroup::\n'; }

group "runner: kernel, image, cpus"
uname -a
echo "ImageOS=${ImageOS:-unset} ImageVersion=${ImageVersion:-unset} RUNNER_ARCH=${RUNNER_ARCH:-unset}"
nproc
free -m | head -2
endgroup

group "runner: user-namespace sysctls"
sysctl kernel.apparmor_restrict_unprivileged_userns user.max_user_namespaces kernel.unprivileged_userns_clone 2>&1 || true
endgroup

group "runner: /proc/self/status"
grep -E '^(Threads|Seccomp|Seccomp_filters|NoNewPrivs|CapInh|CapPrm|CapEff|CapBnd|CapAmb):' /proc/self/status
endgroup

group "runner: apparmor"
for entry in /proc/sys/kernel/apparmor*; do
  printf '%s: ' "$entry"
  cat "$entry" 2>&1 || true
done
printf '/sys/module/apparmor/parameters/enabled: '
cat /sys/module/apparmor/parameters/enabled 2>&1 || true
sudo grep -E '^(unpriv_)?bwrap ' /sys/kernel/security/apparmor/profiles 2>&1 || echo "no bwrap profile loaded"
endgroup

group "docker: daemon security options"
docker version --format 'server {{.Server.Version}}' 2>&1 || true
docker info --format '{{json .SecurityOptions}}' 2>&1 || true
endgroup

group "gate container: direct and nested bwrap probes"
docker run --rm \
  --security-opt seccomp="$seccomp_profile" \
  --security-opt systempaths=unconfined \
  --security-opt apparmor=unconfined \
  --security-opt no-new-privileges \
  "$image" sh -c '
    echo "uid=$(id -u) home=$HOME"
    grep -E "^(Threads|Seccomp|NoNewPrivs|CapEff|CapAmb):" /proc/self/status
    printf "/proc/sys/user/max_user_namespaces: "; cat /proc/sys/user/max_user_namespaces
    probe="setpriv --inh-caps=-all --ambient-caps=-all bwrap --new-session --die-with-parent --unshare-net --ro-bind / / --dev /dev --unshare-pid --unshare-user --cap-drop ALL --proc /proc --tmpfs /tmp"
    if $probe -- /bin/true; then echo "direct bwrap probe: ok"; else echo "direct bwrap probe: FAILED (exit $?)"; fi
    if $probe -- unshare --user --map-root-user /bin/true; then echo "nested userns inside the sandbox: ok"; else echo "nested userns inside the sandbox: FAILED (exit $?)"; fi
  ' 2>&1 || true
endgroup
