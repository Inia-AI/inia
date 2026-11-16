---
type: guide
status: current
description: "Walks through configuring an AI Query block's Output Format (named tags, JSON, JSON in a tag): the Simple Schema Builder and Advanced JSON Schema, the lenient validator, the exact error messages, the one retry, and how later blocks read the extracted values. Read when you need a later block to read one value out of an AI Query answer, or an AI Query block fails output-format validation."
---

# AI Query Output Format

**On this page:** When to use it · Format types · Configuring a format · Schema rules · Using extracted values downstream · What "fail" means · Common gotchas · Limitations · Done when · See also

An [AI Query](block-reference.md#ai-query) block normally produces one block of free text. That is fine when the answer flows straight into the report. But when a **later block needs to read one specific value back out** — a rating, a yes/no, a number, a structured record — free text is awkward to reference reliably.

The **Output Format** setting solves this. It tells the platform to check the model's answer has the shape you asked for (a named tag, several named tags, or JSON). If the answer doesn't match, the block runs **once more with the same prompt**. If it still doesn't match, the block is marked **Failed** with the validator error attached, and the rest of the workflow keeps running.

The format is **opt-in**. Existing AI Query blocks default to **None** and behave exactly as before.

## When to use it

- A downstream block needs to pull one specific field out of the model's response.
- You want to fail loudly when the model produces something unparseable, instead of passing garbage forward.
- You want a one-attempt safety net against a transient model misformat.

If you don't need any of those, leave Output Format set to **None**.

## Format types

| Format (editor label) | What it means | Example response |
| --- | --- | --- |
| **None (no validation)** (default) | No checking. The raw answer goes through as-is. | (anything) |
| **Single Tag** | The answer must contain **one** named tag wrapping the value. | `<answer>yes</answer>` |
| **Multi Tag** | The answer must contain **several** named tags, in any order. | `<reasoning>…</reasoning> <answer>…</answer>` |
| **JSON** | The **entire** answer must be valid JSON. | `{ "name": "Alice", "age": 30 }` |
| **JSON in Tag** | Valid JSON **inside** a named tag, so the model can also write reasoning outside the tag. | `<answer>{ "score": 0.9 }</answer>` |

You still write the instruction to use the tags in your **prompt** — for example "Put the one-word rating inside `<rating></rating>`." The Output Format setting is what then *enforces* it: if the model forgets the tag or returns malformed JSON, the platform retries automatically and shows you the rejected attempt in the editor if it ultimately can't comply.

## Configuring a format

In the AI Query block editor, open the **Output format validation** panel.

1. Pick a **Format** from the dropdown. A short description of the selected format appears beneath it.
2. For tag-based formats, fill in the **Tag name** (Single Tag / JSON in Tag) or **Tag names** (Multi Tag, comma-separated). Enter just the name — no angle brackets. The editor previews what it expects to find in the response, e.g. `<answer>...</answer>`.
3. For JSON-based formats, optionally configure **Schema rules** (see below). Leave them blank to only check that the response parses as JSON.

> [!TIP]
> **Tag-name rules**
>
> A tag name must start with a letter or underscore and contain only letters, digits, underscores or hyphens. Attributes (`<answer foo="bar">`) and nested same-name tags are not supported.

## Schema rules

When the format is **JSON** or **JSON in Tag**, you can optionally enforce **what fields** the model must produce and **what values** they're allowed to take. Without schema rules, the validator only checks that the response is parseable JSON. With rules, it also checks the keys, types, and values against what you defined.

You configure rules in the row-based **Simple Schema Builder** — no JSON Schema knowledge required.

### The Simple Schema Builder

Each row defines one field the model's response must contain.

| Column | Purpose |
| --- | --- |
| **Field name** | The JSON key the model must produce. |
| **Type** | Text, Number, Whole number, True / False, or List. |
| **Required** | Untick to make the field optional but still type-checked when present. |
| **⚙ (gear)** | Per-type constraints (see below). |

Per-type constraints (the gear icon opens these):

| Type | Constraints |
| --- | --- |
| **Text** | **Allowed values** — a comma-separated list of acceptable strings (becomes a JSON Schema `enum`). Leave blank to allow any text. |
| **Number** | **Minimum** and **Maximum** (decimals allowed, e.g. 0.0 to 1.0). |
| **Whole number** | **Minimum** and **Maximum** (integers only, e.g. 0 to 100). |
| **True / False** | No extra constraints. |
| **List** | **Item type** — the type each array element must match (Text / Number / Whole number / True-False). |

Click **Add Field** to add more rows. Click the trash icon on a row to remove it. With no rows, the response only has to be valid JSON.

### Required vs optional

The **Required** checkbox controls whether the field's *presence* is enforced. The **Type** controls what *value* is allowed if the field is present. They're independent.

| Required | Field present in response? | Outcome |
| --- | --- | --- |
| ✓ | Yes, value matches type | Pass |
| ✓ | Yes, value wrong type | **Fail** — `Invalid type. Expected …` |
| ✓ | No | **Fail** — `Required properties are missing from object` |
| ✗ | Yes, value matches type | Pass |
| ✗ | Yes, value wrong type | **Fail** — `Invalid type. Expected …` |
| ✗ | No | Pass |

So unticking Required means "you don't have to include this field, but if you do, it has to match the type."

### Worked examples

#### Sentiment classifier (Text + Number + List)

Three rows, all required.

| Field name | Type | Required | Constraints |
| --- | --- | --- | --- |
| `sentiment` | Text | ✓ | Allowed values: `positive, neutral, negative` |
| `confidence` | Number | ✓ | Min: 0, Max: 1 |
| `keywords` | List | ✓ | Item type: Text |

Passes:

```json
{ "sentiment": "positive", "confidence": 0.85, "keywords": ["fast", "reliable"] }
```

Fails: `sentiment = "great"` (not in the allowed values), `confidence = 1.5` (out of range), `keywords = "fast, reliable"` (a string instead of a list).

#### Yes/no with reasoning (True/False + optional field)

Two rows. Reasoning is optional — the model can skip it.

| Field name | Type | Required |
| --- | --- | --- |
| `match` | True / False | ✓ |
| `reasoning` | Text | ✗ |

Passes:

```json
{ "match": true, "reasoning": "Both addresses share the same postcode." }
```

Also passes (reasoning omitted):

```json
{ "match": false }
```

Fails: `{ "match": "yes" }` (a string instead of a boolean), `{}` (missing the required `match`).

#### Numeric range (Whole number)

One row — a discrete count.

| Field name | Type | Required | Constraints |
| --- | --- | --- | --- |
| `count` | Whole number | ✓ | Min: 0, Max: 100 |

Passes: `{ "count": 42 }`.

Fails: `{ "count": 3.5 }` (not a whole number), `{ "count": 200 }` (out of range), `{ "count": "42" }` (a string).

### Lenient defaults

The validator is lenient unless you explicitly tighten it.

- **Extra keys** the model returns that aren't in your schema are **allowed**. If you only configured `sentiment` but the model returns `{ "sentiment": "positive", "extra": "blah" }`, that's still a pass.
- **A wrapping Markdown code fence** around the JSON (a single ```` ```json … ``` ```` block) is stripped before validation, so the fenced answer passes. The raw response keeps the fence; `{Score.fieldName}` and `{Score.Json.fieldName}` both read the validated JSON (see [Using extracted values downstream](#using-extracted-values-downstream)).
- **Leading and trailing whitespace** is trimmed.

If you need strict mode (reject any extra keys), open Advanced and add `"additionalProperties": false` to the raw JSON Schema.

### Errors you'll see when validation fails

The error shown on the block is the validator's reason for rejecting the attempt. Common ones:

| Error | Meaning |
| --- | --- |
| `Response does not contain a <tagName>...</tagName> tag.` | Single Tag / JSON in Tag: the named tag was missing. |
| `Response is missing the following tags: <a>, <b>.` | Multi Tag: one or more named tags were missing. |
| `Response is not valid JSON: <details>` | The response (or the inner content of the tag) couldn't be parsed as JSON at all. |
| `Configured JSON Schema is invalid: <details>` | The schema saved on the block does not parse; fix it in Advanced. |
| `Response does not match JSON Schema: Required properties are missing from object: <name>.` | A field marked Required wasn't in the response. |
| `Response does not match JSON Schema: Value "X" is not defined in enum.` | A Text field's value wasn't in its Allowed values list. |
| `Response does not match JSON Schema: Invalid type. Expected String but got Integer.` (etc.) | A field's value didn't match its row's Type. |
| `Response does not match JSON Schema: Integer X is greater than maximum value of Y.` | A Number / Whole number value exceeded the configured Max. |

> [!TIP]
> **Where the schema messages come from**
>
> The text after `Response does not match JSON Schema:` is produced by the underlying JSON Schema engine, so the exact wording can vary with the violation. The platform always prefixes it with `Response does not match JSON Schema:`, and joins several violations with `; `.

### Iterating on a schema

When a block fails validation, click **View rejected response** in the block panel to see the exact raw text the model produced. Compare it to the schema and decide whether to:

1. **Tighten the prompt** so the model produces what you asked for. This is the most common fix — the prompt usually under-specifies.
2. **Loosen the schema** — add a value to the Allowed values list, widen a Min/Max range, or untick Required for a field the model rarely produces.
3. **Switch to Advanced** if you need a feature the simple builder doesn't expose.

### Advanced (raw JSON Schema)

Toggle **Advanced (raw JSON Schema)** to flip to a raw textarea. Use it when:

- You're comfortable with JSON Schema syntax.
- You need features the simple builder doesn't expose:
  - Regex `pattern` (e.g. enforcing a postcode format on a string).
  - Nested objects (e.g. `address` is an object with its own `street` / `city` / `postcode` fields).
  - Combinators like `oneOf`, `anyOf`, `allOf`.
  - String constraints like `minLength`, `maxLength`, `format`.
  - Array constraints like `minItems`, `maxItems`, `uniqueItems`.
  - `additionalProperties: false` for strict mode.

The textarea validates as you type and surfaces `JSON Schema is invalid: …` if the schema itself won't parse.

#### Round-trip warning

The simple builder can only represent a flat object with primitive properties (plus optional enum, range, and list item type). If a schema uses anything richer, switching back to the simple builder shows a warning:

> [!WARNING]
> The current schema uses features the simple builder can't show (e.g. patterns, nested objects). Switch to Advanced to keep them, or remove and re-add fields here.

The warning fires for any root keyword other than `type` / `properties` / `required`, and for any per-property keyword the simple builder doesn't model (`pattern`, `format`, `minLength`, `maxLength`, `multipleOf`, `oneOf`, `anyOf`, `allOf`). In plain terms: if you wrote `"pattern": "^[A-Z]{3}$"` on a field in Advanced and then flip to Simple, the builder shows the field as **Text** but can't show the regex — and saving in that state **drops the regex**. The warning is telling you to keep editing in Advanced (or remove the pattern first) so detail isn't lost.

## Using extracted values downstream

Once a Single Tag / Multi Tag / JSON / JSON-in-tag block validates, each named piece becomes referenceable by a later block using the standard `{BlockName...}` [placeholder system](block-reference.md#placeholder-system). For an AI Query block named `Score`:

| Format | Reference syntax | Resolves to |
| --- | --- | --- |
| Single Tag (`tag = answer`) | `{Score.answer}` | Inner text of `<answer>...</answer>`; when the answer repeats the tag, every match in order, joined by line breaks |
| Multi Tag | `{Score.reasoning}`, `{Score.answer}`, … | One reference per tag, the inner text of every match in order, joined by line breaks |
| JSON (whole response) | `{Score.fieldName}` or `{Score.Json.fieldName}` | The value of top-level key `fieldName` (dot-path into the validated JSON); `{Score.Json}` is the whole JSON |
| JSON in Tag | `{Score.fieldName}` or `{Score.Json.fieldName}` | A sub-key of the inner JSON; `{Score.Json}` is the whole inner JSON. `{Score.tagName}` still reads the tag's inner text |
| Any | `{Score}` | The full raw response |

So an AI Query block named `Score` that emits `<rating>` and `<confidence>` lets a later [Calculate Number](block-reference.md#calculate-number) formula or prompt use `{Score.rating}` and `{Score.confidence}` directly, instead of re-parsing free text.

## What "fail" means

If **both** attempts fail validation, the block is marked **Failed**:

- The block's status changes to `Failed` with the validator error attached.
- The workflow editor surfaces it under **View Failed Blocks**.
- No `BlockResponse` is produced — there is no raw response or extracted value to read.
- The workflow continues, unless the block is marked critical ([critical blocks abort the run](../../architecture/workflow-engine/parallel-execution.md#critical-blocks-abort-the-run)). Downstream blocks still run; one that referenced the failed block receives the "no data" placeholder value.

If the **first** attempt fails but the second passes, the block is marked **Completed** and surfaces under **View Retried Blocks** (alongside any failures). The block panel shows the validator error from the rejected attempt and a **View rejected response** disclosure with the raw text the model produced the first time.

The retry uses the **same prompt** — no error feedback is fed back to the model. The only thing that changes between attempts is the model's own sampling.

## Common gotchas

- **Empty tag content** like `<answer></answer>` is valid. The reference resolves to an empty string.
- **Extra JSON keys** not in the schema are tolerated by default. Tighten with `additionalProperties: false` in Advanced if you need to reject them.
- **Multiple matches of the same tag**: the validator checks the **first** match, but a `{Score.tagName}` reference reads **every** match in order, joined by line breaks.
- **A wrapping Markdown code fence around JSON** is stripped before validation, but not from the raw response, so `{Score}` shows the fence. A dotted path walks the raw response as JSON when it parses as JSON, and otherwise looks for a `<fieldName>` tag. When neither finds a value and the format is **JSON** or **JSON in Tag**, the path walks the validated JSON (`JsonResponse`) instead (`PopulatePlaceholderStringFromSection` in `WorkflowHandler`), so `{Score.fieldName}` works with or without a fence. A tag of the same name in the raw response wins over a JSON key.
- **Leading and trailing whitespace** is trimmed.

## Limitations

- **Tag attributes** (`<answer foo="bar">...</answer>`) are not supported — the validator looks for plain `<tagName>...</tagName>`.
- **Nested same-name tags** are not supported.
- **Retry feedback** — both attempts use the identical prompt. There is no "here's what went wrong, try again" injection.
- **Chunked AI Query blocks** (**Process Pages in Batches**) validate per-chunk; each chunk gets its own retry. The extracted values are merged in chunk order with the last write winning, so for every format the **last** chunk's tag values, and for **JSON** / **JSON in Tag** its parsed JSON, are what `{Score.…}` reads — the full concatenation of every chunk lives in the raw response, as usual.

## Done when

The block's run shows **Completed** with no **View rejected response** disclosure, and a later block's "after execution" field shows the value you reference (for example `{Score.rating}`) in place of the tag.

## See also

- [Block Reference — AI Query](block-reference.md#ai-query) — the rest of the AI Query block's properties.
- [Block Reference — Placeholder System](block-reference.md#placeholder-system) — how `{BlockName...}` references resolve.
- [Block Implementation Guide](../../architecture/workflow-engine/block-implementation-guide.md) — the underlying strategy pattern, for developers.
