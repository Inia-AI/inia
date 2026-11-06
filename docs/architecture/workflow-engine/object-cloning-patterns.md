---
type: explanation
status: current
description: "Explains how workflow entities clone: the CloneHelper.CopyScalars copy modes, the three-phase clone methods of Workflow, Section and Block, when to mark a property [CloneIgnore], how ExecutionState is shared, and the gotchas. Read when you add a field or an entity to a workflow template, or a field goes missing after a duplicate, an execution or an upload."
---

# Object Cloning Patterns

**On this page:** Why this pattern exists · The pattern · What `CopyScalars` does · `[CloneIgnore]` — when and where · Adding a new field to an existing entity · Adding a new clone-bearing entity · `ExecutionState` semantics · Gotchas · Related Documentation

Workflow entities (`Workflow`, `Section`, `Block`, `BlockProperties`, `TextInput`, `RagSettings`, `BlockResponse`, `WorkflowVariable`, `DocumentClass`, `WorkflowRegressionField`, `WorkflowSignatureField`) use a convention-based cloning pattern built on `CloneHelper.CopyScalars` and the `[CloneIgnore]` attribute. This page explains how the pattern works, how to add fields to existing entities safely, and how to add new clone-bearing entities.

The template hierarchy is three levels deep — `Workflow → Section → Block` — and the clone pattern recurses through all three: `Workflow.CloneTo` clones each `Section` via `Section.CloneTo`, which clones each `Block` via `Block.CloneToSection`. A `Block` is parented by a `Section` (via `Block.SectionId` / `Block.Section`), not directly by a `Workflow`.

## Why this pattern exists

Every clone-bearing entity used to maintain a hand-written object-initializer block listing every field to copy:

```csharp
public override BlockProperties Clone()
{
    return new AiQueryProperties
    {
        Type = Type,
        UserPromptTemplate = UserPromptTemplate,
        SystemPromptTemplate = SystemPromptTemplate,
        // ...19 more fields...
        ExecutionState = ExecutionState,
    };
}
```

Adding a new field meant editing three places (the property itself, the serializer, *and* the `Clone()` override). Forgetting the third step silently dropped the new field during workflow execution, persistence, or duplication — a hard-to-spot class of bug that hit the codebase repeatedly.

The pattern replaces the per-class field list with reflection-driven copying and an explicit opt-out attribute, so **new fields are cloned automatically unless you mark them otherwise**.

## The pattern

Every clone method on a workflow entity has three phases:

1. **Construct into the new owner.** The constructor sets nav properties (`WorkflowId`, `Workflow` on `Section`; `SectionId`, `Section` on `Block`; `BlockId`, `Block` on a `TextInput`) and adds the clone to the parent's collection.
2. **Copy scalar fields.** A single call to `CloneHelper.CopyScalars(source, target)` handles everything reflection can see.
3. **Walk child collections explicitly.** Nested entities (`Sections`, `Blocks`, `TextInputs`, `RagSettings`, `BlockResponse`, etc.) are still cloned with explicit `foreach` loops — they carry their own semantics and can't safely be automated.

### Example: `Block.CloneToSection`

A `Block` clones into a `Section` (its parent), not directly into a `Workflow`. Phases 2 and 3 for the block's own content live in `CopyContentTo`, which `CloneDetached` (a clone with no section) shares:

```csharp
public Block CloneToSection(Section section, int index = -1, string? replacementTag = null)
{
    // Phase 1: construct into new owner (added/inserted into the target Section)
    var clone = new Block { Type = Type };
    if (index == -1)
    {
        section.Add(clone);
    }
    else
    {
        section.Insert(index, clone);
    }

    // Phases 2 and 3: CopyScalars, Properties.Clone(), then TextInputs and RagSettings
    CopyContentTo(clone);
    clone.Section = section;
    clone.SectionId = section.Id;

    clone.ReplacementTag = replacementTag
        ?? (!string.IsNullOrWhiteSpace(ReplacementTag)
            ? ReplacementTag
            : section.MintFreeReplacementTag(DEFAULT_TAG_PREFIX, clone.Order));

    _ = BlockResponse?.CloneToBlock(clone);

    return clone;
}

private void CopyContentTo(Block clone)
{
    CloneHelper.CopyScalars(this, clone);
    clone.Properties = Properties.Clone(); // polymorphic — must dispatch through the virtual Clone

    foreach (TextInput input in TextInputs)
    {
        _ = input.CloneToBlock(clone);
    }

    foreach (RagSettings ragSetting in RagSettings)
    {
        _ = ragSetting.CloneToBlock(clone);
    }
}
```

### Example: `Section.CloneTo`

