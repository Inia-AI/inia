---
type: explanation
status: current
description: "Documents the VeeValidate and Zod form pattern, the TanStack table column factories with their row-control guard, the composables inventory and where shared types and enums live. Read when you add or change a form, a table or a composable in the Vue app, or look for an existing composable before writing one."
---

# Forms, Tables & Composables

**On this page:** Form pattern: VeeValidate + Zod · Tables: TanStack Vue Table column factories · Composables inventory · Types, enums & contracts

The IniaAi.Web frontend leans on three repeating building blocks: VeeValidate + Zod forms wired through the shadcn-style `ui/form` primitives, TanStack Vue Table column factories that render cells with `h()`, and a folder of small composables that own cross-cutting behaviour (debouncing, confirm-then-mutate, route-synced query state, live durations). This page documents those patterns so a maintainer can add a new form, table, or composable that matches the rest of the codebase. For the API/query layer the forms and tables sit on top of, see [API & Data Layer](api-layer.md); for the wider component taxonomy see [Components & Design System](components-and-design-system.md).

## Form pattern: VeeValidate + Zod

Forms live in organism directories that pair a `.vue` component with a colocated `schema.ts`, e.g. `src/components/organisms/CounterpartyForm/{CounterpartyForm.vue,schema.ts}`. The schema file does **not** export a static Zod object — it exports a builder that takes the i18n translator and returns a `z.object`, so validation messages are localized:

```ts
// src/components/organisms/CounterpartyForm/schema.ts
export const buildCounterpartyFormSchema = (t: (key: string) => string) =>
  z.object({
    id: z.string().min(3, t('shared.components.counterpartySelect.referenceId.validation.minLength')),
    name: z
      .string()
      .min(1, t('page.counterpartyManagement.form.validation.nameRequired'))
      .refine(isValidCounterpartyName, {
        message: t('page.counterpartyManagement.form.validation.nameForbiddenCharacters'),
      }),
    // email / website are `.nullish()` and validated only when non-blank
    counterpartyTypeIds: z.array(z.string()).min(1, t('...typeRequired')),
  })
```

The component feeds that schema to `useForm` as it is. VeeValidate 5 reads a Zod 4 schema through the Standard Schema interface, so no adapter wraps it (the `toTypedSchema` of the retired `@vee-validate/zod` package is gone). VeeValidate 5 does not take a form's initial values from schema defaults and does not read a required flag from the schema: a form sets its initial values itself, as below.

```ts
// src/components/organisms/CounterpartyForm/CounterpartyForm.vue
const { t: $t } = useI18n()
const schema = computed(() => buildCounterpartyFormSchema($t))

const form = useForm<CounterpartyFormValues>({
  validationSchema: schema,
  initialValues: formData.value,
})
```

### Edit mode, initial values, and reset

`CounterpartyForm` keeps a `formData` ref as the single two-way-bound source for the template, derived from `props.initialValues` (falling back to `defaultValues`). Edit vs create is a computed off the presence of an id:

```ts
const isEditMode = computed(() => !!props.initialValues?.id)
const formData = ref<CounterpartyFormValues>(
  props.initialValues ? fromInitialValues(props.initialValues) : { ...defaultValues },
)
```

When the parent swaps `initialValues` (e.g. the user picks a different row to edit), an `immediate` watcher rebuilds `formData` and calls `form.resetForm` so VeeValidate's internal state, dirty flags, and field values all re-baseline:

```ts
watch(
  () => props.initialValues,
  newValues => {
    if (newValues) {
      Object.assign(formData.value, fromInitialValues(newValues))
      isIdManuallyEdited.value = !!newValues.id
    } else {
      Object.assign(formData.value, { ...defaultValues })
      isIdManuallyEdited.value = false
    }
    form.resetForm({ values: formData.value })
  },
  { immediate: true },
)
```

Submission goes through `form.handleSubmit`, which only fires after validation passes. The handler transforms the raw values before emitting — here `blankToNull` normalizes empty email/website to `null` — and the component communicates outward purely through `submit` and `cancel` events rather than mutating anything itself:

```ts
const handleSubmit = form.handleSubmit(data => {
  $emits('submit', { ...data, email: blankToNull(data.email), website: blankToNull(data.website) })
})
```

> [!TIP]
> Keep the schema builder pure and i18n-driven, keep transformation (trimming, blank-to-null, slug generation) in the submit handler, and let the parent own persistence. The form component stays a controlled, emit-only unit — easy to reuse from a dialog or a page.

