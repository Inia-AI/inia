---
type: explanation
status: current
description: "Explains how a parameterized section expands into one copy per variant at run time: the config model, what each copy gets, tag suffixing and params, the ClassMap that rewrites document classes per variant, document distribution, the legacy-config conversion, the class rename/delete cascade, and the editor dialog. Read when you configure a parameterized section, a variant reads the wrong documents or tags, or you add a field to SectionVariant."
---

# Parameterized Sections

**On this page:** Configuration Model · Execution-Time Expansion · Document Classes: the ClassMap · Conversion from the legacy replace mode · Document-Class Rename/Delete Cascade · Editor Dialog · Key Files

A parameterized section is a section template that the engine expands into one concrete copy per **variant** at execution time. Prompts are written once and reused across multiple document types, funds, regions, or any other dimension, instead of hand-copying a section and its blocks for each case. Like [loop processing](loop-processing.md), it uses a **template-and-clone** approach: expansion happens before any block runs, and the Core template is never mutated.

## Table of Contents


---

## Configuration Model

The configuration lives on the section as a JSON string (`Section.ParameterizedConfig`) and deserializes to `ParameterizedSectionConfig` (`platform/IniaAi.Domain/Entities/Workflows/ParameterizedSectionConfig.cs`):

```csharp
public record ParameterizedSectionConfig(
    List<SectionVariant> Variants
);

public record SectionVariant(
    string Key,                                              // tag suffix, e.g. "AR", "F2"
    string Label,                                            // display name
    Dictionary<string, string>? Params = null,               // meta-variables resolved in prompts
    string? SectionStopGate = null,                          // stop-gate template for the variant
    List<string>? SectionSupportedDocumentClasses = null,    // LEGACY input only, see Conversion below
    Dictionary<string, List<string>>? ClassMap = null        // per-block class rewrite map
);
```

A section is parameterized when `ParameterizedConfig` is non-empty. The config is edited in the workflow editor via `ParameterizedSectionConfigDialog.razor`.

---

## Execution-Time Expansion

Expansion is **execution-time only**: `WorkflowHandler.ProcessParameterizedSectionsAsync` runs during a full workflow run, after variable processing and **before** replay seeding and the signature-override preload:

1. Sections with a non-empty `ParameterizedConfig` are processed **bottom-up by `Order`** (so inserting variants does not shift the positions of sections not yet processed). `ParameterizedSectionNormalizer.Normalize` reads each config, converting a legacy one on the way.
2. `ParameterizedSectionExpander.Expand` clones the template section once per variant.
3. The documents attached anywhere in the tree are collected (`CollectDocumentsFromTree`).
4. The template section is replaced by its expanded variants (`Workflow.ReplaceSectionWithExpanded`).
5. The collected documents are redistributed to the variants' blocks (`DistributeDocumentsToVariants`).

A config that cannot be parsed, or that has no variants, is not expanded: the section's blocks are marked `Skipped` with the config error as the reason, so the problem reads as skipped blocks rather than lost results. The section then dispatches alone, like a loop section.

The editor's single-section and single-block runs do not go through the full-run path: `ExecuteSectionAsync` and `ExecuteBlockAsync` expand a parameterized section on demand (`ExpandParameterizedSectionAsync`) and run every variant.