A `Section` clones its scalars, deep-clones its `Properties` (`SectionProperties`) and `Configuration`, then recurses into each child `Block` (ordered by `Order`) via `Block.CloneToSection`:

```csharp
public void CloneTo(Section destination)
{
    destination.Clear();
    CloneHelper.CopyScalars(this, destination);
    destination.Properties = Properties.Clone();
    destination.Configuration = Configuration?.Clone();

    foreach (Block block in Blocks.OrderBy(b => b.Order))
    {
        _ = block.CloneToSection(destination);
    }
}
```

### Example: `Workflow.CloneTo`

The root `Workflow` clones its scalars and `Configuration`, then walks its five child collections — `Variables`, `DocumentClasses`, `RegressionFields`, `SignatureFields`, and `Sections` (ordered by `Order`). Each section is created with `new Section(section.Name, destination)` and then deep-cloned via `Section.CloneTo`:

```csharp
public void CloneTo(Workflow destination)
{
    destination.Clear();
    CloneHelper.CopyScalars(this, destination);
    destination.Configuration = Configuration?.Clone();

    foreach (WorkflowVariable variable in Variables)
    {
        _ = variable.CloneToWorkflow(destination);
    }

    foreach (DocumentClass documentClass in DocumentClasses)
    {
        _ = documentClass.CloneToWorkflow(destination);
    }

    foreach (WorkflowRegressionField regressionField in RegressionFields)
    {
        _ = regressionField.CloneToWorkflow(destination);
    }

    foreach (WorkflowSignatureField signatureField in SignatureFields)
    {
        _ = signatureField.CloneToWorkflow(destination);
    }

    foreach (Section section in Sections.OrderBy(s => s.Order))
    {
        var destinationSection = new Section(section.Name, destination);
        section.CloneTo(destinationSection);
    }
}
```

`Workflow.CloneToPreservingIds` runs `CloneTo`, copies the source `Id`, re-applies the source Ids onto the cloned graph (`OverwriteChildIds → OverwriteBlockIds`), then re-syncs every nav FK via `FixNavigationProperties` (see below).

### Example: `BlockProperties` subclass

Because `BlockProperties` are owned by a `Block` (no nav ref back), the pattern collapses to two lines:

```csharp
public override BlockProperties Clone()
{
    var clone = new AiQueryProperties();
    CloneHelper.CopyScalars(this, clone);
    return clone;
}
```

## What `CopyScalars` does

`IniaAi.Common.Reflection.CloneHelper.CopyScalars<T>(T source, T target)` reflects over `target.GetType()` once per type (results are cached) and copies every property that satisfies all of:

- Public instance property
- Has **both** a public getter **and** a public setter (`GetMethod.IsPublic && SetMethod.IsPublic`)
- Is **not** marked `[CloneIgnore]`

Properties with a private, internal, or protected setter are skipped by default — `{ get; private set; }` child collections like `Block.TextInputs`, `Section.Blocks`, `Workflow.Sections`, etc. are never touched by the helper, even without `[CloneIgnore]`. The `[CloneIgnore]` annotations on those collections are intentionally kept as explicit markers of intent rather than relied on for enforcement.

Each property is classified into one of three copy modes at cache-build time:

