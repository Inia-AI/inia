# Container seccomp profile for the SDK sandbox

The Claude Agent SDK executes sandboxed Bash commands through bubblewrap, which must create
user/pid/net namespaces and call `pivot_root` as an unprivileged process. Docker's default seccomp
profile denies exactly that, so the agent container runs under `seccomp-bwrap.json` — a minimal
derivative of the default profile — wired via `security_opt` in `compose.yaml` and via
`--security-opt` on the CI gate runs.

## Files

- `docker-default-seccomp.json` — pristine input: Docker's default profile, fetched from
  [moby/profiles](https://github.com/moby/profiles) `seccomp/default.json` at commit
  `f9bc03ec19b2dc4c091449b08e88f85c0caa9f0b` (2026-07-03).
- `make-custom-profile.jq` — the three-rule transform (see its header for what each rule does and
  why `clone3` is deliberately untouched).
- `seccomp-bwrap.json` — generated output. Never edit by hand; regenerate.

## Regenerate

```bash
docker compose run --rm -v "$PWD/sandbox:/sandbox" agent \
  sh -c 'jq -f /sandbox/make-custom-profile.jq /sandbox/docker-default-seccomp.json > /sandbox/seccomp-bwrap.json'
```

Run it through the container (jq 1.7 in the base image) so formatting is deterministic; CI diffs
the committed output against a fresh transform and fails on drift. For the same reason `biome.json`
excludes both JSON files: Biome's formatting differs from jq's, and the input stays byte for byte as
fetched.

## Four independent controls, all four required

Three are container options, one is a property of the host. None substitutes for another, and each
produces a different bwrap error — which is how you tell them apart instead of guessing:

| Control | Where | bwrap failure when missing |
|---|---|---|
| seccomp profile | `security_opt: seccomp=./sandbox/seccomp-bwrap.json` | `No permissions to create new namespace` |
| masked `/proc` paths | `security_opt: systempaths=unconfined` | fails at `--proc /proc` |
| AppArmor confinement | `security_opt: apparmor=unconfined` | `Failed to make / slave: Permission denied` |
| unprivileged userns | host `kernel.apparmor_restrict_unprivileged_userns=0` | `setting up uid map: Permission denied` |
| unprivileged userns, nested (Ubuntu 26.04) | host AppArmor profile `bwrap-userns-restrict` unloaded | `unshare: write failed /proc/self/uid_map: Operation not permitted` (the pre-run probe), `apply-seccomp: write /proc/self/setgroups … Permission denied` (the SDK helper) |
| *(none — SDK helper)* | not a control; see the fifth failure below | `apply-seccomp: unshare(CLONE_NEWUSER): Invalid argument` |

The table is code as well as prose: `src/sandbox-probe.ts` carries it as `SANDBOX_FAILURE_CATALOGUE`,
so a failed pre-run probe fails the run naming the control (`sandbox_unavailable: <name> — <control>:
<bwrap stderr>`), and the scripted contract run names the same cause when its retry is exhausted.
A new row here is a new catalogue entry there, and vice versa.

**AppArmor confinement**: Docker's `docker-default` profile carries a blanket `deny mount`, so
bubblewrap's `mount(MS_SLAVE|MS_REC)` on `/` is refused even when seccomp allows the syscall. Passing
the flag on a host without AppArmor is accepted and does nothing, so set it unconditionally. A custom
AppArmor profile — the analogue of what we do for seccomp — is not available: Docker can only
reference a profile already loaded on the host by `apparmor_parser`, making it host provisioning
rather than a repo artifact.

**Unprivileged userns** is a **host** requirement, not a container flag, and it is the subtle one.
Ubuntu 24.04 ships `kernel.apparmor_restrict_unprivileged_userns=1`, under which the user namespace
is still created but carries no privilege inside it, so the uid_map write and any `CAP_NET_ADMIN`
operation return EPERM. Two things make it easy to misdiagnose: the first symptom you meet is
usually the *later* one (`loopback: Failed RTM_NEWADDR`), and `--cap-add NET_ADMIN` does **not** help,
because the missing privilege is inside the namespace rather than in the container's capability set.
Probe with a bare `bwrap --unshare-user --ro-bind / / -- /bin/true`: if that fails on uid map, this is
your control.

Hosts differ, so check rather than assume: Amazon Linux 2023 has no AppArmor and needs nothing —
a spike ran bubblewrap there successfully. Any Ubuntu 24.04+ host needs the sysctl set by its
provisioning. CI runners are Ubuntu, so CI sets it explicitly; that makes CI *more*
representative of production, not less.

**Ubuntu 26.04 adds a second host step.** Its `apparmor` package loads
`/etc/apparmor.d/bwrap-userns-restrict`, which attaches a profile to `/usr/bin/bwrap` and runs
everything bwrap starts under `unpriv_bwrap`, a profile that denies every capability. The path
matches inside the container too, and `apparmor=unconfined` does not stop it: the profile attaches
when bwrap starts, after the container's own confinement is set. bwrap still builds its
namespaces, so a bare bwrap probe passes. What fails is the next step: the SDK's `apply-seccomp`
helper makes a nested user namespace on every Bash call and cannot write its
`/proc/self/setgroups`, which needs `CAP_SYS_ADMIN` in that namespace. The sysctl does not reach
this profile. Unload it and keep it unloaded:

```bash
sudo apparmor_parser -R /etc/apparmor.d/bwrap-userns-restrict
sudo ln -s /etc/apparmor.d/bwrap-userns-restrict /etc/apparmor.d/disable/
```

That lifts the restriction for every bwrap on the host, Flatpak's included. The sysctl has already
lifted it for every other program, so the host gives up nothing the first step had not. The
pre-run probe takes the helper's step itself (`unshare --user --map-root-user` inside the
sandbox, ADR 0159), so such a host fails each run before its first model turn as
`bwrap_apparmor_profile_loaded`. CI unloads the profile with `scripts/ci-permit-userns.sh`, which
fails when a bwrap profile is still loaded afterwards.


