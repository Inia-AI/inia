# Docker default seccomp -> minimal bubblewrap-capable derivative: ungates
# {mount, umount, umount2, setns, unshare} from the CAP_SYS_ADMIN-only ALLOW
# rule, drops the CLONE_NEW*-args filter on the unconditional clone() ALLOW,
# and adds pivot_root (absent from the default profile, so blocked by
# defaultAction otherwise).
#
# clone3 is deliberately left untouched: bubblewrap's raw_clone() calls
# legacy clone(), never clone3, so the default profile's ENOSYS-for-non-admin
# rule for it is harmless.

def ungatedNames: ["mount", "umount", "umount2", "setns", "unshare"];

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
  ]
) + [
  {names: ["pivot_root"], action: "SCMP_ACT_ALLOW"}
]