The form has one submit control: the `Button` with `type="submit"`. Enter in any field submits through it. Cancel is a plain button that emits `cancel` and never submits, because of the shared `Button`'s default type (see [primitive forwarding](components-and-design-system.md#primitive-forwarding)).

The counterparty form also carries a **freeform property bag**. `CounterpartyDetails.properties` is a `Record<string, string>` on the wire, and the form keeps it in a `propertiesRecord` ref *outside* the VeeValidate schema — its keys are user-coined, so there is nothing fixed to validate. The ref is the `v-model` of the `KeyValueEditor` molecule (`src/components/molecules/KeyValueEditor/`), which edits it as rows of `{ key, value }`: it trims each key, drops a row whose key is blank, treats two keys that differ only in capitals as one (the later row wins) and marks such a pair with its duplicate-key message. The submit handler sends the record as it stands. The editor shows for every counterparty — there is no type gate. An empty bag emits as `{}`, which the API treats as "clear it"; omitting `properties` entirely (never done by this WYSIWYG form) is what means "leave unchanged".

### Field binding via `ui/form`

Fields use VeeValidate's `Field` component (aliased `FormField`) with a scoped slot exposing `componentField`, which is spread onto the input so VeeValidate wires `value` + `onChange`/`onBlur`. The colocated `formData` ref provides `v-model` for the template's own two-way binding, and `FormMessage` renders the active error:

```vue
<FormField v-slot="{ componentField }" name="name">
  <FormItem>
    <FormLabel>Name</FormLabel>
    <FormControl>
      <Input v-bind="componentField" v-model="formData.name" placeholder="Enter name" />
    </FormControl>
    <FormMessage />
  </FormItem>
</FormField>
```

The `FormItem` / `FormLabel` / `FormControl` / `FormMessage` wrappers come from `src/components/ui/form`. They coordinate accessibility ids and error state through the `useFormField` hook (`src/components/ui/form/useFormField.ts`), which injects the VeeValidate field context plus the per-item id and returns the field's reactive state plus the derived ids:

```ts
// src/components/ui/form/useFormField.ts
const { name } = fieldContext            // from inject(FieldContextKey)
return {
  id, name,
  formItemId: `${id}-form-item`,
  formDescriptionId: `${id}-form-item-description`,
  formMessageId: `${id}-form-item-message`,
  valid: useIsFieldValid(name),
  isDirty: useIsFieldDirty(name),
  isTouched: useIsFieldTouched(name),
  error: useFieldError(name),
}
```

`useFormField` throws if called outside a `<FormField>`, so the `ui/form` primitives must always be nested under one.

`FormControl` points its control's `aria-describedby` only at what is on screen: the item's `FormDescription` while one is rendered (it registers itself with its `FormItem`), and the `FormMessage` while the field has an error. A field with neither carries no `aria-describedby`, so no reference names an element that is not there.

## Tables: TanStack Vue Table column factories

Data tables are built on `@tanstack/vue-table` (v8). Each table module under `src/tables/` default-exports a **factory function** that takes the view's callbacks and state as `Options` and returns `{ columns }` typed as `ColumnDef<RowType>[]`. The view supplies row-action handlers and selection state; the module translates its own captions and control names through `useI18n`, so the names a test reads are the names a user hears. A row link whose cell text repeats across rows takes its name from the link column's `meta.rowLinkLabel` (typed in `src/types/tanstack-table.d.ts`), so the columns alone carry it to `DataTable`.

| Table module | Row type | Notable options |
|---|---|---|
| `src/tables/counterparty.ts` | `CounterpartyRow` | `onAdd` / `onEdit` / `onDelete` row actions, `toggleRow` + `expandedRowIds` for tree expansion |
| `src/tables/documents.ts` | `DocumentRow` | `selection: Ref<Set<string>>`, `allSelected: ComputedRef<'off'|'on'|'mixed'>`, `onToggleRow`/`onToggleAll`, `canDelete` |

Other tables follow the same shape: `financialAnalysis.ts` and `modelPricing.ts` in `src/tables/`.


### Cells render through `h()`, not templates

Column `cell` and `header` functions return render-function output via `h(Component, props, children)` rather than Vue templates. This keeps columns as plain TypeScript modules (no SFC) and lets them compose project components like `IniaIcon`, `HighlightedText`, and `Checkbox` directly:

