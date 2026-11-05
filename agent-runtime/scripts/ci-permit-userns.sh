#!/usr/bin/env bash
# Lets bubblewrap build the runtime's sandbox on an Ubuntu runner: the user-namespace sysctl, and
# the AppArmor profile bwrap-userns-restrict unloaded, which Ubuntu 26.04 loads and the sysctl does
# not reach (sandbox/README.md). Fails when a bwrap profile is still loaded afterwards, so a runner
# image that moves the profile breaks here instead of in the sandboxed tests.
set -euo pipefail

profile=/etc/apparmor.d/bwrap-userns-restrict
loaded_profiles=/sys/kernel/security/apparmor/profiles

sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0

bwrap_profile_loaded() {
  sudo grep -q '^bwrap ' "$loaded_profiles" 2>/dev/null
}

if bwrap_profile_loaded; then
  sudo apparmor_parser -R "$profile"
  echo "unloaded the AppArmor profiles in $profile"
fi

if bwrap_profile_loaded; then
  echo "::error::the AppArmor profile bwrap is still loaded, so the sandbox's nested user namespace gets no capability" >&2
  exit 1
fi