## A fifth failure that is none of the four controls

`apply-seccomp: unshare(CLONE_NEWUSER): Invalid argument` — **EINVAL, not EPERM**, surfacing as a
failed Bash tool call. Every row in the table above is provably innocent when this appears: the
message comes from the SDK's own `apply-seccomp` helper (native code embedded in the CLI binary,
re-exec'd as `/proc/self/fd/3` with `ARGV0=apply-seccomp` *inside* the bwrap sandbox), so bwrap's
own user namespace was already created, mounted, and executing by the time it printed. The helper
creates a *nested* user namespace before installing its seccomp filter, and
`unshare(CLONE_NEWUSER)` implies `CLONE_THREAD`, which the kernel refuses with EINVAL unless the
calling thread group holds exactly one thread. A helper thread racing startup therefore turns the
call into this exact error — intermittently, timing-dependent, observed 4 times in ~27 scripted-run
executions on GitHub-hosted runners across two runner images. Nothing in this repo can
fix the race; the scripted-run contract test retries it, bounded to that exact signature and loudly
logged with `/proc/sys/user/*` and `uid_map` diagnostics (`src/contracts/userns-flake.ts`), so a
persistent failure — or any EPERM from the table above — still goes red. The failure is fail-closed
at runtime: the sandboxed command never ran.

**It is not always a coin toss.** Two jobs (2026-09-03 and 2026-09-04) were measured in which
all three attempts failed the same way, both on runner image 20260819.586 with kernel
`6.17.0-1022-azure`, while every job on image 20260828.587 passed the contract run first time. On
such a VM a retry cannot help; the exhausted retry therefore fails under its own name
(`UserNamespaceFlakePersistedError`, one annotation naming `sdk_helper_nested_userns_einval` with
the kernel and the userns sysctls) instead of a stub-run transcript diff, and a red `check` runs
`scripts/ci-sandbox-diagnostics.sh`, which captures the runner's kernel, sysctls, seccomp and
AppArmor state and probes both a direct bwrap and a **nested** user namespace from inside the
sandbox — the very step the helper fails — so the next occurrence separates a kernel that refuses
nested user namespaces from a helper that raced its own threads. The same capture runs on demand
through the workflow's `capture_diagnostics` input.

**None of this is visible on macOS.** Docker Desktop's LinuxKit VM has no AppArmor and no userns
restriction, so the full flag set passes locally while failing on a real Linux host. Treat a
namespace, mount, or LSM result measured only on Docker Desktop as unmeasured.

**The runtime consumes this table at run start.** The SDK's `failIfUnavailable` only checks that
`bwrap` is installed — it never executes a command, so a host where any control above is missing
sails past it and every Bash call then fails mid-run (measured: four minutes and $1.36
of silent per-call failures). Every run child therefore probes the sandbox itself before the
first model turn — `src/sandbox-probe.ts` runs a no-op through the SDK-shaped argv — and a
failed probe fails the run `sandbox_unavailable` immediately, with the bwrap error from this
table's third column in the failure message (ADR 0120).

