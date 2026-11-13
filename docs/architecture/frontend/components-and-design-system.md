---
type: explanation
status: current
description: "Documents the Atomic Design component layering, the reka-ui/shadcn pattern and its keyboard behaviour, Tailwind v4 and the design tokens, status colours and pills, the workflow-state mapping, the execution log page, the report's document panel, icons, the theme and the brand marks. Read when you add or restyle a component, pick a colour token, change the header, the theme, the execution log or the report panel, or need to know where a component belongs."
---

# Components & Design System

**On this page:** Atomic Design layering · reka-ui + shadcn-vue pattern · Tailwind CSS v4 · Design tokens · Semantic status colour tokens · Workflow state: single source of truth · The execution log page · The report's document panel · Icons · Theme (light / dark) · Brand marks

The IniaAi.Web frontend organises its UI into an Atomic Design hierarchy layered on top of [reka-ui](https://reka-ui.com/) primitives, styled with Tailwind CSS v4 and a CSS-custom-property design-token system. This page is the maintainer's map of *where a component belongs*, *how variants and tokens are defined*, and *which abstractions are the single source of truth* for recurring concerns like workflow-state colour and icons. It is a sibling of the [frontend overview](index.md) — see also [forms, tables & composables](forms-tables-and-composables.md) for the data-entry layer.

## Atomic Design layering

Components live under `src/components/`, split by composition level. The split is a convention, not a build boundary — but it drives one real behaviour: **atoms and organisms are auto-registered as global components, molecules are not** (see [Global registration](#global-registration-registerts)).

| Layer | Path | What belongs here | Examples |
|---|---|---|---|
| **atoms** | `src/components/atoms/` | Indivisible building blocks, no business logic | `IniaIcon`, `IniaLoader`, `IniaWordmark`, `HighlightedText`, `LiveDuration`, `ThemeToggle`, `MarkdownText` |
| **molecules** | `src/components/molecules/` | Small compositions of atoms / ui primitives | `WorkflowStatus`, `WorkflowStatusPill`, `ReplayProvenanceBadge`, `MultiSelect`, `IniaSelect`, `SearchableSelect`, `DateRangeFilter`, `MonthRangeFilter` |
| **organisms** | `src/components/organisms/` | Complex, feature-bearing compositions | `DataTable`, `LoginForm`, `SourceChainPanel`, workflow elements |
| **ui** | `src/components/ui/` | shadcn-vue-style wrappers over reka-ui primitives | `Button`, `Badge`, `Dialog`, `DropdownMenu`, `Tooltip`, `Tabs` |
| **layouts** | `src/components/layouts/` | Page chrome / shell | `DefaultLayout`, `BlankLayout` |

> [!TIP]
> **Placement rule of thumb**
>
> If it has no children of its own, it is an atom. If it composes atoms/ui into a reusable unit, it is a molecule. If it owns a feature or talks to the API layer, it is an organism. Anything that is a thin styled wrapper around a reka-ui primitive lives in `ui/`.

Alongside the atomic-design layers, one folder groups by feature rather than by composition level: `src/components/signatures/` holds signature components (e.g. `SignatureTestDialog`). It is not auto-registered, so its components are imported by name.


### Global registration (`register.ts`)

`src/register.ts` is invoked once at app bootstrap and does three things:

1. Adds every icon exported from `src/icons.ts` to the Font Awesome `library`.
2. Registers `FontAwesomeIcon` globally as `<font-awesome-icon>`.
3. **Auto-registers all Atoms and all Organisms as global components** by iterating the barrel exports of `@/components/atoms` and `@/components/organisms`.

```ts
import * as Atoms from '@/components/atoms'
import * as Organisms from '@/components/organisms'

export default function (app: App) {
  // ... library.add(...) for each icon, app.component('font-awesome-icon', ...)

  Object.keys(Atoms as Record<string, Component>).forEach(x => {
    app.component(x, (Atoms as Record<string, Component>)[x])
  })
  Object.keys(Organisms as Record<string, Component>).forEach(x => {
    app.component(x, (Organisms as Record<string, Component>)[x])
  })
}
```

> [!WARNING]
> **Molecules are NOT auto-registered**
>
> `register.ts` only walks `atoms` and `organisms`. Molecules (and `ui/` primitives) must be **imported by name** wherever they are used, e.g. `import WorkflowStatus from '@/components/molecules/WorkflowStatus/WorkflowStatus.vue'`. Adding a molecule and expecting it to resolve as a global tag will silently fail.

## reka-ui + shadcn-vue pattern

The `ui/` layer follows the shadcn-vue convention: each primitive directory has an `index.ts` that exports the `.vue` wrapper plus a **`class-variance-authority` (cva)** variant definition, and the `.vue` file forwards props/emits to the underlying reka-ui primitive.

A few wrappers add keyboard behaviour that every consumer inherits:

- `DropdownMenuContent` closes on Tab and returns focus to its trigger: reka's modal menus otherwise trap Tab.
- `PopoverContent` closes on Escape and keeps that key from the layers beneath it. A `Command` list inside a popover is reka's topmost dismissable layer and would otherwise take Escape for itself, and a dialog under the popover would otherwise close on the same key.
- `PopoverContent` renders in place, not teleported to the body, when it sits inside a dialog (`portalDisabled` overrides this): the dialog's focus trap treats teleported content as outside and pulls focus back, so a search field in a teleported popover could never be typed in. When the content has no control of its own, it focuses the content's `role="dialog"` element, so focus does not stay behind on the trigger.
- `AccordionTrigger` takes an `ariaLabel` prop for the trigger button, because its outer element is the accordion header, and a required `headingLevel` (2 to 6) that renders that header as `h2` to `h6`.
- `DialogContent`'s close button is a 32px target, left out of the dialog's button styling by its `dialog-close` class. The dialog paints every other button inside it in one style, so a dialog whose buttons need their own variants, such as amount shortcuts or a text link, sets `ownButtonStyles`. A dialog that can be taller than a phone's screen adds `max-h-[calc(100dvh-2rem)] overflow-y-auto` to its own class, as the top-up dialog does; it is not the default, because a dropdown that opens inside a dialog without a portal would be cut off at the dialog's edge.
- `Button` keeps its label on one line. Give it `wrap` when the label can be longer than a phone's screen, such as "Top up $750 to run this report": the label then wraps and the button grows taller.
- `Input` uses 16px text on narrow screens and 14px from the `md` breakpoint, so a phone does not zoom into the field.

`DefaultLayout` (`src/components/layouts/DefaultLayout/DefaultLayout.vue`) lays the header out as three grid columns: the menu buttons and the logo on the left, the menu centred in the middle, and the organisation picker, theme, log and account controls on the right. The menu shows only while it fits centred between the two sides with the column gap to spare on each, whatever the window width; `useFitsBetween` (`src/composables/useFitsBetween.ts`) measures that with a `ResizeObserver`, so the role's menu items, a module's menus and a review badge all count. When the menu does not fit it collapses: it stays measurable but is hidden and `inert`. One bars button at the left opens the tools drawer beside the full menu (**Open tools menu**) and the navigation sidebar in its place (**Open navigation menu**), so focus always has the same button to come back to. The superadmin's menu fits from about 1,390 px wide.

The layout holds two slide-in panels: the **Tools** drawer, opened beside the full menu, and the navigation sidebar, which lists every menu item and every tool and is opened when the menu collapses. Each is a modal dialog (`role="dialog"`, `aria-modal="true"`, named **Tools** or **Navigation**) that is `inert` while closed, so its off-screen controls take no Tab stop. Opening one makes the page behind it `inert` and puts focus on the panel's first control. However it closes (Escape, the backdrop, a link in it, or the drawer's **Close tools menu** button), focus returns to the button that opened it, through `focusFirstConnected` from `src/utils/focus.ts`. Both panels render at every width with their own close controls, so a panel that is open when the menu collapses or comes back stays open, and closing it hands focus back to the bars button.

Two molecules carry patterns worth reusing. `SidePanelToggle` is the collapse and expand control of a page's side panel: named by its action, with `aria-expanded`, `aria-controls` while the panel is open (the panel is not rendered while it is closed), and its glyph hidden from assistive technology. A page renders one toggle inside the open panel and another in its place when the panel is collapsed; the toggle a press brings in takes focus, so a keyboard user can collapse and expand the panel without losing their place. A row whose main area opens something is a native button with the `.row-main-button` class (`src/assets/main.css`): its stretched `::after` keeps the whole row clickable and draws the focus ring around the row, and controls beside it take `.row-main-button-raised` to sit above that overlay. The report's document rows use it, and so do the paid build's Regression health rows.

### Variant definitions with `cva`

Variants are declared in `src/components/ui/<name>/index.ts`. `Button` defines `variant`, `size`, and `fontSize` axes:

```ts
export const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 ... disabled:opacity-50',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground shadow-sm hover:bg-primary/90',
        destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
        destructiveSoft: 'text-error bg-transparent hover:bg-destructive/10 ...',
        outline: 'border border-input bg-background ...',
        secondary: 'bg-secondary text-secondary-foreground ...',
        ghost: 'hover:bg-accent hover:text-accent-foreground',
        link: 'text-primary underline-offset-4 hover:underline',
        soft: 'text-primary bg-transparent hover:bg-primary/20',
      },
      size: { default: 'h-9 px-4 py-2', sm: 'h-8 ...', lg: 'h-10 ...', icon: 'h-9 w-9' },
      fontSize: { default: 'text-sm', sm: 'text-sm', lg: 'text-lg' },
    },
    defaultVariants: { variant: 'default', size: 'default', fontSize: 'default' },
  },
)
export type ButtonVariants = VariantProps<typeof buttonVariants>
```

`Badge` (`src/components/ui/badge/index.ts`) follows the same shape with a single `variant` axis: `default`, `secondary`, `destructive`, `outline`, and `gradient` (which maps to the global `.badge-gradient` class rather than utilities).

### `cn()` — class merge helper

`src/lib/utils.ts` exports `cn()`, used everywhere a wrapper merges its base variant classes with a caller-supplied `class`. It runs `clsx` first (to resolve conditionals/arrays) then `tailwind-merge` (to dedupe conflicting Tailwind utilities so the last one wins):

```ts
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
```

### Primitive forwarding

Wrappers render reka-ui's `Primitive` (or the specific primitive) and pass through props. Simple wrappers like `Button` use `Primitive` with `as`/`as-child`:

```vue
<Primitive :as="as" :as-child="asChild"
  :type="as === 'button' && !asChild ? type : undefined"
  :disabled="isLoading"
  :class="cn(buttonVariants({ variant, size, fontSize }), 'aria-disabled:pointer-events-none aria-disabled:opacity-50', props.class)">
  <IniaLoader v-if="isLoading" class="w-4" />
  <slot />
</Primitive>
```

`Button` renders `type="button"` by default when it renders a native button. A button that submits a form must say `type="submit"`. The browser treats a button with no type as a submit button, so Enter in a field would activate the first of them, for example a Cancel button. With `as-child`, or with an `as` other than `button`, `Button` sets no type.

A `Button` with `aria-disabled="true"` looks and acts disabled to the pointer, as a natively `disabled` one does, but it keeps keyboard focus. Use it for a control that a keypress can disable while it holds focus, such as the page reader's **Next page** at the last page; the click handler must still refuse the action. The `aria-disabled` look lives in `Button.vue` and not in `buttonVariants`, because the calendar parts reuse `buttonVariants` and reka sets `aria-disabled` on unavailable dates.

Compound primitives that emit events use `useForwardPropsEmits` to delegate both props and emits while stripping the locally-handled `class`. From `DropdownMenuContent.vue`:

```ts
const props = withDefaults(defineProps<DropdownMenuContentProps & { class?: HTMLAttributes['class'] }>(), { sideOffset: 4 })
const emits = defineEmits<DropdownMenuContentEmits>()
const delegatedProps = computed(() => { const { class: _, ...delegated } = props; return delegated })
const forwarded = useForwardPropsEmits(delegatedProps, emits)
```

### Dropdown option-list render cap

The searchable dropdown molecules (`MultiSelect`, `PeerCompanyMultiSelect`, `CounterpartySearchableSelect`) never mount their full option list. Mounting one reka-ui `ComboboxItem` per option is O(N) with a large constant — with thousands of counterparties a dropdown took multiple seconds to open. Instead they:

- pass `ignore-filter` to `Command` (reka-ui's `ComboboxRoot`) and filter client-side themselves (case-insensitive substring over `searchText || label`),
- render only the first `DROPDOWN_RENDER_CAP` (100, `src/constants/dropdown.ts`) matches,
- show a muted "{n} more — keep typing to narrow down" row (`shared.components.multiSelect.moreResults`) when the match set exceeds the cap.

Selected-value labels (trigger badges, selected label) always resolve against the **full** option list, so selections beyond the rendered window display correctly. `CommandEmpty` still works under `ignore-filter` — reka-ui shows it when zero items are mounted. For the two counterparty selects the filter/cap logic lives in `useInlineCounterpartyCreation`; `MultiSelect` implements it locally. In those two selects, `InlineCounterpartyCreation.vue` renders the "Create" option as a `CommandItem`, which counts as a mounted item, so `CommandEmpty` never shows while it is present; the component renders the no-match message itself, and keeps the "Add new counterparty" hint outside the listbox.

### A select names itself through `aria-label`

`MultiSelect`, `PeerCompanyMultiSelect` and `IniaSelect` declare `inheritAttrs: false` and pass the caller's `aria-*`, `id` and `data-*` attributes to the trigger (`splitTriggerAttrs`, `src/lib/triggerAttrs.ts`), so a `FormControl` or a `label for` reaches the trigger. The other attributes stay off the trigger, among them the blur and change listeners a form binding hands over, because the trigger blurs as soon as its list opens. Pass the caption a filter toolbar prints above the control as `aria-label` (`:aria-label="$t('page.documents.filters.category')"`), because that caption is a sibling element no browser ties to the control. For a toolbar with no caption, pass the words the placeholder option shows. Every filter on the analyst toolbars passes one, so a screen reader or a role-and-name test finds "Report type" by name.

### A filter says what it is set to when nothing is chosen

`IniaSelect` shows its `placeholder` until a value is chosen. `MultiSelect` takes a `placeholder` prop too and shows it in the trigger only while nothing is chosen; a chosen value replaces it, and clearing the selection brings it back. A chosen value that matches no loaded option, such as an id from the address while the options still load or one whose record is gone, shows the "{n} selected" count instead, so the trigger never reads "All" over a filtered list. The filters on the Reports list (Report type, Company, User) and on Company Management (Type) pass "All", so no filter box on those pages is blank: the filters on Documents read "All". A `MultiSelect` in a form passes none and keeps an empty trigger. `PeerCompanyMultiSelect` reads "No peers chosen" unless it is given a `placeholder`.

Nothing inside a `MultiSelect` trigger takes focus: the trigger is the one control. Once anything is chosen, a last option in the list, "Clear {n} selections", clears every choice from the keyboard; the X on a badge, or on the "{n} selected" badge, does the same for the mouse.

The Created and Reporting range filters (`RangeFilterPopover`) are named by what they filter followed by the label they show, "Filter by created date All Created Dates", through `aria-labelledby` over a visually hidden purpose and the visible label, so the visible words are part of the name.

## Tailwind CSS v4

Styling uses **Tailwind v4** wired through the Vite plugin — there is **no `tailwind.config.js`**. Configuration that would have lived in a config file lives in CSS instead.

- `vite.config.ts` registers `@tailwindcss/vite` (`tailwindcss()` in the plugins array).
- `src/assets/main.css` starts with `@import 'tailwindcss';` — the single entry point.
- The dark variant is declared in CSS: `@custom-variant dark (&:where(.dark, .dark *));`.
- shadcn colour mappings are exposed to utilities via an `@theme inline { ... }` block (e.g. `--color-primary: var(--primary)`), so `bg-primary`, `text-success`, etc. resolve to the design tokens below.
- Dynamic grid classes that aren't present in source at build time are safelisted via `@source inline("...grid-cols-/col-span-...")`.

> [!WARNING]
> **Scoped styles silently beat utility classes**
>
> Tailwind v4 emits utilities inside a cascade layer, while Vue `<style scoped>` rules are unlayered — and unlayered CSS wins over layered CSS regardless of order. A scoped rule that sets a property on an element overrides any Tailwind utility bound in that element's template, with no warning (this is how the mobile sidebar's `translate-x-0` open state broke after the v4 migration). When a scoped rule touches a property, drive its state with a scoped modifier class, not a utility.

> [!NOTE]
> **Versions (from `package.json`)**
>
> `tailwindcss` and `@tailwindcss/vite` `^4.3.0`, `reka-ui` `^2.10.5`, `class-variance-authority` `^0.7.1`, `tailwind-merge` `^3.6.0`, `@fortawesome/fontawesome-svg-core` `^7.2.0`, Vue `^3.5.34`.

## Design tokens

All tokens are CSS custom properties defined twice in `src/assets/main.css`: once on `:root` (light) and once on `.dark` (dark). Components use these tokens and do not hard-code hex values, so the two themes stay consistent. The comments beside each token in `main.css` give its contrast ratios.

| Token group | Properties | Light → Dark |
|---|---|---|
| Page grounds | `--background`, `--card`, `--muted` | `#f6f2ec / #ffffff / #ebe7e0` → `#1a3850 / #244c6d / #112434` |
| Layer backgrounds | `--bg-0`, `--bg-1`, `--bg-2` | `#f6f2ec / #ffffff / #ebe7e0` → `#112434 / #1a3850 / #244c6d` |
| Strokes | `--stroke`, `--stroke-hover`, `--divider` | translucent slate and `#a5b4c0` → translucent navy and `#43627c` |
| Text | `--text-primary`, `--text-secondary`, `--text-muted` (`--text-tertiary` is an alias of it) | `#1a3850 / #47657d / #546878` → `#f6f2ec / #b4c3cf / #b0bdc8` |
| Live agent mark | `--presence` | `#1565b8` → `#6ea8dc` |
| Brand pair | `--accent-indigo-from`, `--accent-indigo-to` | `#1a3850 / #015294` → `#c0d5ea / #f6f2ec` |
| Radii | `--radius-panel` 14px, `--radius-card` 12px, `--radius-button` 10px, `--radius-small` 8px | same both modes |
| Spacing (8px scale) | `--spacing-xs` 8 / `-sm` 16 / `-md` 24 / `-lg` 32 / `-xl` 48 px | same both modes |
| Shadows | `--shadow-panel`, `--shadow-card`, `--shadow-hover` | lighter alpha → deeper alpha |

shadcn-style tokens (`--background`, `--foreground`, `--primary`, `--muted`, `--destructive`, `--radius` and the rest) live in the same blocks and feed the `@theme inline` mapping. `--destructive` is the brand rust fill, `#be5014` in both themes. It is a fill colour, not a text colour: white on it clears AA, but as text it measures 4.33 on the light page ground and 2.52 on the dark one. Text that reports an error uses `text-error` (`--accent-error`).

`--text-tertiary` is an alias of `--text-muted` in both themes, because an ink visibly lighter than `--text-muted` falls under 4.5:1 on stone. `--text-muted` measures 4.70 on stone, 5.19 on sand and 5.79 on white in the light theme, and 4.70 on a card, 6.35 on the ground and 8.27 sunken in the dark one. There is no `--text-quaternary`. `--presence` is the live-agent accent of the agent presence line, the activity line and the agent panel. It colours dots and carets only, so its bar is 3:1; on a dark card it measures 3.56.

## Semantic status colour tokens

For anything where colour signals the **state of a thing** (success / error / warning / in-flight / queued), use the semantic status tokens, not raw `text-green-*` / `text-red-*` / `text-yellow-*` / `text-blue-*` utilities.

| Token | Ink, light → dark | Use for |
|---|---|---|
| `text-success` / `--accent-success` | `#0b6243` → `#b0ecd7` | Complete / positive / passing |
| `text-warning` / `--accent-warning` | `#7b4b0e` → `#fabc6f` | Attention / in-flight / partial success |
| `text-error` / `--accent-error` | `#903d10` → `#f3c1a6` | Failed / destructive, form errors included |
| `text-info` / `--accent-info` | `#105993` → `#c0d5ea` | Queued / informational |
| `text-muted-foreground` | `#47657d` → `#b4c3cf` | Secondary text, cancelled, dismissed |

Each `--accent-*` status token has three siblings. `--accent-<name>-light` is a solid chip ground, so a chip keeps its contrast whatever lies behind it. `--accent-<name>-border` is the chip or panel border. `--accent-<name>-fill` is the solid brand fill for buttons and badges. The ink clears 4.5:1 on its own chip ground and on every page ground, in both themes.

### The contrast contract

Text colour comes from a token that a test has checked. `src/test/helpers/themeColors.ts` lists them:

- **Page inks** clear 4.5:1 on `--background`, `--card` and `--muted` in both themes: the foregrounds, `--primary`, the `--text-*` tokens, the brand pair and the four status inks.
- **Paired inks** clear 4.5:1 on the ground they are made for, for example `--primary-foreground` on `--primary`, `--nav-ink` on `--nav-bg`, or a status ink on its chip ground.
- **Graphic tokens** (`--presence`) clear 3:1, for marks that are not text.

`src/assets/themeContrast.test.ts` checks the lists. `src/textContrast.test.ts` checks every use:

- A component that sets a raw colour must set the background beside it, in the same class string or CSS rule, so that the test can measure the pair. If the background is not beside it, the raw colour must clear every page ground in both themes. The execution log's block-type badges keep their Tailwind hues this way: each badge sets its own background, and its text is white or black, whichever clears AA on it.
- A colour is never dimmed with `opacity`, an `opacity-*` utility or a `/NN` alpha. Use a quieter token instead, such as `text-muted-foreground` for a select's chevron.


> [!WARNING]
> **Don't hand-roll status colour**
>
> `src/textContrast.test.ts` fails on a raw colour that falls under AA, on a token outside the checked lists, and on text dimmed below AA by `opacity`, an `opacity-*` utility or an alpha. Reach for `text-success` / `text-error` / `text-warning` / `text-info` (and their `bg-`/`border-` forms) when the colour means something.


## Workflow state: single source of truth

The per-state **icon + colour + tooltip** mapping for workflow executions lives in exactly one place: `src/components/molecules/WorkflowStatus/WorkflowStatus.vue`. Do not re-derive it inline anywhere.

`WorkflowStatus` is icon-only (built for dense table cells), accessible (`role="img"` + `aria-label`, focusable trigger), and pulls its tooltip from the i18n namespace `shared.enums.workflowStateTooltip.<state>` (rendering no tooltip when the key is missing). The mapping, keyed off the `WorkflowState` enum:

| State | Indicator | Colour token |
|---|---|---|
| `Queued` | `clock` icon | `text-info` |
| `Running`, `ProcessingDocuments` | spinner (`IniaLoader`) | `text-warning` |
| `Draft`, `Confirmed` | `circle-check` icon | `text-success` |
| `Failed` | `circle-exclamation` icon | `text-error` |
| `Cancelled` | `ban` icon | `text-muted-foreground` |

`WorkflowStatusPill` (`src/components/molecules/WorkflowStatusPill/WorkflowStatusPill.vue`) wraps `WorkflowStatus` to add a text label (from `shared.enums.workflowState.<state>`) inside a rounded chip — use it when you want icon + label, and `WorkflowStatus` when you want icon-only. Both inherit the mapping from `WorkflowStatus.vue`, so adding or recolouring a state is a one-file change.

`ReplayProvenanceBadge` (`src/components/molecules/ReplayProvenanceBadge/ReplayProvenanceBadge.vue`) sits beside the status wherever executions are listed — the reports table's Status cell and the report header — and renders only for a **frozen execution**: one whose `freezeSourceExecutionId` is set because it replayed another execution's external fetches ([freeze option](../../reference/api/workflow-executions.md#freezing-external-fetches)). It is an outline `Badge` reading "Replayed" with the `clock-rotate-left` glyph; its tooltip names the source and the coverage (`freezeReplayedCount` of `freezeEligibleCount`, `shared.replayProvenance.tooltip`), or only the source while the frozen run is still in flight (`tooltipPending`, keyed on the execution's `workflowState`), or the source with a note that coverage was not recorded when the run is no longer in flight and carries no counts (`tooltipUnrecorded`), and says plainly that this is a test artifact, not a normal report. A normal execution renders nothing; the reports table skips the vnode entirely for rows without a `freezeSourceExecutionId`.

## The execution log page

`WorkflowExecutionLogs` (`src/views/WorkflowExecutionLogs.vue`) shows **every** log entry the run produced, skipped, running and not-executed blocks included. It carries no visibility filter, and its **Failed Blocks** panel jumps to a failed block with `scrollToFailed`.

`WorkflowExecutionLogItem` gives each status a chip: `Running` takes `var(--accent-warning-fill)` with a spinner, `NotExecuted` a neutral bordered chip. Note the token — `--accent-warning` is ink (`#7b4b0e` in light mode), and `--accent-warning-fill` is the amber fill; a chip background wants the fill.

The header's right side holds `DownloadExecutedWorkflowButton` (`variant="default"`). The molecule self-gates on `isOperator`, so the page writes no gate of its own. Under the header, pills show the run's counterparty, its start time (`formatDateTime`) once it has one, and its live duration.

The header's left side says which run the log belongs to. A **Back to report** link opens the report's page, `/workflow-reports/:id/edit`, through `reportRoute` in `src/lib/routes.ts`. It is a link to a fixed route, not `router.back()`, so it lands on the report however the log was opened: from the report's **Actions** menu, from another page, or by its address. Under the **Execution Logs** heading the page shows the report's name, from `reportTitle` in `src/lib/reportTitle.ts`: the report's title, else its workflow type name. That is the name the report's own page shows in its heading.

The browser tab reads `<report name> · Execution Logs · Inia AI`, or `Execution Logs · Inia AI` until the report loads: the view gives the report's name to `useItemTitle` ([Browser tab title](state-routing-and-auth.md#browser-tab-title)).

Each block card's **Show Prompt**, **Show Output** and **Show Full Values** / **Show Truncated Values** toggle carries an `aria-label` that adds the block's name, such as `Show Output for Balance Sheet Extraction` (`page.workflowExecutionLogs.toggles.forBlock`), and the section the entry ran in when it has one, such as `Show Output for Bank statement prep in Account 2` (`toggles.forBlockInSection`). A screen reader that lists the page's buttons can then tell one block's toggles from another's, and one block run once per section from its other runs. The **Log navigation** buttons add the section the same way, for screen readers only (`page.workflowExecutionLogs.inSection`). While the full values load, that last toggle is disabled and named only `Loading...`. Each name starts with the words on screen, the practice WCAG 2.5.3 (Label in Name) recommends, and a block with a blank name keeps the plain label. Two log entries of one block in one section, such as a block that ran twice there, still share a name, as their cards show the same block name.

A block can offer **Show Prompt** and still have no prompt to show. The log list's `hasPrompt` is true when the AI query block has a `UserPromptTemplate`, but the log detail endpoint returns the block's `UserPromptTemplatePreview`, which many runs leave empty. The toggle therefore stays: the page cannot know the prompt is empty until it asks. When the prompt comes back empty, or holds only whitespace, the opened area says `No prompt preview was recorded for this block.` (`page.workflowExecutionLogs.item.noPromptPreview`) instead of showing an empty box. `loadLogDetail` stores a fetched empty prompt as `''` rather than `null`, so closing and reopening the toggle does not ask the platform again; `null` keeps meaning "not loaded yet".

**Show Output** has the same gap: the log list's `hasOutput` is true whenever the block's `BlockResponse.RawResponse` is not `null`, and an empty string counts. An empty or whitespace-only output opens to `No output was recorded for this block.` (`page.workflowExecutionLogs.item.noOutput`). What counts as present is unchanged, for the same reason as the prompt; an empty output already arrives as `''`, so reopening the toggle does not ask again.

The **Failed Blocks** panel takes its colours from the theme tokens alone (`--text-primary` on `--accent-error-light`), with `.dark` overrides only for its gradient and its hover fill. It keys nothing on `@media (prefers-color-scheme: …)`, which follows the operating system rather than the app's `.dark` class.

## The report's document panel

`WorkflowCredits` (`src/components/molecules/WorkflowCredits/WorkflowCredits.vue`) renders a report's counterparty, analyst, **Created** and duration rows, then up to three document sections, each shown only when it has a document: **Documents not used**, the execution's `excludedDocuments` (the documents the caller sent that a stage took out of the run, see [Reports API](../../reference/api/reports.md)), each with its reason, such as "Could not be read"; then the run's own documents, split into **Source Documents** and **Generated Documents** on `isOutputDocument`.

The panel sets no width of its own; the report header (`WorkflowDetails.vue`) sizes it. The header is one wrapping row at every width: the panel, at most 37.5rem wide, sits beside the report's title, and wraps below it when the title would get less than 24rem beside it; on a narrow screen it shrinks to the width there is. The title row wraps too, so the status pill and the **Actions** menu move under the title rather than narrowing it to one word per line, and a single long word in the title breaks rather than pushing the page sideways.

The Created row carries the run's `createdAt` through `formatDateTime` (`dd/MM/yyyy HH:mm`), the same local-time conversion the reports table's **Date Added** column uses, so a report cannot read one time on the table and another on its own page.

Each section header is an `h2`: the panel passes `:heading-level="2"` to the ui `AccordionTrigger`, which renders reka's `AccordionHeader` at that level. The prop is required, because only the caller knows where the accordion sits in the page outline. On the report page the panel sits under the report's `h1`, and the section of the report body that is open is an `h2` too, so no heading level is skipped. A document's info mark and its download button each take the shared `.hit-target` class, a box of at least 24px around the icon.

The document sections are `AccordionItem`s inside one `type="multiple"` accordion, so each opens without closing the others. **Documents not used** and **Generated Documents** arrive open and **Source Documents** arrives collapsed (`:default-value="['excluded', 'generated']"`): the inputs are reference material, the outputs are what the reader came for, and on a run with four attachments and a short body the input list is otherwise most of the page. Both headers keep their count visible in either state — a collapsed section that does not say how much it hides is the failure mode this pattern is prone to. The Settings section below them keeps its own single accordion and its own closed-by-default state.

A document carries `shouldBeDisplayedInOutput`, the flag a submitter sets per uploaded file. The flag decides visibility on one side of the split only:

- A **hidden input** is listed, its name stepped down to the secondary ink, with a "hidden from output" note beside it, and it counts in the section count. The panel is the record of what a run consumed, so an attachment it consumed is named even when the report body does not show it. An input with no document class is listed too: the recipe editor lets a document class stay empty, so an unclassified upload is still an analyst's attachment.
- A **hidden generated document** is dropped. This is what keeps a block's scratch output from reading as a source: `ExaSearchStrategy` writes its search results as a document with `ShouldBeDisplayedInOutput = false`, and it is `isOutputDocument` — not the flag, and not the presence of a class — that keeps it out. Exa always sets a class on what it writes (`CreateDocumentFromExaResults` is only called when the block declares one), so a class check would not exclude it; what excludes it is the class being declared an output class.


The name is stepped down to `--text-secondary` rather than dimmed with `opacity`. An opacity multiplier washed it below AA on the light theme, where the ink starts dark on white and has no headroom to give away.

`fullFileName` (`src/lib/fileName.ts`) appends the stored extension to the stored name unless the name already ends in it, mirroring the server's own `Document.FileName` (`Name.TrimEnd().WithExtension(...)`). `FileMetadata.Name` is meant to hold the stem and `Extension` the rest, but some write path stores the whole file name in `Name` — about 6% of stored files — so every consumer that concatenates the two doubles the extension. The frontend helper is shared by the report panel, the report's download handler and the create-workflow uploader list, so what a row displays and what the saved file is called cannot drift apart. The server composes the name the same way for the file download (`GetFileQueryHandler`, through `WithExtension`) and for the document name of a citation (`Document.FileName`). One server path still concatenates `Name` and `Extension` and so doubles the extension of such a file: the report list the editor agent reads (`EditorAgentReports`). The write path that stores a full name in `Name` is the actual defect.


## Icons

Icons are Font Awesome solid glyphs, rendered through the `IniaIcon` atom (`src/components/atoms/IniaIcon/IniaIcon.vue`). It is a thin wrapper that builds the Font Awesome class string from `type` (default `solid`) and `name`:

```vue
<font-awesome-icon :class="classes" :icon="`fa-${type} fa-${name}`" />
```

Only icons explicitly imported in `src/icons.ts` are bundled — this keeps the icon payload to what's actually used. Adding an icon takes **two manual edits**; the registration in the middle is automatic:

1. **Import + re-export** the icon in `src/icons.ts` from `@fortawesome/free-solid-svg-icons` (camelCase, e.g. `faBrain`). Some are re-aliased, e.g. `faArrowTrendUp as faTrendingUp`. *(manual)*
2. `src/register.ts` iterates every export of `icons.ts` and calls `library.add(...)`, so the new icon registers automatically — no further wiring. *(automatic — nothing to do)*
3. Reference it by **kebab-case** name in markup: `<IniaIcon name="brain" />`. *(manual)*

The camelCase → kebab-case naming is implicit and handled by Font Awesome, not by our code. `IniaIcon.vue` builds `fa-${type} fa-${name}` from its props, so `<IniaIcon name="brain" />` produces the class string `fa-solid fa-brain`. The export you added (`faBrain`) and the `name` you reference (`brain`) are the same glyph — Font Awesome maps its internal `faBrain` definition to the `fa-brain` class. So drop the `fa` prefix, lowercase the first letter, and hyphenate before each remaining capital (`faCircleCheck` → `name="circle-check"`).

> [!TIP]
> Forgetting step 1 is the usual failure: an `<IniaIcon name="...">` for an unimported glyph renders nothing (or a missing-icon box) rather than erroring. If an icon doesn't show, check that it is exported from `src/icons.ts`.

## Theme (light / dark)

Theme state lives in the Pinia store `src/store/theme.ts`. It initialises from `localStorage['theme']` (falling back to `'dark'`), applies the theme by toggling the `.dark` class on `document.documentElement`, and persists every change back to `localStorage` via a `watch`. It exposes `theme`, `toggleTheme()`, and `setTheme()`.

The user-facing control is the `ThemeToggle` atom (`src/components/atoms/ThemeToggle/ThemeToggle.vue`), mounted in `DefaultLayout` (`src/components/layouts/DefaultLayout/DefaultLayout.vue`). It reads `themeStore.theme` and calls `toggleTheme()`.

The nav is the one surface that **counter-inverts** with the theme: the `--nav-bg` / `--nav-ink` / `--nav-hover` / `--nav-stroke` family in `src/assets/main.css` paints a navy strip over the light page and a cream strip over the dark page, so the header always contrasts with the ground. Anything rendered inside the header or the navigation sidebar must colour itself from the `--nav-*` tokens — the page-level `--text-*` / `--bg-*` tokens agree with the page, not the strip, and vanish against it (the one deliberate exception is the tenant selector and ThemeToggle, which render as white cards with navy ink on the strip in both themes).

> [!WARNING]
> **Every load starts dark**
>
> `src/App.vue` sets VueUse's colour mode to dark on every boot, which puts the `.dark` class on `<html>` before the first screen renders:
>
> ```ts
> const mode = useColorMode()
> mode.value = 'dark'
> ```
>
> `useColorMode` keeps its own value under `localStorage['vueuse-color-scheme']` and never reads `theme`. The theme store is created only when its first user mounts, `ThemeToggle` in the header of `DefaultLayout`, and it applies the saved theme then. So a saved light theme shows once a page in the default layout renders, after the first paint in dark. A blank-layout page opened first, such as sign-in, forgot password, password reset, two-factor verification or the Health screen, mounts no toggle and stays dark.

## Brand marks

The brand is the INIA.AI wordmark from the 2026 material: navy `#1A3850` letters and a copper `#AD8977` dot.

The `IniaWordmark` atom (`src/components/atoms/IniaWordmark/IniaWordmark.vue`) draws the wordmark as inline SVG. The letters use `currentColor` and the dot is always copper, so the wordmark takes the text colour around it: `--nav-ink` in the header and the navigation sidebar of `DefaultLayout`, and `--text-primary` in `LoginForm`. Do not recolour it with a CSS `filter`: a filter also recolours the dot. With a `label` prop the SVG reads as an image with that name; without one it is `aria-hidden`, as in the header, where the button around it carries the name.

Each app has its own browser-tab icon, so a person can tell the two tabs apart. Both icons show the mark I.A (the bold I of "INIA", the dot at 1.6 times its wordmark size, and the slim A of ".AI") on a rounded tile:

| App | Tile | Files |
|---|---|---|
| Analyst app (this SPA) | navy tile, white letters, copper dot | `public/icon.svg`, `public/favicon.ico` (32 px), `public/apple-touch-icon.png` (180 px, opaque), linked from `index.html` |
| Workflow editor | copper tile, navy letters, white dot | the same three names in `platform:IniaAi.WebApp/wwwroot`, linked from `Components/App.razor` through `@Assets[...]`; the editor's own wordmarks are `wwwroot/images/inia-wordmark-navy.svg` and `inia-wordmark-white.svg` |

The ICO link carries `sizes="32x32"` so that Chrome takes the SVG icon over the ICO.

The workflow editor mirrors this palette: every editor colour lives in `platform:IniaAi.WebApp/Theme/`, where `IniaTheme.cs` carries the tokens of `src/assets/main.css` with vivid success, warning and error colours. A token change here needs the same change there.