```ts
// src/tables/documents.ts
{
  accessorKey: 'name',
  header: () => t('page.documents.table.name'),
  enableSorting: true,
  cell: ({ row }: { row: Row<DocumentRow> }) =>
    h('div', { class: 'document-name-cell' }, [
      h(IniaIcon, { name: mediaTypeIconName(row.original.mediaType), classes: 'document-name-icon' }),
      h('span', { title: row.original.name }, row.original.name),
    ]),
}
```

### Augmenting the row type

The row type is the API contract type intersected with the extra fields the view pre-computes, so cell renderers can read display-ready data straight off `row.original`:

```ts
// src/tables/documents.ts
export type DocumentRow = DocumentSummary & {
  categoryName: string   // resolved from the categories query in the view
  userName: string       // resolved from the users query in the view
}
```

`src/tables/counterparty.ts` does the same with `CounterpartyRow = Counterparty & { type?, parentName?, searchQuery?, isContextParent? }`, carrying the current search query down to the cell so `HighlightedText` can highlight matches.

### Row actions and selection

Row actions are passed in as functions on `Options` and invoked from an `actions` column. An icon action is a native button built by `tableActionButton` (`src/tables/tableActionButton.ts`): it carries its name as both `aria-label` and `title`, stops the click from reaching the row, and takes the shared `.hit-target` and `.table-action-button` classes from `src/assets/main.css`: the first gives it a target of at least 24px each way around its icon, the second its look and focus ring. Its icon colour is a theme token, such as `text-muted-foreground`, `text-info` or `text-error`. `DataTable` ignores a row click that starts on a button, link, label or form control, so an action never opens the row. Each row control is named for its row ("Delete company Peer A", "Download invoice.pdf", "Edit pricing of Claude Sonnet"), so a screen reader that lists the page's buttons can tell one row's Delete from another's. The Documents row checkbox is named the same way ("Select invoice.pdf") and sits in a `label`, so a click beside it ticks it rather than opening the document.

Where the rows of a list are repeats of one thing, the item's name is not enough, and the name adds the time that tells the rows apart. The Reports list describes a report by its type, counterparty and date added (`useReportDescription`, `src/composables/useReportDescription.ts`): **View Report** keeps its visible text at the start of a name that adds that description ("View Report Credit Memo for Pinebrook Capital, added 01/10/2026 16:00:00"), and the row link in the Type column is named by the description.


```ts
// src/tables/counterparty.ts
{
  id: 'actions',
  header: () => h('div', { class: 'text-right w-full' }, 'Actions'),
  cell: ({ row }) => h('div', { class: 'flex justify-end gap-3 actions-cell' }, [
    tableActionButton({ label: actionLabel('edit', row), class: 'action-icon action-icon-edit', icon: 'pencil', onActivate: event => onEdit(row, event) }),
    /* conditional add-child + delete buttons */
  ]),
}
```

A `rowControls.test.ts` guard in a `tables/` folder (`src/tables`) mounts every table module of its folder in `DataTable` with a few rows and asserts, through `describeRowControls` and its `rowControlProblems` check (`src/test/helpers/rowControls.ts`), that every element that reacts to a click has a control role, takes a Tab stop and has a name; that no control sits inside another; and that no two rows share a control name. It also lists the folder's modules, so a new table fails the guard until it is added with its fixtures.


