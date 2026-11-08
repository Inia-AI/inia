---
type: runbook
status: current
description: "Lists the failures that a first start, a sample run and the command-line check have met, each with what you see, the cause and the fix: a full disk during the build, a port in use, a setting that did not take, a first admin who cannot sign in, a sample missing from the app, the agent sample failing on Ubuntu, a block skipped for want of a Cohere key, and each FAIL line of the check. Read when the stack does not start, a sample run fails, or the check prints FAIL."
---

# Troubleshooting

**On this page:** The build stops on a full disk · A port is already in use · A changed setting has no effect · The first admin cannot sign in · A sample does not show in the app · The agent sample fails on Ubuntu · A block is skipped for want of a Cohere key · The check prints FAIL · Done when · Related pages

Each section names what you see, why it happens and what to do. The commands run in the root folder of your clone, the folder that holds `compose.yaml`.

## The build stops on a full disk

**What you see:** `docker compose up --build --wait` stops, and the build output says `no space left on device`.

**Why:** the images take 6 GB, and a build leaves up to 14 GB of build cache. The host needs 20 GB of free disk. A release check of the open repository ran out of disk this way on a 14 GB runner.

**Fix:** free the build cache, then build again.

```bash
docker builder prune
docker compose up --build --wait
```

## A port is already in use

**What you see:** a service does not start, and Docker says that a port such as `127.0.0.1:8080` is already allocated.

**Why:** another program on the host listens on the port. The Vue app listens on 8080 and the admin app on 8081 unless `.env` says otherwise.

**Fix:** set free ports in `.env`, then start again:

```bash
INIA_WEB_PORT=8090
INIA_ADMIN_PORT=8091
```

## A changed setting has no effect

**What you see:** you changed a value in `.env`, and the platform still behaves as before.

**Why:** a running service keeps the values it started with.

**Fix:** recreate the services:

```bash
docker compose up --wait --force-recreate
```

## The first admin cannot sign in

**What you see:** the first admin's sign-in is refused after you changed `INIA_ADMIN_PASSWORD` or `INIA_ADMIN_EMAIL`.

**Why:** the first start creates the account with those values. Later starts keep the address and the password the account has, whatever `.env` says.

**Fix:** sign in with the values of the first start. To start over with new ones, delete the data with `docker compose down -v`, which removes every report and setting, then start again.

## A sample does not show in the app

**What you see:** the list of report types has no **Sample: annual report key figures**, **Sample: block showcase** or **Sample: agent run**.

**Why:** the `sample` service loads the samples on the first start and then exits. A failure leaves its reason in its log.

**Fix:** read the log, fix the cause, and start again. The next start tries again.

```bash
docker compose logs sample
docker compose up --wait
```

## The agent sample fails on Ubuntu

**What you see:** on Ubuntu 24.04 or later, the run of **Sample: agent run** ends as Failed, and its Agent block's log says `Agent run failed with subtype 'sandbox_unavailable'`. The other samples pass.

**Why:** Ubuntu restricts unprivileged user namespaces, and from Ubuntu 26.04 it also loads the AppArmor profile `bwrap-userns-restrict`. The agent runtime cannot build its sandbox under either one.

**Fix:** lift the restriction on the host, then run the sample again. The second command is needed only where the profile is loaded.

```bash
sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0
sudo apparmor_parser -R /etc/apparmor.d/bwrap-userns-restrict
```

`agent-runtime/scripts/ci-permit-userns.sh` runs both steps and fails if the profile stays loaded.

## A block is skipped for want of a Cohere key

**What you see:** an AI Query block of your own workflow is skipped, and its reason says: "The filter rule of this block picks pages by meaning, which needs a Cohere API key, and the platform has none."

**Why:** the block's filter rule ranks pages by embeddings, and only a Cohere key gives the platform embeddings. The samples use rules that need none.

**Fix:** put your key in `COHERE_API_KEY` in `.env` and recreate the services, or give the block a filter rule that reads the whole document, exact pages or the table of contents.

## The check prints FAIL

`platform/scripts/clean-machine/check.py` runs the samples one at a time. A `FAIL` line starts with the sample's id, such as `SampleAnnualReport`, and says what went wrong:

- **`The sample run <id> ended as Failed. Its block logs say why.`** Open the report in the Vue app and read the log of the block that failed.
- **`<figure>: <value> cites page 2 of <file>, not page 3`**: the run finished, but the model cited another page for that figure than the sample's `expected.json` names. Open the report, click the figure and look at the page its citation opens. Run the check again: a model can pick another page on another run.
- **`<figure>: the image of the cited page does not open`**: the citation names a page whose image the API cannot render. Open the page from the report to see the error.
- **`SKIP <sample>: it needs …`** is not a failure. The sample needs a part that this install runs without.

## Done when

- `docker compose ps` shows every service `healthy`.
- `INIA_ADMIN_PASSWORD='<the password you set>' python3 platform/scripts/clean-machine/check.py` prints a `PASS` or `SKIP` line for each sample and exits with code 0.

## Related pages

- [Quickstart](quickstart.md): the first start, the samples and the check.
- [Upgrade to a New Release](upgrade.md): moving an install to the next release, and rolling back.
- [The Docker Stack](docker-stack.md): the services of `compose.yaml` and how the secrets reach them.