Signature overrides need no mapping for variants: they are keyed by field name, so a variant's blocks resolve them exactly as the template's do ([Signature-override injection](parallel-execution.md#signature-override-injection-during-execution)).

### What each variant copy gets

For every variant, `ParameterizedSectionExpander.CloneSection` produces a new section with:

- Title `"<TemplateTitle> - <Key>"`, fresh `Id`, `Order = template.Order + variantIndex`.
- `TextInputTemplate` set to the variant's `SectionStopGate` (copied **verbatim**, stop gates are not param-substituted or tag-suffixed).
- `StableSectionId` suffixed with the variant key; shared-component fields copied.
- **Deep copies of `Section.Properties` and `Section.Configuration`**: a dashboard section's `GridLayout` and a separator section's `IsSeparator` survive expansion, and mutating a variant's copy never touches the template.
- Every block cloned with its `ReplacementTag` suffixed to `<tag>_<Key>` and its `StableBlockId` suffixed with the key.

### Tag suffixing and params

- **Sibling references are auto-suffixed**: a reference like `{load_FS.answer}` to a block inside the same section becomes `{load_FS_<Key>.answer}` in every templated field of every block: the block properties, the output sub-title, the block text inputs and the filter rules, keywords and smart filter expressions included. The expander rewrites through `Block.TransformAllTemplateStrings`, which walks the same fields as `Block.GetAllTemplateStrings` (reflection over every authored string, string-dictionary value and string-list item), so a templated field added to a block type is covered without a change here.
- **Cross-section references are NOT suffixed.** To reference an expanded block from outside (or another expanded section), use a nested param, e.g. `{Leverage_OM_{FUND_ID}.answer}` with a `FUND_ID` param per variant.
- **Params are literal string replacements** applied after sibling suffixing: each `{<name>}` occurrence of a param name is replaced with the variant's value (names conventionally start with `_`, e.g. `{_PERIOD_DESC}`). Param values may themselves contain `{tag}` or `{Variable}` references, which resolve later during normal execution. Because suffixing runs first, a sibling tag inside a param value is **not** suffixed.
- **Block `TextInputs` are rewritten like every other templated field**, so a skip-check input such as `AF11 = {sibling.stop}` reads the variant's own sibling. Only the section stop gate (`SectionStopGate`) is copied verbatim.
- **Agent templates are rewritten with the same rewrite.** An Agent block's skills (content and description), task prompt and system prompt override render under the double-brace grammar, which the reflection walk does not see, so `Block.TransformAllTemplateStrings` also calls `BlockProperties.TransformAgentTemplateTags`: each <code>{{tag}}</code> goes through the variant rewrite as `{tag}`, and the result comes back in double braces (a param value is spliced as text, and a tag inside it, such as `{InterimPeriod} month`, comes back as <code>{{InterimPeriod}}</code>). Single-brace text is never touched, and a skill file (asset) is touched only when its author ticked **Fill <code>{{tags}}</code> in this file** (`FillTags`), in which case its content is rewritten like skill content. The dependency map scans each variant through the same call.
- **MergeJson `JsonInputs`** are a string list, so the walk rewrites them: `{summary}` becomes `{summary_N}`.

---

## Document Classes: the ClassMap

Each variant carries one mechanism for document classes: a **rewrite map** from a block's own class to the classes the variant should read instead.

```json
{ "Fund1_AuditedFinancials": ["Fund2_AuditedFinancials"],
  "Fund1_OfferingMemo": ["Fund2_OfferingMemo", "Fund2_SupplementalMaterials"] }
```

- Blocks keep their **own** `SupportedDocumentClasses`; each class is translated through the map. Blocks needing different documents per block just declare different classes.
- A source may **fan out** to several targets (the value is a list); results are de-duplicated.
- A class not present in the map, including the `Any`/`None` sentinels, passes through unchanged. Sentinel **keys** in a map are ignored, and so is an explicitly **empty target list** (emptying a block's classes would mean "all documents").
- Sentinels are valid **targets**: mapping to `["Any"]` gives the block all documents, `["None"]` starves it.
- A null or empty map means "no rewrites": every block keeps its own classes verbatim.
- **Single-class fields map too**: a block that creates a document (`IProducesDocumentBlockProperties.CreatesDocumentClass`, Json2Xlsx and Intelligent Search) and Json2Xlsx's `InputDocumentClass` take the first class their class maps to, so each variant writes and reads its own class (`ParameterizedSectionExpander.ApplyVariantToBlock`, which the dependency map uses too).

### Document distribution

After expansion, `DistributeDocumentsToVariants` attaches documents by each block's (post-mapping) classes: empty list or `Any` attaches all documents; `None` attaches none; otherwise documents whose `Class` matches. Blocks with no RAG settings never receive document content regardless of attached documents.

---

## Conversion from the legacy replace mode

Variants used to carry a single flat list (`SectionSupportedDocumentClasses`) that **overwrote every block's classes** at expansion. That mode is retired, and the stored configs were converted:

- **The `ConvertParameterizedSectionClassMaps` data migration** (July 2026, hand-written SQL for both providers) rewrote every legacy config in the Core database: all blocks of a converted section got the **base class** (the first non-sentinel class scanning the variants in order) and each variant got a ClassMap translating that base to its legacy list. A null/empty legacy list became `["Any"]` (all documents, the legacy behavior); a list equal to the base collapsed to an empty map. Blocks were rewritten because legacy semantics ignored block classes entirely, so the stored values were dead data. **The legacy list itself was retained**, so code that ignores `ClassMap` and reads the list still behaves as before. Every database ran the migration before release 2.2.0: since the squash it is part of the squashed history, and startup refuses a database that never reached 2.2.0 ([Database Services](../services/infrastructure/database-services.md#migration--startup)).
- **`ParameterizedSectionNormalizer`** (`platform/IniaAi.Core/Workflows/`) applies the same rule in-memory at every load boundary: execution, single-section runs, editor load, version restore, file import, and the config dialog. This covers legacy JSON that the migration could not reach, notably restored `WorkflowVersion` snapshots (history is never rewritten) and raw saves through the templates API.
- Sections whose variants only ever said `Any`/empty convert to **empty block classes plus empty maps** (all documents for every variant, the legacy behavior). A sentinel mix involving `None` is not expressible as a map and is left unconverted.

Run-time behavior of the conversion is pinned by `ParameterizedSectionNormalizerTests`.

---

## Document-Class Rename/Delete Cascade

Renaming or deleting a document class on the workflow cascades into every variant config (`Workflow.RenameDocumentClassKey` / `RemoveDocumentClass`):

- **Rename** rewrites ClassMap **keys and target-list elements** (de-duplicating within a list). If a rename collapses a renamed key onto a pre-existing entry, the pre-existing entry wins deterministically.
- **Delete** removes entries whose **key** is the deleted class and removes the class from **target lists**; a target list that empties becomes `["None"]`, so the affected blocks receive no documents rather than silently falling back to the source class's documents. An emptied map stays `{}`. A **block** inside a parameterized section whose own classes empty on delete falls back to `None` (not the plain-section `Any` fallback): block classes are the variant maps' pivot, and `Any` would silently feed every variant all documents.

---

## Editor Dialog

`ParameterizedSectionConfigDialog.razor` edits variants in tabs (Key / Label / Stop Gate, class mapping, params). The mapping table is **auto-derived**: one row per distinct class found on the section's blocks, source read-only, target a multi-select of the org's classes plus the `Any`/`None` sentinels (mutually exclusive with real classes), where an empty selection means "unchanged". Saved mappings whose source class is no longer used by any block render as removable stale rows. Row derivation and persistence logic live in `platform/IniaAi.WebApp/Services/VariantClassMapEditor.cs` (identity and unchanged rows are omitted on save).

Blocks inside a parameterized section keep their normal class picker (`DocumentClassSelector.razor`), with a hint that variants translate the classes at run time.

> [!WARNING]
> **Round-trip rule for new config fields**
>
> `Confirm()` rebuilds each `SectionVariant` from the dialog's edit model. Any new field added to `SectionVariant` **must** also be added to the dialog's load/save path, or it is silently wiped the first time someone opens the dialog and saves. Engine changes and dialog changes ship together.

---

## Key Files

| Concern | File |
|---|---|
| Config model | `platform/IniaAi.Domain/Entities/Workflows/ParameterizedSectionConfig.cs` |
| Expansion engine | `platform/IniaAi.Core/Workflows/ParameterizedSectionExpander.cs` |
| Legacy conversion | `platform/IniaAi.Core/Workflows/ParameterizedSectionNormalizer.cs` |
| Execution orchestration | `platform/IniaAi.Core/Workflows/WorkflowHandler.cs` (`ProcessParameterizedSectionsAsync`) |
| Rename/delete cascade | `platform/IniaAi.Domain/Entities/Workflows/Workflow.cs` |
| Editor dialog | `platform/IniaAi.WebApp/Components/WorkflowEditor/Dialogs/ParameterizedSectionConfigDialog.razor` |
| Class-map editor logic | `platform/IniaAi.WebApp/Services/VariantClassMapEditor.cs` |
| Tests | `ParameterizedSectionExpansionTests`, `ParameterizedSectionNormalizerTests`, `DocumentClassCascadeTests` |
