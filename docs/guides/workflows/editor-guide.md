---
type: guide
status: current
description: "Full walkthrough of the workflow editor: creating a type, sections, blocks, variables, saving and version history, import and export, test runs and Generate Report, re-arranging, and the dependency chain and Graph tab. Read when you build or change a workflow in the editor and need to know where a control is or what it does."
---

# Workflow Editor Guide

**On this page:** Overview · Getting around the editor · Creating a New Workflow · Managing Workflow Variables · Editing Existing Workflows · Version History · Importing and Exporting Workflows · Running Workflows from Editor · Re-arranging Sections and Blocks · Seeing what a block depends on · Best Practices · Common Issues · Related Documentation

The workflow editor is a web interface for designing, configuring, and managing workflows — no coding required.

## Overview

The workflow editor enables non-technical users to create sophisticated AI-powered workflows without writing code. It provides visual configuration, real-time validation, and integrated testing capabilities.

> [!TIP]
> **A few terms used throughout this guide**
>
> - **[Workflow type vs. workflow vs. execution](../../reference/glossary.md)** — a *workflow type* is the reusable template you build and edit in this editor (for example "Credit Analysis"). Each time you run it against real documents and inputs you produce a *workflow* run, also called an *execution*. You author the type once; you create many executions from it.
> - **[Company](../../reference/glossary.md)** — the company or entity a report is about (a fund, an issuer, a borrower). Every screen calls it a company; the code, the API and the `{Counterparty.Name}` style placeholders keep the name counterparty. When you run a workflow you can attach a company so blocks can read its saved data and write results back to its profile.
> - **[RAG](../../reference/glossary.md)** (retrieval-augmented generation) — when a block needs an answer from your uploaded documents, the platform first retrieves the most relevant pages and feeds only those to the AI, rather than the whole document.

## Getting around the editor

### The main areas

- **Editor canvas** — where you build the workflow, with tabs for switching views.
- **Workflow map** — a visual outline of the workflow's structure; click any part to jump to it.
- **Root workflow controls** — import, export, save, and manage variables for the whole workflow.
- **Section editors** — organise blocks into logical groups.
- **Block configuration** — settings for each block, which change depending on the block type.
- **Dependency chain and Graph tab**: what each block reads and what reads it, over the editor from a block's **Dependency chain** button, and for the whole workflow in the **Graph** tab. See [Seeing what a block depends on](#seeing-what-a-block-depends-on).

### When you run a workflow

These controls sit above the template, top to bottom:

- **Upload card** — **Upload Files** adds documents; set each one's document class on its row. There is no OCR switch: pages without a usable text layer go through OCR on their own.
- **Company selector** — pick the company the run is about, plus any peer companies.
- **Variables panel** — fill in the workflow's variables before running.
- **Generate Report** — start the run.

## Creating a New Workflow

### 1. Create Workflow Type

