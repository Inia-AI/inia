# Contributing

Issues are welcome: a bug report, a feature request, or a question that the docs do not answer. Open
one from the **Issues** tab, with the bug or the feature form. To report a vulnerability, do not open
an issue: follow [SECURITY.md](SECURITY.md).

## How a change gets in

Maintainers do not merge pull requests in this repository. When they accept a change, they apply it
to the maintainers' own repository of that part by hand, credit you, and the change reaches this
repository with the next release. Each release replaces the folders with the new version, so a
commit made here directly would be lost.

Open an issue before you start a change larger than a small fix, so that a maintainer can say
whether it fits.

## The bar for each part

Each folder has its own build, tests and rules:

- [platform/CONTRIBUTING.md](platform/CONTRIBUTING.md) for the API, the admin app and the samples.
- [The commands of the Vue app](web/README.md#commands) and its
  [contributing notes](web/README.md#contributing).
- [The quality gates of the agent runtime](agent-runtime/README.md#quality-gates).

The workflows in `.github/workflows/` run the same checks on every pull request.

## Conduct

Everyone who takes part follows the [code of conduct](CODE_OF_CONDUCT.md).