Multi-select tables model selection as a `Ref<Set<string>>` of row ids plus a tri-state header checkbox driven by an `allSelected` computed (`'off' | 'on' | 'mixed'`, mapped to the checkbox's `indeterminate`). In `documents.ts` the whole select column — and the per-row delete icon — is omitted when `canDelete` is `false`, so permission gating happens at column-assembly time:

```ts
const columns: ColumnDef<DocumentRow>[] = [
  ...(canDelete ? [selectColumn] : []),
  /* ...data columns... */
]
```

## Composables inventory

Composables live in `src/composables/`. They own reusable reactive behaviour and are imported directly (no barrel). The recurring families:

| Composable | Returns / shape | Purpose |
|---|---|---|
| `useDebounce.ts` | `useDebouncedRef(value, delay=300)`, `useDebounce(fn, delay)`, `useDebouncedEmit(emitFn, delay)` | Debounce a ref, a function, or a component `emit` |
| `useDebouncedProperty.ts` | `{ localValue, ... }` | Local typing state that syncs back to a prop after a delay (avoids cursor jumps on undo/redo) |
| `useConfirmedMutation.ts` | `{ showDialog, target, isPending, prompt, confirm, dismiss }` | Generic confirm-then-mutate flow over a TanStack mutation |
| `useCancelExecution.ts`, `useDeleteReport.ts` | composed `useConfirmedMutation` instances | Concrete destructive flows |
| `useCounterpartiesQueryState.ts`, `useDocumentsQueryState.ts` | `{ state, patchState }` | Sync filter/sort/page state to and from the route query |
| `useLiveDuration.ts` | `ComputedRef<string \| null>` | Ticking elapsed-time chip for in-flight runs; falls back to server `duration` |
| `refreshToken.ts` (`useRefreshToken`) | `{ setRefreshTokenTimer }` | Schedule a JWT refresh ~1 minute before expiry |
| `useCurrentUserRoles.ts` | `{ isSuperAdmin, isAdmin, isEditor, isOperator }`, and `useRoleCheck(roles)` | Reactive role reads off the JWT. `isEditor` is `WorkflowEditor` or `SuperAdmin`, `isOperator` is `Operator` or `SuperAdmin`, mirroring the backend composite gates. Each role check shares one `storage` listener, ref-counted, and re-reads the token when it changes |
| `useWorkflowTypeNames.ts` | `{ workflowTypeOptions, typeNames, withTypeName }` | The one place a reports-list row gets its type name. The list endpoint sends only `workflowTypeId`. The dashboard and the Reports page map each row through `withTypeName`, which falls back to the id |
| `useFitsBetween.ts` | `useFitsBetween(container, start, middle, end)` → read-only `Ref<boolean>`, and the pure `fitsBetween` | Whether a middle element, centred in its container, clears the elements at both ends by the container's column gap. One `ResizeObserver` watches all four. The header menu in `DefaultLayout` collapses into the navigation sidebar when it does not fit |
| `useBundleDownload.ts` | `{ download, isPending }` | One shape for an export the server names: run the export call, click the download, and toast the server's refusal. The document text download and the counterparty bundle export use it |
| `useDownloadExecutedWorkflow.ts` | download state and handler | The executed-workflow download behind `DownloadExecutedWorkflowButton` |
| `useReportDescription.ts` | a describer of a report row | The type, counterparty and date added that name a report's row controls (see [Row actions and selection](#row-actions-and-selection)) |
| `usePageImage.ts` | the page's image or, for a file type with none, its text | Loads a page's rendered image while `active` holds; shared by the page reader and `PagePreviewDialog` |
| `usePeerCandidates.ts` | the peer-candidate query | Searches the counterparties a run can name as peers ([API layer](api-layer.md#module-inventory)) |
| `useInlineCounterpartyCreation.ts` | `{ searchTerm, mergedOptions, visibleOptions, hiddenOptionCount, ... }` | Shared search/create logic for the counterparty selects; also caps rendered options (see [dropdown render cap](components-and-design-system.md#dropdown-option-list-render-cap)) |
| `useDocumentUpload.ts`, `useReferenceId.ts`, `useCurrentUserId.ts`, `useMyReviewCounts.ts`, `useFactTooltip.ts` | varies | Feature-specific helpers |



### `useDebounce` — three variants

`src/composables/useDebounce.ts` exports a value debouncer, a function debouncer, and a thin alias used to debounce component emits:

```ts
export function useDebouncedRef<T>(value: Ref<T>, delay = 300): Ref<T>
export function useDebounce<T extends (...a: any[]) => any>(fn: T, delay = 300): (...a: Parameters<T>) => void
export function useDebouncedEmit<T extends (...a: any[]) => any>(emitFn: T, delay = 300) // wraps useDebounce
```

### `useConfirmedMutation` — confirm then mutate

`src/composables/useConfirmedMutation.ts` factors out the "open a dialog, then on confirm run a destructive mutation and toast the result" flow shared by Cancel-execution and Delete-report. It does not call any query hooks itself — the caller passes in a TanStack mutation (`{ mutateAsync(id), isPending }`) plus i18n toast keys:

```ts
const { showDialog, target, isPending, prompt, confirm, dismiss } = useConfirmedMutation({
  mutation: API.WorkflowExecutions.useCancelWorkflowExecution(),
  successKey: '...', errorKey: '...',
  onSuccess: target => { /* ... */ },
})
```

`prompt(entity)` is a no-op while a mutation is already pending (re-entry guard), and `confirm()` always `dismiss()`es in a `finally`, so the dialog state can't leak across overlapping requests.


### `useXxxQueryState` — state synced to the route query

The `*QueryState` composables keep table filter/sort/pagination in the URL so a view is shareable and survives reload. They return a read-only `state` computed (parsed from `route.query`) and a `patchState` that serializes the merged state and `router.replace`s it — skipping the navigation when the serialized query is unchanged:

```ts
// src/composables/useCounterpartiesQueryState.ts
const state = computed(() => parseQueryState(route.query))
const patchState = async (partial) => {
  const next = serializeQueryState({ ...state.value, ...partial })
  if (shallowEqualRecord(next, serializeQueryState(state.value))) return
  await router.replace({ query: next })
}
```

### `useLiveDuration` — ticking elapsed time

`src/composables/useLiveDuration.ts` returns a formatted-duration computed. While the run is in an in-flight state (`Queued`, `Running`, `ProcessingDocuments`) and has a `startedAt`, it starts a 1-second interval and shows elapsed time; once terminal — or before `startedAt` exists — it falls back to the server-provided `duration`. The interval is cleared via `onScopeDispose`, and the computed returns `null` when there is nothing useful to show so the caller can omit the chip entirely.

## Types, enums & contracts

Shared types and enums are organized by responsibility, and most folders re-export through a barrel `index.ts`:

| Location | Holds | Examples |
|---|---|---|
| `src/types/` | UI/domain TypeScript types and interfaces | `OptionType` (`types/component`), `WorkflowFormData` / `DocumentInfo` (`types/workflow`), `workflow-builder.ts` |
| `src/enums/` | string enums + shared constants, re-exported from `enums/index.ts` | `WorkflowState`, `WorkflowOutputType`, `ReportParagraphKind`, `BackgroundJobState` (`enums/api.ts`); `WorkflowVariableTypes` (`enums/workflow.ts`) |
| `src/api/*/schema.ts` | per-resource request/response contracts | `CounterpartyDetails` (`api/counterparties/schema.ts`), `DocumentSummary` (`api/documents/schema.ts`) |
| `src/utils/` | pure helpers (no Vue reactivity) | `counterpartyName.ts`, `uuid.ts`, `placeholderValidation.ts` |

> [!WARNING]
> The block-type enum is **not** in `src/enums/`. `BlockType` is defined in the workflows API schema and must be imported from `@/api/workflows/schema`. The strongly-typed block hierarchy in `src/api/workflows/blockTypes.ts` imports it (as `BlockTypeEnum`) to build its generic helpers but does not re-export it. Import block types from the `api/workflows` layer, not from `@/enums`.

`WorkflowVariableTypes` mirrors the backend's variable kinds (`String`, `Number`, `DateTime`, `List`, `Dropdown`, `MultiSelect`); keep it aligned with the backend enum when either side changes. There is no enum of workflow types in the app: a workflow type arrives as a `workflowTypeId`, and `useWorkflowTypeNames` names it from the category summaries (`API.Workflows.useGetWorkflowTypeOptions`). The create-report wizard renders a `DateTime` variable as a date-only input and sends `yyyy-MM-dd`, the one format a Date variable carries on every surface; `toDateVariableValue` (`src/lib/dateVariable.ts`) cuts a stored default or a re-run value that still carries a time part down to its calendar day without passing it through a time zone. An optional `Number` variable with no default starts empty, and a cleared number input counts as empty rather than failing validation; a required one shows **Required** when left blank. On submit, `submittableVariables` (`src/lib/workflowVariableValues.ts`) leaves a blank optional variable out of the request, because the API refuses an empty value for a `Number` or `DateTime`, and a left-out variable runs with its template default, or with "No data available" when it has none. The one exception is a blank optional `String` or `Dropdown` that has a default: it is sent empty, so it still overrides the default. When a left-out field has a default, the review step shows that value marked "(template default)".

The wizard's Details step checks each variable's input rules with `variableInputRuleCheck.ts` (`src/lib/inputRules/`), a port of the server's `VariableInputRuleCheck` that writes the same messages; the rules themselves are on [Input Rules](../workflow-engine/variable-system.md#input-rules). It reads the values `submittableVariables` would send, falls back to a left-out field's default as the server does, and takes the attached classes from the Documents step. A broken rule shows its message on the field and keeps **Continue** disabled; the field's default is never cleared or changed. A stale rule is skipped without a message. `variableInputRuleCheck.test.ts` runs the shared `variable-input-rule-cases.json`, the same file the platform's tests run.

> [!NOTE]
> Pure data transforms belong in `src/utils/` (testable in isolation, no Vue import), reactive behaviour belongs in `src/composables/`, and per-resource request/response shapes belong next to their API service in `src/api/<resource>/schema.ts`. Following this split keeps the form schemas, table factories, and composables on this page small and reusable.