1. Navigate to the **Workflow Type Management** page (in the editor app's left-navigation menu, at `/workflow-type-management` — it needs the `WorkflowEditor` role; if it is not visible, ask a super admin to grant it, see [who may grant what](../../features/user-management/authorization.md#how-roles-are-created-and-assigned))
2. Click **"Add Workflow Type"**
3. Fill in:
   - **ID**: Unique identifier (e.g., `credit-analysis`)
   - **Name**: Display name (e.g., `Credit Analysis`)
   - **Description**: Purpose and use case
   - **Category**: Organizational grouping
   - **Allowed Company Types**: the company types the workflow works on
4. Click **"Create"**

### 2. Open Workflow Editor

1. Go to **Workflow Editors** page
2. Click on the newly created workflow type
3. Editor loads with empty root workflow

### 3. Configure Root Workflow

1. **Name**: Internal identifier for the workflow
2. **Title**: User-facing title (appears in output)
3. **Description**: Optional detailed description

### 4. Add Sections

1. Click **"Add Section"** to add a section to the workflow
2. Sections are first-class parts of a workflow that organize blocks into logical groups
3. Navigate into section by:
   - Clicking section name in workflow map
   - Clicking the section's **"Open"** button

### 5. Configure Sections

**Section Properties:**
- **Title**: Section heading in final output
- **Grid Layout**:
  - Number of columns for block display
  - Gap size between blocks
  - When a block puts nothing in the report, such as a chart block that produced no chart, the report leaves it out. A paragraph or chart with nothing beside it in its rows takes the full width of the section, in the app and in the Word export. This holds for a block you place alone in its row too.
- **Custom Properties**: Section-specific configuration values
- **Leave out of exports** (section **⋮** menu): the section still shows in the report in the app, but the Word and Markdown exports leave it out: no heading, no paragraphs, no table-of-contents entry. Use it for debug and transparency content that the analyst reads in the app but does not want in the exported document. While it is on, the section header shows **Not in exports**, and the analyst sees the same label on the section in the report. The menu item reads **Include in exports** to switch it back.
  - It takes effect for runs that start after you save the workflow. Each report keeps the setting it was built with, so switching it later does not change the export of an earlier run.
  - On a section linked to the Library, the setting is part of the shared content, like the layout: inserting or syncing the Library section brings its setting, and switching it on a linked copy makes that copy differ from the Library until you push or adopt.

**Navigation:**
- **"Go Back"**: Return to root workflow
- **Workflow Map**: Click any section to navigate

### 6. Add Blocks

1. Inside a section, click **"Add Block"**
2. A block runs once the blocks it reads have finished; blocks with no link between them can run in parallel ([Parallel execution](../../architecture/workflow-engine/parallel-execution.md))

### 7. Configure Blocks

**Basic Properties:**

- **Name**: Display name (visible only in editor)
- **Replacement Tag**: Identifier for referencing output (e.g., `Portfolio_Analysis`)
  - ⚠️ **Important**: two blocks sharing a tag makes every `{tag}` reference ambiguous — it resolves to whichever of them runs first. It is always a warning, never a blocked save
  - The editor avoids creating the ambiguity for you: a new block takes the first free tag, duplicating a block gives `{tag}_copy` (then `_copy_2`, `_copy_3`…), and a block moved into a section that already holds its tag is renamed with a numeric suffix (`final_summary` → `final_summary_2`). Renaming is never applied to your existing `{tag}` references
  - The same tag in *different* sections is a legitimate pattern (duplicated sections, branch variants) and is left alone — the tag field shows a note naming the other block and its section so you know the reference is ambiguous
  - Tags are capped at 100 characters
- **Show in Output** (action bar): include the block's result in the report
  - **Paragraph Sub-Title Template**: optional heading for this output paragraph; it appears once **Show in Output** is on
- **Send as data** (action bar): return the block's JSON to other systems beside the report, through [Get Report Data Outputs](../../reference/api/reports.md#get-report-data-outputs). A block can be both in the report and sent as data, and a workflow can send several outputs. Report readers see each one under **Data** in the report's section list, as the JSON itself with its cited figures
  - **Data Output Name**: the name other systems read the JSON under. It starts as the block's replacement tag; give each data output its own name, because a reading system keys on it. The panel warns when another block uses the same name. A block inside a loop sends one output per item, named `<name>_0`, `<name>_1` and so on; in a parameterized section, one per variant, named `<name>_<variant key>`
  - An AI Query block sent as data must set **Output format validation** to JSON or JSON in Tag ([AI Query Output Format](../blocks/ai-query-output-format.md)); the panel warns until it does. A run fails a block whose result is not JSON, and its log says why
  - With **Get Citations From Model** on, use **JSON in Tag**: in plain JSON the model can mark its citations inside the values and write numbers as text ([why](../../architecture/citations/index.md#citations-in-a-data-output)). The panel warns about this too
- **Save Output for Company**: save the output to the company's profile
  - **Select Company Output Type** picks the field to update

**Block Type:**
- Pick a type from the dropdown — see the [Block Reference](../blocks/block-reference.md) for the full list
- Each type shows its own configuration options

**Block-Specific Settings:**
- Configuration varies by block type
- See [Block Reference](../blocks/block-reference.md) for details

**Text Inputs:**
- Additional properties for advanced configuration
- Available only to the specific block
- Support placeholder replacement

## Managing Workflow Variables

Variables provide dynamic user input at workflow execution time.

### Creating Variables

1. Navigate to root workflow
2. Click the **"Variables"** tab (next to "Sections")
3. Click **"+"** to create new variable

### Variable Configuration

**Required Properties:**

| Property | Description |
|----------|-------------|
| **Replacement Tag** | Unique identifier (e.g., `fund_name`) |
| **Display Name** | User-facing label (e.g., `Fund Name`) |
| **Variable Type** | String, Number, DateTime, List, Dropdown, MultiSelect |

**Optional Properties:**

| Property | Description |
|----------|-------------|
| **Description** | Help text (shown on hover) |
| **Required** | User must provide value (on by default) |
| **Hidden** | Hide from user interface |
| **Confidential** | The value still reaches the prompts, but a finished run masks it and a re-run does not carry it over ([Confidential Variables](../../architecture/workflow-engine/variable-system.md#confidential-variables)) |
| **Default Value** | Pre-filled value |
| **Options** | For Dropdown/MultiSelect types |

### Variable Types

**String**
- Single-line text input
- Use for: Names, identifiers, short text

**Number**
- Numeric input with validation
- Use for: Counts, percentages, thresholds

**DateTime**
- Date/time picker
- Use for: Report dates, analysis periods

**List**
- Multi-line text input
- Use for: Multiple items, long text

**Dropdown**
- Single selection from predefined options
- Options format: `{internal_value: "Display Name"}`
- Use for: Categories, types, single choice

**MultiSelect**
- Multiple selections from predefined options
- Options format: `{internal_value: "Display Name"}`
- Use for: Multiple categories, tags

### Configuring Dropdown/MultiSelect Options

**Option Structure:**
```json
{
  "comprehensive": "Comprehensive Analysis",
  "basic": "Basic Review",
  "quick": "Quick Assessment"
}
```

**Adding Options:**
1. Click **"Add Option"** in variable dialog
2. **Option N - Value**: Internal identifier (sent to workflow)
3. **Option N - Display Name**: User-facing label (shown in UI)

**Best Practices:**
- Use descriptive display names
- Keep internal values lowercase and simple
- Provide at least 2 options

## Editing Existing Workflows

### Loading Workflow

1. Go to **Workflow Editors** page
2. Click on workflow type to edit
3. Last saved version loads automatically

### Making Changes

- Navigate through sections using workflow map
- Modify blocks, sections, or variables
- Changes are not saved automatically

### Saving Changes

The editor toolbar (and a floating Save button, bottom right) saves the template. Every save also adds an entry to the workflow's version history (see [Version History](#version-history)).

While the editor holds changes that are not saved, an **Unsaved changes** chip shows beside the "Last saved" stamp. A link inside the app then asks first (**Unsaved changes**: "This page has changes that are not saved. If you leave now, they are lost.", with **Stay** and **Leave without saving**), and a reload or a closed tab brings up the browser's own leave-page warning.


**Option 1: Save**
1. Click 💾 **"Save"** in the editor toolbar
2. Persists the template and, when blocks need them, prompts to regenerate prompt previews before saving
3. Use this when you want the saved version to carry up-to-date previews

**Option 2: Save without regenerating previews**
1. Open the **⋮ (More actions)** menu and choose **"Save without regenerating previews"** (the floating Save button does the same)
2. Persists immediately, skipping the preview-regeneration prompt
3. Faster; existing previews are left untouched

**Commit to GitHub (optional)**
- When GitHub versioning is configured for the deployment, the **⋮ (More actions)** menu shows **"Commit to GitHub"**, which prompts for a commit message and pushes the workflow JSON to the configured repository. Saves can also auto-commit when this is enabled.

## Version History

Every save appends an immutable snapshot to the workflow's history, so you can see how a template evolved and roll back if needed. No manual tagging or commit step is required — history builds itself as you save.

**Opening history:**
1. Click **"Version history"** in the editor toolbar
2. The **Saved versions** drawer lists each save newest-first, with the most recent marked **Current**

**What each entry shows:**
- An auto-generated plain-language summary of what changed since the previous version (entries saved with no detectable change read "Saved without a change summary")
- Who saved it, when, and a short version code (also shown inline as a "Last saved" / "Last changed" stamp in the editor)

**Actions:**
- **Open this version** — loads an earlier snapshot back into the editor (save again to make it the new current version)
- **Compare with current** — shows a plain-language summary of the differences
- **Select** two versions, then **Compare the two selected** — diff any two points in history

Each save records exactly what it was edited from, so the history forms an unbroken chain you can trace back. To save space, very old snapshots may have their full content trimmed; a trimmed version keeps its summary and details (who, when) but can no longer be opened or compared. A workflow type has one history, shared by every organization that uses the type ([Workflow Template Versioning](../../features/workflow-template-versioning.md)).

## Importing and Exporting Workflows

### Exporting Workflow

**Method 1: Floating button**
1. Open workflow in editor
2. Click the 📥 **Download** floating button (bottom right)
3. JSON file downloads with `.workflow` extension

**Method 2: Toolbar**
1. Click **"Download to file"** in the editor toolbar
2. Same result as the floating button

Exported files carry a top-level `"FormatVersion"` stamp (currently `4`,
`Workflow.CURRENT_FORMAT_VERSION`) identifying the `.workflow` file format the
exporting platform wrote. Older files without the stamp remain valid.

**Export workflow bundle** (a third floating button, for editors) exports the
type with its version trail and the Library components it uses, for moving it
to another deployment ([transfer bundles](../../architecture/workflow-engine/shared-components.md)).

**Use Cases:**
- Backup workflows
- Share workflows between environments
- Version control via external systems

### Importing Workflow

⚠️ **Important**: Import replaces current workflow in selected type

**Steps:**
1. Open **any** workflow editor (type determines where import is saved)
2. Click **"Upload from file"** in the editor toolbar
3. Select a `.workflow` (or legacy `.sequence`) JSON file
4. Workflow definition loads, replacing current content
5. Review and modify as needed
6. Click 💾 **"Save"** to persist

A file whose `FormatVersion` is **newer** than the platform supports is
rejected with `This workflow file uses format version <n>, but this platform supports up to version 4. Update the platform before importing this file.` Files without a `FormatVersion` (legacy exports) import
normally.

**Tips:**
- Verify workflow type before importing
- Review imported configuration before saving
- Keep backup of original workflow



## Running Workflows from Editor

### Setup

**1. Select Company (Optional)**
- Dropdown: **"Select Company"**
- Required if workflow uses company data

**2. Select Peer Companies (Optional)**
- Dropdown: **"Select Peer Companies"**
- Used for comparative analysis

**3. Fill Workflow Variables**
- The variables panel below the company selector shows every variable that is not hidden
- Required variables marked with *
- Dropdowns show configured options

**4. Upload Documents (If Required)**
- Click **"Upload Files"** on the upload card at the top of the page
- Multiple files supported (up to 25 at a time)
- Set each document's **Document Class** (e.g., "Annual Report") in the picker on its row; there is no OCR switch, because pages without a usable text layer go through OCR on their own
- Remove a document with its delete (trash) button


### Execution

1. Click **"Generate Report"**
2. The editor shows "Job started successfully." and stays open; the run is a background job
3. Workflow execution begins

### Monitoring

- Navigate to **Dashboard** page
- View execution status and progress
- Output available after completion

⚠️ **Note**: Workflow output is viewable only in **IniaAi.Web** (the Vue single-page app for end users — a separate front end from this editor, at your deployment's web address; report list at `/workflow-reports`), not in the editor. See [Frontend (Vue SPA)](../../architecture/frontend/index.md).

### A block's output in the editor

A test run of a block (**Run block**, **Run section**, **Execute all**) shows that block's own output below its settings, in the **Raw Response**, **JSON**, **HTML**, **Markdown** and **Binary** tabs, plus **Debug Info** when the block has it. An output that is a JSON object or array, alone or inside one code fence, shows indented, one field per line, in the **Raw Response** and **JSON** tabs. Only the line breaks and spaces change on screen: the stored output stays as the block wrote it until you edit the text in the tab. An edit stores the text exactly as you leave it in the tab, and later blocks read that text. An output over about one million characters shows as stored. The editor keeps these outputs until the page reloads; saving the workflow does not store them.

## Re-arranging Sections and Blocks

Each section and block offers the same five actions in its action bar:

### Duplicate
- **Behavior**: Creates a copy directly below the original
- **Configuration**: Identical to original (new ID assigned)
- ⚠️ **Tags**: a duplicated block takes the tag `{tag}_copy`; a duplicated section keeps its blocks' tags. `{tag}` references inside the copy are never rewritten, so check what they point at
- A duplicated block is not sent as data, even when the original is: two outputs under one name would give a reading system two values for one key

### Move
- **Behavior**: Opens a dialog to pick a target section and a 1-based position; the section/block is moved there
- **Use**: Reorganize execution flow, including across sections

### Copy to another workflow
- **Behavior**: Copies the section/block into a different workflow type (the current workflow is left unchanged)
- **Use**: Reuse configuration across workflows

### Copy as text
- **Behavior**: Copies a plain-text rendering of the section/block to the clipboard
- **Use**: Share or review configuration outside the editor

### Remove
- **Behavior**: Deletes the section/block
- **Protection**: Confirmation dialog required

Blocks additionally offer **Run block**, **Show in Output** (include the result in the final report), **Dependency chain** (show this block's chain over the editor), and an overflow (**⋮**) menu with **"Show only the blocks related to this one"**, **Share to Library**, and **Exclude from run log** / **Include in run log**. Sections additionally offer **Run section**, **Parameterize**, and an overflow menu with **Enable citations for all** / **Disable citations for all** (the section's AI Query blocks), **Leave out of exports**, **Layout in report** and **Share to Library**.

## Seeing what a block depends on

The graph of what each block reads and what reads it is in two places. Both draw the workflow as it is in the editor, unsaved edits included, and never change it.

- **Dependency chain** in a block's action bar opens that block's chain over the right of the editor: the blocks it reads and the blocks that read it. The editor stays visible and scrolls behind it. It is closed when the editor loads.
- The **Graph** tab, the last tab in the editor, shows the whole workflow at the full width of the editor. It opens on the map.

In the **Graph** tab:

- **Map** shows one card per section, with a link between two sections when a block in one reads a block in the other. The sections read in authored order, top to bottom, with sections that sit at the same depth wrapped into rows as wide as the tab. Click a section card to open it into its blocks; click the section's title to close it again.
- Click a block card to see its chain in the tab; **Chain** and **Map** switch between the two. **Open in editor** on a block card switches to the Sections view, scrolls to the block and highlights it.
- The map opens at a readable size. When the whole workflow does not fit at that size, it shows the start of the workflow; drag to pan, and use **+** and **-** or Ctrl and the mouse wheel to zoom.

In the dependency chain over the editor:

- Clicking a block in the chain centres the chain on it and scrolls the editor to it, opening its section if another one is showing. Clicking inside a block in the editor centres the chain on that block. The trail at the top goes back to blocks you looked at.
- **See on the full graph** opens the **Graph** tab with the block shown in its open section.
- Escape, the close button, or a click on an empty part of the editor closes it. Drag its left edge to make it wider or narrower for as long as it is open.

In both:

- **Chain** centres on one block: the blocks it reads above it, the blocks that read it below. It shows one step each way, and a **+N more** card opens the next step.
- Every block is drawn, whatever its type, including Merge, Merge JSON and String Manipulation blocks.
- Links are drawn by kind: a block reading another block's output, a block waiting for documents another block creates, a block waiting for a company value another block writes, and a section gate. The legend under the toolbar names each one: **reads output**, **documents**, **company** and **section gate**.
- **Inputs** and **Gates** show the workflow variables, signature fields and other inputs a block reads, and the section gates in front of it. Each block card also has its own counts, which open them for that block alone.
- A tag that nothing supplies is drawn as a red card, "No block or input supplies {tag}", whether or not inputs are shown.
- The **issues** count in the header lists tags nothing supplies, cycles, and blocks that run before the block that creates their documents or writes the company value they read. Pick an issue to open the chain on it.
- **Fit** fits the whole graph in view, however small.

A link in the graph is a dependency the run waits for: the graph and the scheduler use the same rule ([Parallel execution](../../architecture/workflow-engine/parallel-execution.md#building-the-graph)).

## Best Practices

### Workflow Organization

**Sections:**
- Group related blocks together
- Use descriptive section titles
- Limit to 5-10 blocks per section

**Blocks:**
- Descriptive names for clarity
- Unique replacement tags when referencing needed
- Clear output configuration

**Variables:**
- Descriptive replacement tags (e.g., `analysis_type` not `var1`)
- Provide default values when sensible
- Mark confidential data appropriately

### Performance Considerations

**Document Upload:**
- Upload only required documents
- Prefer files with a text layer: scanned pages go through OCR, which is slower
- Appropriate document classes for filtering

**Block Configuration:**
- Avoid unnecessary AI queries
- Use caching for repeated queries
- Consider token limits

### Maintenance

**Regular Reviews:**
- Test workflows periodically
- Update variable options as needed
- Remove unused sections/blocks

**Version Control:**
- Export workflows regularly
- Document major changes
- Keep backups before significant edits

## Common Issues

### Variables Not Appearing
- Ensure variable saved before use
- Check "Is Hidden" flag
- Verify replacement tag is unique

### Blocks Not Executing
- Check execution order
- Press **Dependency chain** on the block to see what it waits for and whether the graph reports an issue
- Verify replacement tag references
- Review block configuration for errors

### Import Fails
- Verify JSON file format (`.workflow`)
- Check file not corrupted
- Ensure workflow structure valid
- If the error names a `FormatVersion`, the file comes from a newer platform — update this deployment first

## Related Documentation

- [Block Reference](../blocks/block-reference.md) - Complete block configuration reference
- [Workflow Engine Architecture](../../architecture/workflow-engine/index.md) - Technical details
- [Variable System](../../architecture/workflow-engine/variable-system.md) - Variable internals

