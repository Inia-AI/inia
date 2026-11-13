# Security policy

## Report a vulnerability

Report a vulnerability privately, through GitHub:

1. Open the **Security** tab of this repository.
2. Select **Report a vulnerability**.
3. Describe the problem, the release or commit you found it in, and the steps that show it.

This is the only channel for vulnerability reports. Do not open a public issue, pull request or discussion about a vulnerability.

## Scope

An escape from the sandbox that runs the agent's Bash commands is in scope: a command the agent runs that reads, changes or reaches anything outside that sandbox. [Container seccomp profile for the SDK sandbox](sandbox/README.md) describes the sandbox and its controls.

## What happens next

Inia AI's maintainers read every report and answer in it. A confirmed vulnerability is fixed in a release.

## Versions

Fixes go into the next release. Run the latest release.