1. **Duplicate** — concrete `List<T>` or `Dictionary<TKey, TValue>`. The helper builds a new instance and copies the elements (or entries) across. Scalars of the source can mutate their copies without corrupting the original.
2. **Assign** — everything else that isn't collection-shaped: strings, numbers, enums, `DateTime`, nullable value types, and reference types like `ExecutionState`. The value is assigned directly (shallow reference copy for classes). `string` is special-cased so it isn't mistaken for `IEnumerable<char>`.
3. **Throw** — anything else that looks collection-shaped. Specifically, the helper classifies a property as "collection-shaped" if `PropertyType.IsArray` is true *or* `typeof(IEnumerable).IsAssignableFrom(PropertyType)` is true (with `string` special-cased so it isn't mistaken for `IEnumerable<char>`). That catches every common case: `ICollection<T>`, `IList<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, `IEnumerable<T>`, `HashSet<T>`, `SortedSet<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>`, `SortedDictionary<,>`, and arrays (`T[]`). An `InvalidOperationException` is raised at **cache build time** — i.e. the first time anything clones an instance of that type in the process — naming the declaring type and property and suggesting `List<T>` / `Dictionary<TKey, TValue>` or `[CloneIgnore]`.

```csharp
// Source and clone share the List<string> reference? No — CopyScalars duplicates it.
var clone = new IntelligentSearchProperties();
CloneHelper.CopyScalars(source, clone);
clone.IncludeDomains.Add("example.com"); // source.IncludeDomains unaffected
```

Non-collection reference types (like nested `ExecutionState` objects) are copied by reference. This is intentional — see [ExecutionState semantics](#executionstate-semantics).

## `[CloneIgnore]` — when and where

Add `[CloneIgnore]` to a property when any of the following is true:

| Reason | Examples |
| --- | --- |
| The **constructor** already sets it | `Section.WorkflowId`, `Section.Workflow`, `Block.SectionId`, `Block.Section`, `TextInput.Block`, `TextInput.BlockId` |
| It's a **child collection** managed by an explicit walk | `Workflow.Sections`, `Workflow.Variables`, `Workflow.DocumentClasses`, `Workflow.RegressionFields`, `Workflow.SignatureFields`, `Section.Blocks`, `Block.TextInputs`, `Block.RagSettings` |
| It needs **polymorphic** or **deep** handling `CopyScalars` can't provide | `Block.Properties` (dispatches to subclass `.Clone()`), `Section.Properties` (`SectionProperties.Clone()`), `Workflow.Configuration` (explicit deep clone) |
| It's an **identity / audit** field that should stay fresh on the clone | `Entity<T>.Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, etc. |
| It has a **setter with side effects** that would throw on a self-valued re-assignment | `TextInput.ReplacementTag` (throws on duplicate tag inside the same block) |
| It would break ordering invariants | `Section.Order` (managed by `Workflow.Insert` / `Workflow.MoveSection`), `Block.Order` (managed by `Section.Insert` / `Section.MoveBlock`), `Block.ReplacementTag` |

All `Entity<T>` audit fields are already marked `[CloneIgnore]` at the base class — derived entities inherit this automatically.

## Adding a new field to an existing entity

Add the property as normal. **Do nothing else** if it's a scalar, a `List<T>`, or a `Dictionary<TKey, TValue>`. The helper will pick it up on the next clone.

```csharp
// In AiQueryProperties — add new property and you're done.
[JsonProperty(nameof(NewModelParameter))]
public string? NewModelParameter { get; set; }
```

The only question worth asking is: *should this field be reset, deep-copied, or shallow-copied on clone?*

- **Simple scalar** (string, number, enum, bool): default shallow assignment is correct. Nothing to do.
- **`List<T>` or `Dictionary<TK, TV>` of primitives**: duplicated automatically. Nothing to do.
- **Complex nested object that needs its own deep clone** (e.g. another `Workflow`-like aggregate): mark it `[CloneIgnore]` and handle it explicitly in the clone method.
- **Other collection shapes** (`ICollection<T>`, `IList<T>`, `HashSet<T>`, arrays, etc.): the helper will throw an `InvalidOperationException` the first time anyone clones an instance of the declaring type. Either change the property to a concrete `List<T>` / `Dictionary<TK, TV>`, or mark it `[CloneIgnore]` and handle it by hand.

One consumer worth knowing about: the `SharedComponent*` link fields on `Block` and `Section` (see [Shared Components](shared-components.md)) rely on exactly this default — no `[CloneIgnore]`, so every clone, duplicate, and copy path carries them verbatim, which is what makes "a duplicate of a live copy is another live copy" true with zero per-path code. The one manual construction site that bypasses `CopyScalars` — `ParameterizedSectionExpander.CloneSection` builds its `Section` with an explicit initializer — copies the link fields by hand — the four plus `SharedComponentSlotStates` — cloning the bindings and slot-state dictionaries per variant. If you add a Section-level field, that expander initializer is the one place the reflection convention will not save you.

## Adding a new clone-bearing entity

When adding a brand new entity that needs to be cloned (rare — most new block types only add a `BlockProperties` subclass), follow this checklist:

1. **Inherit from `Entity<T>`** if it's persisted — audit fields are already `[CloneIgnore]`.
2. **Add a constructor** that takes the target owner and attaches `this` to the owner's collection. Set `*Id` and nav ref inside the constructor.
3. **Mark nav refs and child collections `[CloneIgnore]`.** Anything the constructor or an explicit walk manages must not be touched by the helper.
4. **Write the clone method** in the three-phase shape:
   ```csharp
   public MyEntity CloneToOwner(Owner owner)
   {
       var clone = new MyEntity(owner);
       CloneHelper.CopyScalars(this, clone);
       // Explicit walks for any child collections here.
       return clone;
   }
   ```
5. **Add a targeted unit test** populating every scalar field on the source and asserting equality on the clone — it becomes the regression net for future field additions. Example: `BlockPropertiesCloneTests.AiQueryProperties_Clone_CopiesEveryField`.

## `ExecutionState` semantics

`ExecutionState` properties on `Block`, `TextInput`, `RagSettings`, `Section`, `Workflow`, and every `BlockProperties` subclass are **shallow reference-copied** by design. This is safe because:

- **Pre-execution callers** clone a freshly-loaded template: `WorkflowExecutionOrchestrator` and `JobExecutor.Execute` (both `CloneToPreservingIds`), then `WorkflowHandler.BuildExecutedWorkflowAsync`, which makes the fresh-id clone a run persists as its UserData snapshot. `ExecutionState` is `[NotMapped]`, so it is null on a template loaded from the database. The run populates it later via `SetExecutionState`, which **assigns a new instance** — breaking the shared reference before anything mutates it.
- **Callers that clone a run** rely on the clone holding the *same* `ExecutionState` instance as the source. The editor's file upload (`WorkflowEditor`) clones an uploaded workflow, which may be an executed run's download, and keeps its execution states, so the editor shows the run's resolved values.

`UpdateExecutionState` uses `??=` and would mutate a shared instance if called before `SetExecutionState`, but the execution pipeline always calls `SetExecutionState` first, so this never bites in practice. Do not change this contract without understanding both caller classes.

## Gotchas

**Private / internal / protected setters are skipped by default.** `CopyScalars` requires `SetMethod.IsPublic == true`. That means `{ get; private set; }` properties like `Block.TextInputs`, `Section.Blocks`, `Workflow.Sections`, `Workflow.Variables`, `Workflow.DocumentClasses` are silently skipped even without `[CloneIgnore]`. The annotation is still applied to those properties as an explicit marker of intent — future readers should not have to guess why a property isn't copied, and the annotation keeps the contract visible at the property site.

**Polymorphism is not automatic.** `Block.Properties` is typed as the abstract `BlockProperties` base. `CopyScalars` would shallow-copy the reference, making the clone share the source's configuration object. `[CloneIgnore]` the property and assign `clone.Properties = Properties.Clone();` explicitly so the subclass override runs.

**`WorkflowConfiguration` is deep-cloned by hand.** The nested `GridLayoutSettings` / `ReportLayout` / `CustomSettings` hierarchy has complex-element lists that `CopyScalars` can't duplicate. `Workflow.Configuration` is `[CloneIgnore]`d and deep-cloned inside `Workflow.CloneTo`. If you add new sub-fields to `WorkflowConfiguration`, update the hand-written block in `Workflow.CloneTo` as well.

**Arrays and non-`List<T>` collections are intentionally rejected.** Even though `byte[]`, `HashSet<T>`, and `ICollection<T>` all look cloneable, the helper throws on them. This is deliberate: `List<T>` and `Dictionary<TK, TV>` cover every case currently in the domain, and forcing new fields to use them (or explicit `[CloneIgnore]`) keeps the duplication contract obvious at the call site. The one current exception is `BlockResponse.BinaryResponse` (`byte[]`), which is `[CloneIgnore]`d and reference-copied by hand inside `BlockResponse.CloneToBlock` to preserve pre-refactor behavior.

**Nav FKs are re-synced after clone, not by `CopyScalars`.** Because `Section.WorkflowId`/`Section.Workflow` and `Block.SectionId`/`Block.Section` are `[CloneIgnore]`d, `CopyScalars` never copies them — the recursion re-wires them instead. `Workflow.FixNavigationProperties` walks the cloned (or freshly deserialized) graph and, for each section, sets `section.WorkflowId = workflow.Id`; for each block, sets `block.Section = section` and `block.SectionId = section.Id`. `Workflow.FromJson` runs `NormalizeSectionOrdering`, per-section `NormalizeBlockOrdering`, then `FixNavigationProperties`, so off-disk workflows come back with a consistent FK graph; it then hoists legacy block-level regression and signature field definitions to the workflow level (`RegressionSchemaLegacyMigrator.Hoist`, `SignatureFieldsLegacyMigrator.Hoist`).

**`Block.SectionId` is nullable by design.** A `Block` can exist without a parent `Section` (`string? SectionId`, `Section? Section`) — for example transient or orphan rows. Do not treat a NULL `SectionId` as corruption; the clone path always sets it, but the property is intentionally nullable on the entity.

**Reflection caches per runtime type.** `source.GetType()` is the cache key, so each concrete subclass builds its own metadata once. Attribute changes take effect on the next process start — and so do the strict collection checks: a field you add today with an unsupported type won't surface the throw until the first clone of that class runs in a new process.

## Related Documentation

- [Block Strategies](block-strategy-pattern.md) — Strategy pattern and how blocks are executed
- [Execution Context](execution-context.md) — How `ExecutionState` is populated during execution
