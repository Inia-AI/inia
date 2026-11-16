# Docker default seccomp -> minimal bubblewrap-capable derivative: ungates
# {mount, umount, umount2, setns, unshare} from the CAP_SYS_ADMIN-only ALLOW
# rule, drops the CLONE_NEW*-args filter on the unconditional clone() ALLOW,
# and adds pivot_root (absent from the default profile, so blocked by
# defaultAction otherwise).
#
# The uid calls (setuid, setreuid, setresuid, setfsuid and their 32-bit forms)
# leave the unconditional ALLOW rule and are allowed only when every uid argument
# is one the runtime uses: 1000, a uid from 16384 to 32767 (the run pool and the
# loopback uid sit there; scripts/seccomp-uid-matrix.sh checks that against the
# image), or -1 for "leave unchanged". The supervisor holds CAP_SETUID to drop each
# run to its own uid; with it, uid 0 is one call away, and under
# systempaths=unconfined uid 0 can write the host kernel's settings
# (/proc/sys/kernel/core_pattern, /proc/sysrq-trigger).
# A list of allowed values, never a "not 0" test: seccomp compares the whole 64-bit
# register, while the kernel reads a uid from its low 32 bits, or from its low 16
# when a 32-bit program calls the plain names, so 2^32 and 65536 both pass "not 0"
# and are uid 0. Each rule here pins all 64 bits. libseccomp keeps only one
# comparison per argument in a rule, so a rule cannot hold "not 0 and below 2^32".
# The mask is 2^64 - 2^14: jq 1.7 or later writes it back digit for digit, and an
# older jq writes it in exponent form, which Docker refuses.
# The container must run with no-new-privileges. Without it runc installs the
# filter before its own setuid(0) for the image's root user, and the container
# does not start.
#
# clone3 is deliberately left untouched: bubblewrap's raw_clone() calls
# legacy clone(), never clone3, so the default profile's ENOSYS-for-non-admin
# rule for it is harmless.

def ungatedNames: ["mount", "umount", "umount2", "setns", "unshare"];

def uidArgCounts: {setuid: 1, setuid32: 1, setfsuid: 1, setfsuid32: 1,
                   setreuid: 2, setreuid32: 2, setresuid: 3, setresuid32: 3};
def allowedUids: [
  {op: "SCMP_CMP_EQ", value: 1000},
  {op: "SCMP_CMP_MASKED_EQ", value: 18446744073709535232, valueTwo: 16384},
  {op: "SCMP_CMP_EQ", value: 4294967295}
];
def allowedUidArgs($count):
  if $count == 0 then []
  else allowedUidArgs($count - 1) as $rest | allowedUids[] | $rest + [. + {index: ($count - 1)}]
  end;

.syscalls = (
  [
    .syscalls[]
    | if ((.includes.caps? // []) == ["CAP_SYS_ADMIN"]) and ((.names // []) | contains(["mount"]))
      then
        (. + {names: (.names - ungatedNames)}),
        {names: ungatedNames, action: "SCMP_ACT_ALLOW"}
      elif (.names == ["clone"]) and (.args != null)
      then
        del(.args)
      else
        .
      end
    | if .names then .names -= (uidArgCounts | keys_unsorted) else . end
  ]
) + [
  {names: ["pivot_root"], action: "SCMP_ACT_ALLOW"}
] + [
  uidArgCounts | to_entries[] | .key as $name
  | {names: [$name], action: "SCMP_ACT_ALLOW", args: allowedUidArgs(.value)}
]
