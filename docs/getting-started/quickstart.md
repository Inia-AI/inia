---
type: guide
status: current
description: "Takes a reader from a clone of the open repository to a report whose figures link to their pages, with the one-command Docker stack and the sample workflows, then checks the result from the command line. Read when you run the open edition for the first time, or want to see a run end to end."
---

# Quickstart

**On this page:** 1. Start the platform · 2. Run the sample in the Vue app · 3. Check the figures and their pages · 4. See how blocks pass data · 5. Check the same result from the command line · Next · Done when

This page takes you from a clone of this repository to a report whose figures link to their pages.
It uses the one-command Docker stack and the sample workflows in `platform/samples/`. You need
Docker and an Anthropic API key. [What the host needs](../../platform/README.md#what-the-host-needs) lists the
versions, the memory and the disk.

## 1. Start the platform

1. Clone the repository and open its root folder in a terminal: the folder that holds `compose.yaml`
   and the `platform` folder.

2. Copy the settings template:

   ```bash
   cp .env.example .env
   ```

3. In `.env`, set the two required values:

   - `ANTHROPIC_API_KEY`: your Anthropic API key.
   - `INIA_ADMIN_PASSWORD`: the password of the first admin, `superadmin@example.com` unless you set `INIA_ADMIN_EMAIL`. It follows the
     [password rule](security.md#sign-in).

   The other keys in `.env` are optional.
   [Optional keys](../../platform/README.md#optional-keys)
   says what each one adds, such as the Cohere key for the blocks that pick pages by meaning.

4. Build and start the stack:

   ```bash
   docker compose up --build --wait
   ```

   The command returns when every service is healthy. The first start also loads the two sample
   workflows, **Sample: annual report key figures** and **Sample: block showcase**. If a sample does
   not show in the next step, `docker compose logs sample` says why, and the next start tries again.

## 2. Run the sample in the Vue app

1. Open `http://localhost:8080` and sign in as `superadmin@example.com` with your password.
2. Select **Create New Report**.
3. Under **Report type**, select **Sample: annual report key figures**, then **Continue**.
4. Under **Documents**, add `platform/samples/annual-report/annual-report.pdf` from the `platform` folder of
   your clone.
5. Select **Continue** on **Details**, then **Submit** on **Review**.

When the run ends, the report shows under **Ready for review** on the dashboard.

## 3. Check the figures and their pages

1. Open the report. Its **Key figures** section states four figures of the sample report: revenue,
   net profit, total assets and employees.
2. Click a figure. The source list names `annual-report.pdf` and the page that states the figure.
3. Click the source. The image of that page opens, and it shows the figure.

`platform/samples/annual-report/expected.json` lists each figure with its value and its page, so you can
compare. The sample report is fictional: `platform/samples/annual-report/make_report.py` writes it.

## 4. See how blocks pass data

The second sample, **Sample: block showcase**, reads the same report with one AI Query block. That
block returns the figures as JSON. The other blocks read a page, calculate, reshape, chart and loop
over the answer with no other model call. `platform/samples/block-showcase/workflow.json` holds the workflow.

1. Run it as in section 2: under **Report type**, select **Sample: block showcase**, and add the same
   `annual-report.pdf`.
2. Open the report. The heading of each part names the block type that made it:
   - **Read the figures**: Retrieve Data From Documents reads page 2, and String Manipulation
     removes the page markers.
   - **Calculate**: two Calculate Formula blocks calculate the revenue growth and the net margin.
   - **Combine as data**: Merge JSON combines the AI Query answer with the two results, and Dynamic
     Chart draws four of its figures.
   - **Summary**: Apply Template To Data writes a sentence from the blocks above, and a loop adds one
     line for each measure. A Switch Case block names the currency, and a String Manipulation block
     writes the company name in capitals.
   - **Data**: the JSON that Merge JSON combined, as the data output `figures_and_ratios`. It is
     what the API returns to another system for this run.
3. To see the settings of each block, open the admin app at `http://localhost:8081` and select
   **Workflow Editors**, then **Sample: block showcase**. Select **Open** on a section to see its
   blocks and their settings.

`platform/samples/block-showcase/expected.json` lists what each part of the report says, the points of the
chart, and the values of the data output.

## 5. Check the same result from the command line

The check that runs on every release of the platform runs every sample and compares each report
with the `expected.json` of its sample. A third sample, `platform/samples/agent-run`, runs an Agent block that
calculates the revenue growth with Python in the agent runtime's sandbox. Run it against your install with Python 3. It needs no package.
Give it the password of the first admin in `INIA_ADMIN_PASSWORD`:

```bash
INIA_ADMIN_PASSWORD='<the password you set>' python3 platform/scripts/clean-machine/check.py
```

It prints one `PASS` line for each sample whose report matches. For the annual report, every figure
has its value and a citation to its page, and the image of each cited page opens. For the showcase,
each part of the report says what `expected.json` lists, the chart has its four points, and the
data output holds its values. For the agent run, the answer says `Revenue growth: 10.2%`. Otherwise
it prints one `FAIL` line for each difference. Its exit code
is 0 for a pass, 1 for a difference, and 2 when it cannot reach the install or a run fails. Without
`--url`, it calls the install at `http://localhost:8080`. `--without agent-runtime` leaves out the
samples that need the agent runtime, for an install that runs only the API.

## Next

- [Overview](platform-overview.md): what else a workflow can do.
- [Architecture](../architecture/platform-tour.md): what happened between the upload and the report.
- [The open build and the full edition](editions.md): what this build holds.
- To stop the stack, run `docker compose down`. It keeps the data. `docker compose down -v` also
  deletes the data, and the next start loads the samples again.

## Done when

- `docker compose ps` shows every service `healthy`.
- The report of **Sample: annual report key figures** shows the four figures of `platform/samples/annual-report/expected.json`, each with a citation to its page.
- `INIA_ADMIN_PASSWORD='<the password you set>' python3 platform/scripts/clean-machine/check.py` prints one `PASS` line for each sample and exits with code 0.
