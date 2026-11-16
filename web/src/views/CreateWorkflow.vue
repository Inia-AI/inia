<script lang="ts" setup>
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { fullFileName } from '@/lib/fileName'
import { withPagesRead } from '@/lib/pagesRead'
import { isDocumentProblem, submitValidationMessages } from '@/lib/workflowSubmitErrors'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'
import { v4 as uuidv4 } from 'uuid'

import API from '@/api'

import { OptionType, WorkflowFormData } from '@/types'
import { Workflow, WorkflowVariable } from '@/api/schema'
import { WorkflowVariableTypes } from '@/enums'
import { useToast } from '@/components/ui/toast/use-toast'
import { WorkflowCategorySummary } from '@/api/workflowCategories/schema'
import { extensionPoints } from '@/modules/extensionPoints'
import { ALL_PARENT_COMPANY_TAGS } from '@/enums/workflow'
import DocumentClassChipLabel from '@/components/molecules/DocumentClassChipLabel/DocumentClassChipLabel.vue'
import useCreateWorkflowPrefillStore from '@/store/createWorkflowPrefill'
import type { ReRunChanges, CreateWorkflowPrefill } from '@/lib/workflowReRunPrefill'
import { keepCreateWorkflowDraft, takeCreateWorkflowDraft } from '@/lib/createWorkflowDraft'
import {
  REQUIRED_VARIABLE_MESSAGE,
  blankVariableValueOf,
  appliedTemplateDefault,
  defaultVariableValues,
  isBlankVariableValue,
  numberVariableSchema,
  submittableVariables,
} from '@/lib/workflowVariableValues'
import {
  documentClassDisplayName,
  missingRequiredDocumentClasses,
  requiredClassesToChoose,
  singularClassViolations,
} from '@/lib/documentRequirements'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { WorkflowFileUploader } from '@/components/organisms'
import { ListFormField, IniaSelect, MultiSelect, CounterpartySearchableSelect, PeerCompanyMultiSelect } from '@/components/molecules'
import { usePeerCandidates } from '@/composables/usePeerCandidates'
import { IniaLoader } from '@/components/atoms'
import { Input } from '@/components/ui/input'

import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const router = useRouter()
const { t: $t } = useI18n()
const { toast } = useToast()

// Step 1: Load lightweight category summaries (fast initial load)
const {
  data: rawCategorySummaries,
  isError: isCategoriesError,
  refetch: refetchCategories,
} = API.WorkflowCategories.useGetWorkflowCategorySummaries()
const categorySummaries = computed(
  () => rawCategorySummaries.value as WorkflowCategorySummary[] | undefined,
)

// Track which workflow type needs full details
const selectedWorkflowTypeId = ref<string | null>(null)

// Step 2: Load full workflow details for selected workflow type (on-demand)
const {
  data: loadedWorkflow,
  isError: isWorkflowDetailsError,
  isLoading: isWorkflowDetailsLoading,
  refetch: refetchWorkflowDetails,
} = API.Workflows.useGetWorkflowSummaryByType(
  selectedWorkflowTypeId,
  {
    enabled: computed(() => !!selectedWorkflowTypeId.value),
  },
)

const { mutateAsync: createWorkflow, isPending: isLoading } =
  API.WorkflowExecutions.useCreateWorkflowExecution()

// Document validation error state
const documentValidationErrors = ref<string[]>([])
const documentValidationError = computed(() =>
  documentValidationErrors.value.length > 0 ? documentValidationErrors.value : null
)
const submitRefusal = ref<string | null>(null)


interface VariableFieldDefinition {
  component: any
  componentProps?: Record<string, any>
  name: string
  label?: string
  type: WorkflowVariableTypes
  description?: string
  isRequired?: boolean
}

const formData = ref<WorkflowFormData>({
  documents: undefined,
  type: undefined,
  counterpartyId: undefined,
  peerCompanyIds: [],
  variables: {},
})

const uploadRequestFields = ref<Record<string, unknown>>({})
const documentsAsRead = computed(() => withPagesRead(formData.value.documents ?? [], uploadRequestFields.value))

// The hand-off from a report (a re-run, changed documents, or the next workflow). Consumed
// synchronously in setup so it's already populated before the categorySummaries
// watcher's `immediate: true` run — otherwise a cached summaries query (warm
// from a prior visit) makes the watcher fire once with a still-null prefill,
// and never again because the value doesn't change. Hydration then runs in two
// stages: documents/type/counterparty land once summaries are loaded (so the
// existing form.values.type watcher can resolve `selectedWorkflowTypeId`), and
// variables are merged on top of the workflow's defaults once the on-demand
// details fetch completes.
const prefillStore = useCreateWorkflowPrefillStore()
const pendingPrefill = ref<CreateWorkflowPrefill | null>(prefillStore.consume() ?? takeCreateWorkflowDraft())

const reRunChanges = ref<ReRunChanges | null>(pendingPrefill.value?.reRunChanges ?? null)

const reRunWorkflowId = pendingPrefill.value?.workflowId

const hasNoValue = (value: unknown): boolean =>
  isBlankVariableValue(value) || (Array.isArray(value) && value.every(isBlankVariableValue))

const reRunVariablesStillNeeded = computed(() => {
  const values = (form.values.variables ?? {}) as Record<string, unknown>
  return (reRunChanges.value?.variablesNeedingInput ?? [])
    .filter(variable => hasNoValue(values[variable.replacementTag]))
})

const reRunReasonByField = computed(
  () => new Map(reRunVariablesStillNeeded.value.map(variable => [variable.replacementTag, variable.reason])),
)

const outputClassKeys = computed(
  () => new Set((selectedWorkflow.value?.documentClasses ?? []).filter(dc => dc.isOutputDocument).map(dc => dc.key)),
)

const reRunDocumentClassesStillNeeded = computed(() => {
  const documents = formData.value.documents ?? []
  return (reRunChanges.value?.documentClassesNeedingInput ?? []).filter(documentClass => {
    const sent = documents.some(document => document.class === documentClass.key)
    return outputClassKeys.value.has(documentClass.key) ? sent : !sent
  })
})

const reRunOutputClassConflicts = computed(
  () => reRunDocumentClassesStillNeeded.value.filter(documentClass => outputClassKeys.value.has(documentClass.key)),
)

const reRunDocumentClassKeys = computed(
  () => new Set(reRunDocumentClassesStillNeeded.value.map(documentClass => documentClass.key)),
)

const namesOf = (items: { displayName: string }[]) =>
  items.map(item => item.displayName).join(', ')

// The selected workflow from the loaded full details
const selectedWorkflow = computed<Workflow | undefined>(() => {
  if (!selectedWorkflowTypeId.value || !loadedWorkflow.value) return undefined

  // Return the loaded workflow if it matches the selected type and is available
  if (
    loadedWorkflow.value.workflowTypeId === selectedWorkflowTypeId.value &&
    loadedWorkflow.value.isAvailable
  ) {
    return loadedWorkflow.value as Workflow
  }

  return undefined
})

const platformClassifies = computed(() => selectedWorkflow.value?.classifiesDocuments === true)

const counterpartyParams = computed(() => {
  const wf = selectedWorkflow.value as Workflow & {
    counterpartyTypeMappings?: string[]
  }

  return {
    $top: 10000,
    $orderby: 'name asc',
    ...(wf?.counterpartyTypeMappings?.length
      ? { counterpartyTypeIds: wf.counterpartyTypeMappings.join(',') }
      : {}),
  }
})

const isWorkflowSelected = computed(() => !!selectedWorkflow.value)

const { data: counterparties } = API.Counterparties.useGetCounterparties(
  counterpartyParams,
  {
    enabled: computed(() => isWorkflowSelected.value),
  },
)

const dynamicSchema = computed(() => {
  const shape: Record<string, z.ZodTypeAny> = {}

  if (!selectedWorkflow.value) {
    return z.object(shape)
  }

  for (const field of selectedWorkflow.value?.variables) {
    if (
      field.type === WorkflowVariableTypes.MultiSelect ||
      field.type === WorkflowVariableTypes.List
    ) {
      shape[field.replacementTag] = z.array(z.string())
      if (!field.isRequired) {
        const current = shape[field.replacementTag]
        if (current) {
          shape[field.replacementTag] = current.optional()
        }
      }
    }

    if (field.type === WorkflowVariableTypes.DateTime) {
      shape[field.replacementTag] = field.isRequired
        ? z.string().min(1, REQUIRED_VARIABLE_MESSAGE)
        : z.string().optional()
    }

    if (field.type === WorkflowVariableTypes.Number) {
      shape[field.replacementTag] = numberVariableSchema(!!field.isRequired)
    }

    if (
      field.type === WorkflowVariableTypes.String ||
      field.type === WorkflowVariableTypes.Dropdown
    ) {
      shape[field.replacementTag] = z.string()
      if (!field.isRequired) {
        const current = shape[field.replacementTag]
        if (current) {
          shape[field.replacementTag] = current.optional()
        }
      } else {
        const current = shape[field.replacementTag]
        if (current) {
          shape[field.replacementTag] = (current as z.ZodString).min(1, REQUIRED_VARIABLE_MESSAGE)
        }
      }
    }
  }

  return z.object(shape)
})

const schema = computed(() => {
  return z.object({
    type: z.string(),
    documents: z
      .array(
        z.object({
          file: z.any().optional(),
          name: z.string().optional(),
          class: z.string().optional(),
          id: z.string().optional(),
          progress: z.number().optional(),
        }),
      )
      .optional(),
    counterpartyId: selectedWorkflow.value?.isCounterpartyRequired
      ? z.string({ error: $t('page.createWorkflow.form.errors.counterpartyRequired') }).trim().min(1, $t('page.createWorkflow.form.errors.counterpartyRequired'))
      : z.string().optional(),
    peerCompanyIds: z.array(z.string()),
    variables: dynamicSchema.value,
  })
})

const form = useForm<WorkflowFormData>({
  validationSchema: schema,
  initialValues: formData.value,
  keepValuesOnUnmount: true,
})

const handleSubmit = form.handleSubmit(async data => {
  documentValidationErrors.value = []
  submitRefusal.value = null

  try {
    const {
      type,
      documents,
      counterpartyId,
      peerCompanyIds,
      variables,
    } = data

    await createWorkflow({
      workflowId: type,
      files: documents?.map(x => {
        return {
          fileId: x.id,
          documentClass: x.class?.trim() || undefined,
          documentTags: [],
        }
      }),
      counterpartyId: counterpartyId?.trim() || undefined,
      peerCompanyIds: supportsPeerCompanies.value ? peerCompanyIds : [],
      variables: submittableVariables(variables ?? {}, selectedWorkflow.value?.variables ?? []),
      ...uploadRequestFields.value,
    })

    toast({
      class: 'text-sm',
      description: $t('page.createWorkflow.success'),
    })

    router.push({ name: 'Home' })
  } catch (error: any) {
    console.error('Workflow submission error:', {
      error,
      response: error?.response,
      data: error?.response?.data,
      errors: error?.response?.data?.errors,
    })

    const responseData = error?.response?.data
    const messages = submitValidationMessages(responseData)
    const toastMessage = messages[0] ?? (responseData?.message || responseData?.detail || $t('page.createWorkflow.failed'))

    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: toastMessage,
    })

    if (isDocumentProblem(responseData)) {
      documentValidationErrors.value = messages
      goToStep(stepIndexByKey('documents'))
    } else {
      submitRefusal.value = toastMessage
    }
  }
})

const attachedClassKeys = computed(() => new Set((formData.value.documents ?? []).map(document => document.class)))

const requiredDocuments = computed(() => {
  return selectedWorkflow?.value?.documentClasses
    ?.filter(x => x.requirement === 'Required' && !x.isOutputDocument) || []
})

const suggestedDocuments = computed(() => {
  return selectedWorkflow?.value?.documentClasses
    ?.filter(x => x.requirement === 'Suggested' && !x.isOutputDocument) || []
})

const optionalDocuments = computed(() => {
  return selectedWorkflow?.value?.documentClasses
    ?.filter(x => x.requirement === 'Optional' && !x.isOutputDocument) || []
})

const documentClassOptions = computed(() => {
  const docClasses = selectedWorkflow?.value?.documentClasses
  if (!docClasses?.length) return undefined

  const documentClassList = docClasses
    .filter(x => !x.isOutputDocument)
    .map(x => {
      return { label: x.displayName, value: x.key } as OptionType
    })
    .filter(item => item.label != null)
    .sort((a, b) => a.label.localeCompare(b.label))

  if (documentClassList.length) {
    documentClassList.unshift({
      label: platformClassifies.value
        ? $t('page.createWorkflow.documentsStep.letUsDecide')
        : $t('shared.components.fileControl.emptyClass'),
      value: ' ',
    })
  }

  return documentClassList
})

const counterpartyOptions = computed(() => {
  if (!counterparties?.value) {
    return
  }

  const items =
    counterparties?.value?.value?.map?.(x => ({
      label: x.name,
      value: x.id,
    })) ?? []

  if (selectedWorkflow.value?.isCounterpartyRequired) {
    return items
  }

  return [{ label: 'N/A', value: ' ' }, ...items]
})

const supportsPeerCompanies = computed(
  () => !!selectedWorkflow.value?.supportsPeerCompanies,
)

const knownCounterpartyOptions = computed(() =>
  (counterparties?.value?.value ?? []).map(x => ({
    label: x.name,
    value: x.id,
  })),
)

const {
  options: peerCompaniesOptions,
  search: peerSearch,
  isError: peerCandidatesFailed,
} = usePeerCandidates({
  workflowTypeId: selectedWorkflowTypeId,
  excludeCounterpartyId: computed(() => formData.value.counterpartyId),
  selectedIds: computed(() => formData.value.peerCompanyIds ?? []),
  enabled: computed(
    () => isWorkflowSelected.value && supportsPeerCompanies.value,
  ),
  knownCounterparties: knownCounterpartyOptions,
})

watch(selectedWorkflowTypeId, () => {
  peerSearch.value = ''
})

const onDocumentUpload = (documents: any[] | undefined) => {
  // Clear validation error when user changes documents
  documentValidationErrors.value = []

  // Documents are now uploaded by the WorkflowFileUploader component internally
  // We just receive the completed/uploaded documents here
  formData.value.documents = documents || []
  form.setFieldValue('documents', formData.value.documents)
}

// Use summaries for the workflow type dropdown display
const workflowTypeGroups = computed(() => {
  return (categorySummaries.value ?? [])
    .map(group => {
      const available = (group.workflows ?? [])
        .filter(w => w.isAvailable)
        .map(wf => ({
          id: wf.id,
          name: wf.workflowTypeName,
          summary: wf,
        }))
      return {
        label: group.name,
        description: group.description,
        workflows: available,
      }
    })
    .filter(group => group.workflows.length > 0)
})

const getOptionsEntries = (options: Record<string, string> | undefined) => {
  if (!options) return []

  return Object.entries(options)
}

const buildVariableFieldDefinition = (variable: WorkflowVariable): VariableFieldDefinition => {
  const prop: Partial<VariableFieldDefinition> = {}

  switch (variable.type) {
    case WorkflowVariableTypes.String:
      prop.component = Input
      prop.label = variable.displayName
      break
    case WorkflowVariableTypes.Dropdown:
      prop.component = IniaSelect
      prop.label = variable.displayName
      prop.componentProps = {
        options: getOptionsEntries(variable.options).map(
          ([internalValue, displayName]) => ({
            label: displayName,
            value: internalValue,
          }),
        ),
      }
      break
    case WorkflowVariableTypes.Number:
      prop.component = Input
      prop.componentProps = {
        type: 'number',
      }
      prop.label = variable.displayName
      break
    case WorkflowVariableTypes.DateTime:
      prop.component = Input
      prop.componentProps = {
        type: 'date',
      }
      prop.label = variable.displayName
      break
    case WorkflowVariableTypes.MultiSelect:
      prop.component = MultiSelect
      prop.label = variable.displayName
      prop.componentProps = {
        options: getOptionsEntries(variable.options).map(
          ([internalValue, displayName]) => ({
            label: displayName,
            value: internalValue,
          }),
        ),
      }
      break
    case WorkflowVariableTypes.List:
      prop.component = ListFormField
      prop.componentProps = {
        name: variable.replacementTag,
        header: variable.displayName,
      }
      break
  }

  prop.name = variable.replacementTag
  prop.type = variable.type
  prop.description = variable.description
  prop.isRequired = variable.isRequired

  return prop as VariableFieldDefinition
}

const variableFieldDefinitions = computed(() => {
  const variableFields: VariableFieldDefinition[] = []

  const variables = [...(selectedWorkflow.value?.variables || [])]
    .filter(v => !ALL_PARENT_COMPANY_TAGS.includes(v.replacementTag))
    .sort((a, b) => {
      if (a.isRequired !== b.isRequired) {
        return a.isRequired ? -1 : 1
      }
      return (a.displayName || '').localeCompare(b.displayName || '')
    })

  for (const variable of variables) {
    if (variable.isHidden) {
      continue
    }
    variableFields.push(buildVariableFieldDefinition(variable))
  }

  return variableFields
})

const parentCompanyVariableFieldDefinitions = computed(() => {
  const variableFields: VariableFieldDefinition[] = []

  const variables = [...(selectedWorkflow.value?.variables || [])]
    .filter(v => ALL_PARENT_COMPANY_TAGS.includes(v.replacementTag))
    .sort((a, b) => (a.displayName || '').localeCompare(b.displayName || ''))

  for (const variable of variables) {
    if (variable.isHidden) {
      continue
    }
    variableFields.push(buildVariableFieldDefinition(variable))
  }

  return variableFields
})

watch(
  () => formData.value.counterpartyId,
  val => {
    if (
      val &&
      formData.value?.peerCompanyIds?.length &&
      formData.value.peerCompanyIds.includes(val)
    ) {
      formData.value.peerCompanyIds = formData.value.peerCompanyIds.filter(
        x => x !== val,
      )
      form.setFieldValue('peerCompanyIds', formData.value.peerCompanyIds)
    }
  },
)

// When workflow selection changes, trigger loading full details
watch(
  () => form.values.type,
  val => {
    // keep your external state in sync
    formData.value.type = val
    if (reRunWorkflowId && val !== reRunWorkflowId) {
      reRunChanges.value = null
    }

    // Find the workflow type ID from summaries
    if (val) {
      for (const category of categorySummaries.value ?? []) {
        const workflow = category.workflows?.find(w => w.id === val)
        if (workflow) {
          selectedWorkflowTypeId.value = workflow.workflowTypeId
          // Clear variables while new workflow details are loading
          formData.value.variables = {}
          form.setFieldValue('variables', {}, false)
          return
        }
      }
    }
    selectedWorkflowTypeId.value = null
    // Clear variables when no workflow is selected
    formData.value.variables = {}
    form.setFieldValue('variables', {}, false)
  },
)

// Hydrate documents/type/counterparty from a pending prefill once the
// lightweight category summaries have loaded — we need them so the
// form.values.type watcher above can resolve `selectedWorkflowTypeId` from the
// canonical workflow id we just set. Variables are intentionally NOT applied
// here; they're merged on top of the workflow's defaults once `loadedWorkflow`
// resolves (see watcher below). Effectively single-shot: the early-exit guard
// short-circuits on subsequent fires once `pendingPrefill` is cleared.
watch(
  () => categorySummaries.value,
  summaries => {
    const prefill = pendingPrefill.value
    if (!prefill || !summaries) return

    // Workflow types can be retired between executions. The id has to both
    // appear in `summaries` AND have `isAvailable === true` — retired entries
    // remain in the payload so admin lists can show them, but the picker
    // (`workflowTypeGroups` below) hides them, so prefilling them would leave
    // the user on a half-filled form they can't submit.
    const stillAvailable = summaries.some(category =>
      category.workflows?.some(w => w.id === prefill.workflowId && w.isAvailable === true),
    )
    if (!stillAvailable) {
      pendingPrefill.value = null
      reRunChanges.value = null
      return
    }

    // Documents are uploaded server-side already (we have fileIds). Mark them
    // progress=100 + no `file` so the uploader treats them as completed rows.
    formData.value.documents = prefill.documents.map(d => ({
      id: d.fileId,
      ref: uuidv4(),
      class: d.documentClass,
      file: undefined as unknown as File,
      name: fullFileName(d),
      progress: 100,
      onProcessChange: () => {},
    }))
    formData.value.counterpartyId = prefill.counterpartyId
    formData.value.peerCompanyIds = prefill.peerCompanyIds ?? []
    formData.value.type = prefill.workflowId

    form.setFieldValue('documents', formData.value.documents, false)
    form.setFieldValue('counterpartyId', formData.value.counterpartyId, false)
    form.setFieldValue('peerCompanyIds', formData.value.peerCompanyIds, false)
    // setFieldValue('type', …) fires the watcher above, which sets
    // `selectedWorkflowTypeId` and clears variables. The `loadedWorkflow`
    // watcher then layers prefill variables over the workflow's defaults.
    form.setFieldValue('type', formData.value.type, false)
  },
  { immediate: true },
)

// When full workflow details are loaded, apply default variable values
watch(
  () => loadedWorkflow.value,
  workflow => {
    if (!workflow || !selectedWorkflowTypeId.value) return

    // Verify the loaded workflow matches the selected type
    if (workflow.workflowTypeId !== selectedWorkflowTypeId.value) return

    // compute and apply default variables
    const defaultVariables = defaultVariableValues((workflow as Workflow).variables ?? [])

    // If the user came in via "Edit inputs first" on a re-run, merge their
    // previous values on top of the defaults. Single-shot — once consumed,
    // any subsequent workflow type change picks up untouched defaults.
    const overrides = pendingPrefill.value?.variables ?? {}
    const startStep = pendingPrefill.value?.startStep
    pendingPrefill.value = null

    if (startStep) {
      goToStep(stepIndexByKey(startStep))
    }

    const merged: Record<string, unknown> = { ...defaultVariables, ...overrides }
    for (const { replacementTag } of reRunChanges.value?.variablesNeedingInput ?? []) {
      const definition = workflow.variables?.find(variable => variable.replacementTag === replacementTag)
      if (definition) {
        merged[replacementTag] = blankVariableValueOf(definition.type)
      }
    }
    formData.value.variables = merged
    form.setFieldValue('variables', merged, false)

    // also seed each nested field
    Object.entries(merged).forEach(([key, value]) => {
      form.setFieldValue(`variables.${key}`, value, false)
    })
  },
)

const stepKeys = ['type', 'documents', 'details', 'review'] as const

type StepKey = (typeof stepKeys)[number]

const steps = computed(() =>
  stepKeys.map(key => ({
    key,
    title: $t(`page.createWorkflow.steps.${key}.title`),
    hint: $t(`page.createWorkflow.steps.${key}.hint`),
  })),
)

const currentStepIndex = ref(0)
const currentStepKey = computed<StepKey>(() => stepKeys[currentStepIndex.value])
const currentStep = computed(() => steps.value[currentStepIndex.value])
const isFirstStep = computed(() => currentStepIndex.value === 0)
const isLastStep = computed(() => currentStepIndex.value === stepKeys.length - 1)

const stepIndexByKey = (key: StepKey) => stepKeys.indexOf(key)

// A full page load, such as the way to a payment page and back, would otherwise lose the form: the uploaded files stay on the server, so their ids are enough to show them again.
const keepFormForReturn = () => {
  if (!formData.value.type || !selectedWorkflowTypeId.value) {
    return
  }
  keepCreateWorkflowDraft({
    workflowId: formData.value.type,
    workflowTypeId: selectedWorkflowTypeId.value,
    counterpartyId: formData.value.counterpartyId,
    peerCompanyIds: formData.value.peerCompanyIds ?? [],
    variables: (form.values.variables ?? {}) as Record<string, unknown>,
    documents: (formData.value.documents ?? []).filter(document => document.id).map(document => ({ fileId: document.id as string, documentClass: document.class, name: document.name, extension: '' })),
    startStep: currentStepKey.value === 'type' ? undefined : currentStepKey.value,
  })
}
onMounted(() => window.addEventListener('pagehide', keepFormForReturn))
onBeforeUnmount(() => window.removeEventListener('pagehide', keepFormForReturn))

const uploaderUploading = ref(false)
const showOptionalDocs = ref(false)

const canLeaveCurrentStep = computed(
  () => !(currentStepKey.value === 'documents' && uploaderUploading.value),
)

const goToStep = (index: number) => {
  if (index < 0 || index > stepKeys.length - 1) return
  if (index !== currentStepIndex.value && !canLeaveCurrentStep.value) return
  if (index !== currentStepIndex.value) {
    submitRefusal.value = null
  }
  currentStepIndex.value = index
}

// vee-validate keeps the errors of a check that ran while a step's fields were off screen, and one
// runs while the old step leaves. They would show for a moment when the new step appears, so a step
// enters with none; Continue on the details step checks again.
const dropStoredFieldErrors = () => Object.keys(form.errors.value).forEach(path => form.setFieldError(path as never, undefined))

const stepContentEl = ref<HTMLElement | null>(null)
const focusStepContent = () => stepContentEl.value?.focus()

const classesTheUserChooses = computed(() =>
  requiredClassesToChoose(requiredDocuments.value, selectedWorkflow.value),
)

const missingRequiredDocuments = computed(() =>
  missingRequiredDocumentClasses(classesTheUserChooses.value, formData.value.documents ?? []),
)

const requiredDocumentsSatisfied = computed(
  () => missingRequiredDocuments.value.length === 0,
)

const singularViolations = computed(() =>
  singularClassViolations(
    selectedWorkflow.value?.documentClasses?.filter(x => !x.isOutputDocument) ?? [],
    formData.value.documents ?? [],
  ),
)

const canContinue = computed(() => {
  switch (currentStepKey.value) {
    case 'type':
      return !!selectedWorkflow.value
    case 'documents':
      return (
        !uploaderUploading.value &&
        requiredDocumentsSatisfied.value &&
        singularViolations.value.length === 0 &&
        reRunOutputClassConflicts.value.length === 0
      )
    default:
      return true
  }
})

const goBack = () => goToStep(currentStepIndex.value - 1)

const goNext = async () => {
  if (!canContinue.value) return
  if (currentStepKey.value === 'details') {
    const { valid } = await form.validate()
    if (!valid) return
  }
  goToStep(currentStepIndex.value + 1)
}

const submitHolds = ref<Record<number, string | null>>({})
const submitHeldBy = computed(() => Object.values(submitHolds.value).find(reason => !!reason) ?? null)
const holdSubmit = (index: number, reason: string | null) => {
  submitHolds.value = { ...submitHolds.value, [index]: reason }
}

const submitLabels = ref<Record<number, string | null>>({})
const submitLabel = computed(() => Object.values(submitLabels.value).find(label => !!label) ?? null)
const nameSubmit = (index: number, label: string | null) => {
  submitLabels.value = { ...submitLabels.value, [index]: label }
}

const onFormSubmit = (event: Event) => {
  if (!isLastStep.value || submitHeldBy.value) {
    event.preventDefault()
    return
  }
  handleSubmit(event)
}

const selectedTypeMeta = computed(() => {
  const id = formData.value.type
  if (!id) return undefined
  for (const group of workflowTypeGroups.value) {
    const workflow = group.workflows.find(w => w.id === id)
    if (workflow) return { category: group.label, name: workflow.name }
  }
  return undefined
})

const selectedCounterpartyName = computed(() => {
  const id = formData.value.counterpartyId
  if (!id || !id.trim()) return undefined
  return counterpartyOptions.value?.find(o => o.value === id)?.label
})

const selectedPeerNames = computed(() => {
  const ids = formData.value.peerCompanyIds ?? []
  const options = peerCompaniesOptions.value ?? []
  return ids.map(id => options.find(o => o.value === id)?.label ?? id)
})

const reviewDocuments = computed(() =>
  (formData.value.documents ?? []).map(doc => ({
    id: doc.ref ?? doc.id ?? doc.name,
    name: doc.name,
    className: documentClassDisplayName(
      selectedWorkflow?.value?.documentClasses ?? [],
      doc.class,
    ),
  })),
)

const reviewVariables = computed(() => {
  const values = (form.values.variables ?? {}) as Record<string, unknown>
  const definitionsByTag = new Map(
    (selectedWorkflow.value?.variables ?? []).map(variable => [variable.replacementTag, variable]),
  )
  return [
    ...variableFieldDefinitions.value,
    ...parentCompanyVariableFieldDefinitions.value,
  ]
    .map(field => {
      const raw = values[field.name]
      const value = Array.isArray(raw) ? raw.join(', ') : raw
      const definition = definitionsByTag.get(field.name)
      const appliedDefault = definition && appliedTemplateDefault(definition, raw)
      const shown = isBlankVariableValue(value) ? '' : String(value)
      return {
        label: field.label ?? field.componentProps?.header ?? field.name,
        value: appliedDefault
          ? $t('page.createWorkflow.reviewStep.templateDefault', { value: appliedDefault })
          : shown,
      }
    })
    .filter(entry => entry.value.trim().length > 0)
})

</script>

<template>
  <div class="page-view">
    <div class="page-container">
      <div class="page-header">
        <h1 class="page-title">{{ $t('page.createWorkflow.title') }}</h1>
        <p class="page-subtitle">{{ $t('page.createWorkflow.subtitle') }}</p>
      </div>

      <div v-if="reRunChanges" class="rerun-changes" role="note" data-testid="rerun-changes">
        <IniaIcon name="circle-info" class="rerun-changes-icon" />
        <div class="rerun-changes-body">
          <p class="rerun-changes-title">{{ $t('page.createWorkflow.reRunChanges.title') }}</p>
          <p class="rerun-changes-lead">{{ $t('page.createWorkflow.reRunChanges.lead') }}</p>
          <ul class="rerun-changes-list">
            <i18n-t v-if="reRunVariablesStillNeeded.length" keypath="page.createWorkflow.reRunChanges.needsVariables" tag="li">
              <template #names><strong>{{ namesOf(reRunVariablesStillNeeded) }}</strong></template>
            </i18n-t>
            <i18n-t v-if="reRunDocumentClassesStillNeeded.length" keypath="page.createWorkflow.reRunChanges.needsDocuments" tag="li">
              <template #names><strong>{{ namesOf(reRunDocumentClassesStillNeeded) }}</strong></template>
            </i18n-t>
            <i18n-t v-if="reRunChanges.unavailableDocuments.length" keypath="page.createWorkflow.reRunChanges.unavailableDocuments" tag="li">
              <template #names><strong>{{ reRunChanges.unavailableDocuments.join(', ') }}</strong></template>
            </i18n-t>
            <i18n-t v-if="reRunChanges.removedVariables.length" keypath="page.createWorkflow.reRunChanges.removed" tag="li">
              <template #names><strong>{{ namesOf(reRunChanges.removedVariables) }}</strong></template>
            </i18n-t>
            <i18n-t v-if="reRunChanges.defaultedVariables.length" keypath="page.createWorkflow.reRunChanges.defaulted" tag="li">
              <template #names><strong>{{ namesOf(reRunChanges.defaultedVariables) }}</strong></template>
            </i18n-t>
          </ul>
        </div>
      </div>

      <!-- eslint-disable-next-line vuejs-accessibility/no-redundant-roles -- Safari drops the list semantics of a list styled list-style: none; the explicit role keeps them -->
      <ol class="step-rail" role="list" :aria-label="$t('page.createWorkflow.stepList')">
        <li
          v-for="(step, index) in steps"
          :key="step.key"
          class="step-rail-item"
          :class="{
            'is-active': index === currentStepIndex,
            'is-complete': index < currentStepIndex,
          }"
          :aria-current="index === currentStepIndex ? 'step' : undefined"
        >
          <button
            v-if="index < currentStepIndex"
            type="button"
            class="step-rail-entry"
            :disabled="!canLeaveCurrentStep"
            @click="goToStep(index)"
          >
            <span class="step-rail-marker">
              <IniaIcon name="check" classes="" />
              <span class="sr-only">{{ index + 1 }}</span>
            </span>
            <span class="step-rail-text max-md:sr-only">
              <span class="step-rail-title">{{ step.title }}</span>
              <span class="step-rail-hint">{{ step.hint }}</span>
              <span class="sr-only">{{ $t('page.createWorkflow.stepCompleted') }}</span>
            </span>
          </button>
          <div v-else class="step-rail-entry">
            <span class="step-rail-marker">{{ index + 1 }}</span>
            <span class="step-rail-text max-md:sr-only">
              <span class="step-rail-title">{{ step.title }}</span>
              <span class="step-rail-hint">{{ step.hint }}</span>
            </span>
          </div>
        </li>
      </ol>

      <form @submit="onFormSubmit" class="workflow-form">
        <div class="card-panel step-panel">
          <Transition name="step" mode="out-in" @before-enter="dropStoredFieldErrors" @after-enter="focusStepContent">
            <div
              :key="currentStep.key"
              ref="stepContentEl"
              class="step-content"
              tabindex="-1"
              role="group"
              :aria-label="currentStep.title"
            >
              <section v-if="currentStep.key === 'type'" class="step-section">
                <header class="step-heading">
                  <h2 id="report-type-step-title" class="step-title">{{ $t('page.createWorkflow.typeStep.title') }}</h2>
                  <p class="step-lead">{{ $t('page.createWorkflow.typeStep.lead') }}</p>
                </header>

                <div v-if="isCategoriesError" class="inline-error">
                  <p class="inline-error-text">{{ $t('page.createWorkflow.typeStep.loadTypesFailed') }}</p>
                  <Button variant="outline" size="sm" @click="() => refetchCategories()">{{ $t('page.createWorkflow.retry') }}</Button>
                </div>

                <FormField v-slot="{ componentField }" name="type">
                  <FormItem>
                    <FormControl>
                      <div class="type-groups" role="radiogroup" aria-labelledby="report-type-step-title">
                        <div
                          v-for="group in workflowTypeGroups"
                          :key="group.label"
                          class="type-group"
                        >
                          <div class="type-group-head">
                            <span class="type-group-eyebrow">{{ group.label }}</span>
                            <p v-if="group.description" class="type-group-desc">{{ group.description }}</p>
                          </div>
                          <div class="type-grid">
                            <label
                              v-for="workflow in group.workflows"
                              :key="workflow.id"
                              class="type-card"
                              :class="{ 'is-selected': componentField.modelValue === workflow.id }"
                            >
                              <input
                                type="radio"
                                class="sr-only"
                                :value="workflow.id"
                                :checked="componentField.modelValue === workflow.id"
                                v-bind="componentField"
                              />
                              <span class="type-card-check">
                                <IniaIcon name="check" />
                              </span>
                              <span v-if="extensionPoints.reportTypeDetails.length" class="type-card-text">
                                <span class="type-card-name">{{ workflow.name }}</span>
                                <component
                                  :is="piece"
                                  v-for="(piece, index) in extensionPoints.reportTypeDetails"
                                  :key="index"
                                  :workflow="workflow.summary"
                                />
                              </span>
                              <span v-else class="type-card-name">{{ workflow.name }}</span>
                            </label>
                          </div>
                        </div>
                      </div>
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                </FormField>

                <div v-if="isWorkflowDetailsLoading && selectedWorkflowTypeId" class="inline-note">
                  <IniaLoader class="w-4 h-4" />
                  <span>{{ $t('page.createWorkflow.typeStep.loadingDetails') }}</span>
                </div>

                <div v-if="isWorkflowDetailsError && selectedWorkflowTypeId" class="inline-error">
                  <p class="inline-error-text">{{ $t('page.createWorkflow.typeStep.loadDetailsFailed') }}</p>
                  <Button variant="outline" size="sm" @click="() => refetchWorkflowDetails()">{{ $t('page.createWorkflow.retry') }}</Button>
                </div>
              </section>

              <section v-else-if="currentStep.key === 'documents'" class="step-section">
                <header class="step-heading">
                  <h2 class="step-title">{{ $t('page.createWorkflow.documentsStep.title') }}</h2>
                  <p class="step-lead">{{ $t('page.createWorkflow.documentsStep.lead', { name: selectedTypeMeta?.name }) }}</p>
                </header>

                <div
                  v-if="requiredDocuments.length > 0 || suggestedDocuments.length > 0 || optionalDocuments.length > 0"
                  class="doc-requirements"
                >
                  <div v-if="requiredDocuments.length > 0" class="doc-req-group">
                    <span class="doc-req-label doc-req-required">{{ $t(platformClassifies ? 'page.createWorkflow.documentsStep.needed' : 'page.createWorkflow.documentsStep.required') }}</span>
                    <div class="doc-req-chips">
                      <span
                        v-for="doc in requiredDocuments"
                        :key="doc.key"
                        class="doc-chip"
                        :class="{ 'doc-chip--rerun': reRunDocumentClassKeys.has(doc.key) }"
                      >
                        <DocumentClassChipLabel :document-class="doc" :empty="!attachedClassKeys.has(doc.key)" />
                      </span>
                    </div>
                  </div>

                  <div v-if="suggestedDocuments.length > 0" class="doc-req-group">
                    <span class="doc-req-label doc-req-suggested">{{ $t('page.createWorkflow.documentsStep.suggested') }}</span>
                    <div class="doc-req-chips">
                      <span v-for="doc in suggestedDocuments" :key="doc.key" class="doc-chip">
                        <DocumentClassChipLabel :document-class="doc" :empty="!attachedClassKeys.has(doc.key)" />
                      </span>
                    </div>
                  </div>

                  <div v-if="optionalDocuments.length > 0" class="doc-req-group">
                    <button
                      type="button"
                      class="doc-req-toggle"
                      :aria-expanded="showOptionalDocs"
                      @click="showOptionalDocs = !showOptionalDocs"
                    >
                      <span class="doc-req-label doc-req-optional">{{ $t('page.createWorkflow.documentsStep.optional') }}</span>
                      <span class="doc-req-count">{{ optionalDocuments.length }}</span>
                      <IniaIcon :name="showOptionalDocs ? 'chevron-down' : 'chevron-right'" class="doc-req-caret" />
                    </button>
                    <div v-if="showOptionalDocs" class="doc-req-chips">
                      <span v-for="doc in optionalDocuments" :key="doc.key" class="doc-chip">
                        <DocumentClassChipLabel :document-class="doc" :empty="!attachedClassKeys.has(doc.key)" />
                      </span>
                    </div>
                  </div>
                </div>

                <FormField v-slot="{ componentField }" name="documents">
                  <FormItem>
                    <div v-if="documentValidationError" class="inline-error">
                      <p class="inline-error-text">{{ $t('page.createWorkflow.documentsStep.reviewFollowing') }}</p>
                      <ul class="inline-error-list">
                        <li v-for="(error, index) in documentValidationError" :key="index">{{ error }}</li>
                      </ul>
                    </div>

                    <FormControl>
                      <WorkflowFileUploader
                        v-bind="componentField"
                        :name="'documents'"
                        :model-value="formData.documents"
                        :document-class-options="documentClassOptions"
                        v-model:request-fields="uploadRequestFields"
                        @update:model-value="onDocumentUpload"
                        @update:uploading="uploaderUploading = $event"
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                </FormField>

                <div v-if="platformClassifies" class="inline-note">
                  <IniaIcon name="circle-info" />
                  <i18n-t
                    v-if="classesTheUserChooses.length"
                    keypath="page.createWorkflow.documentsStep.classPartlyOptionalNote"
                    :plural="classesTheUserChooses.length"
                    tag="span"
                  >
                    <template #documents>
                      <strong>{{ classesTheUserChooses.map(doc => doc.displayName).join(', ') }}</strong>
                    </template>
                  </i18n-t>
                  <span v-else>{{ $t('page.createWorkflow.documentsStep.classOptionalNote') }}</span>
                </div>

                <div v-if="missingRequiredDocuments.length" class="doc-gate-note">
                  <IniaIcon name="circle-info" />
                  <span>
                    {{ $t('page.createWorkflow.documentsStep.missingRequiredNote', missingRequiredDocuments.length) }}
                    <strong>{{ missingRequiredDocuments.map(doc => doc.displayName).join(', ') }}</strong>
                  </span>
                </div>

                <div v-if="reRunOutputClassConflicts.length" class="doc-gate-note">
                  <IniaIcon name="circle-info" />
                  <ul class="inline-error-list">
                    <li v-for="documentClass in reRunOutputClassConflicts" :key="documentClass.key">{{ documentClass.message }}</li>
                  </ul>
                </div>

                <div v-if="singularViolations.length" class="doc-gate-note">
                  <IniaIcon name="circle-info" />
                  <span>
                    {{ $t('page.createWorkflow.documentsStep.singularViolationNote', singularViolations.length) }}
                    <strong>{{ singularViolations.map(doc => doc.displayName).join(', ') }}</strong>
                  </span>
                </div>
              </section>

              <section v-else-if="currentStep.key === 'details'" class="step-section">
                <header class="step-heading">
                  <h2 class="step-title">{{ $t('page.createWorkflow.detailsStep.title') }}</h2>
                  <p class="step-lead">{{ $t('page.createWorkflow.detailsStep.lead') }}</p>
                </header>

                <div class="form-bottom-grid">
                  <div class="details-column">
                    <FormField v-slot="{ componentField }" name="counterpartyId">
                      <FormItem>
                        <FormLabel>{{ $t('page.createWorkflow.form.labels.counterpartyId') }}<span v-if="selectedWorkflow?.isCounterpartyRequired" class="text-error ml-1">*</span></FormLabel>
                        <FormControl>
                          <CounterpartySearchableSelect
                            v-model="formData.counterpartyId"
                            v-bind="componentField"
                            :options="counterpartyOptions"
                            :disabled="!selectedWorkflow"
                            :counterparty-type-ids="selectedWorkflow?.counterpartyTypeMappings ?? []"
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    </FormField>

                    <FormField v-if="supportsPeerCompanies" v-slot="{ componentField }" name="peerCompanyIds">
                      <FormItem>
                        <FormLabel>{{ $t('page.createWorkflow.form.labels.peerCompanies') }}</FormLabel>
                        <FormControl>
                          <PeerCompanyMultiSelect
                            v-model="formData.peerCompanyIds"
                            v-bind="componentField"
                            :options="peerCompaniesOptions"
                            :counterparty-type-ids="selectedWorkflow?.counterpartyTypeMappings ?? []"
                            :load-failed="peerCandidatesFailed"
                            @update:search="peerSearch = $event"
                          />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    </FormField>

                    <div
                      v-for="field in variableFieldDefinitions"
                      :key="field.name"
                      :data-rerun-field="reRunReasonByField.has(field.name) ? field.name : undefined"
                    >
                      <ListFormField
                        v-if="field.type === WorkflowVariableTypes.List"
                        :name="`variables.${field.name}`"
                        :header="field.componentProps?.header"
                        :description="field.description"
                      />

                      <FormField
                        v-else
                        v-slot="{ componentField }"
                        :name="`variables.${field.name}`"
                      >
                        <FormItem>
                          <FormLabel>
                            <div class="flex items-center space-x-2">
                              <span>{{ field.label }}<span v-if="field.isRequired" class="text-error ml-1">*</span></span><span v-if="reRunReasonByField.has(field.name)" class="rerun-field-badge"><span class="sr-only">, </span>{{ $t(`page.createWorkflow.reRunChanges.badge.${reRunReasonByField.get(field.name)}`) }}</span>
                              <TooltipProvider v-if="field.description">
                                <Tooltip>
                                  <TooltipTrigger as-child>
                                    <IniaIcon name="circle-info" class="text-md" />
                                  </TooltipTrigger>
                                  <TooltipContent>
                                    <p>{{ field.description }}</p>
                                  </TooltipContent>
                                </Tooltip>
                              </TooltipProvider>
                            </div>
                          </FormLabel>
                          <FormControl>
                            <component
                              v-if="formData.variables"
                              :is="field.component"
                              v-bind="{ ...componentField, ...field.componentProps }"
                            />
                          </FormControl>
                          <FormMessage />
                        </FormItem>
                      </FormField>
                    </div>
                  </div>

                  <div v-if="parentCompanyVariableFieldDefinitions.length > 0" class="form-right-column">
                    <div class="right-column-section">
                      <div class="url-section-header">
                        <h3 class="url-section-title">{{ $t('page.createWorkflow.form.labels.organizationalStructure') }}</h3>
                      </div>
                      <div class="parent-company-fields">
                        <div
                          v-for="field in parentCompanyVariableFieldDefinitions"
                          :key="field.name"
                          :data-rerun-field="reRunReasonByField.has(field.name) ? field.name : undefined"
                        >
                          <ListFormField
                            v-if="field.type === WorkflowVariableTypes.List"
                            :name="`variables.${field.name}`"
                            :header="field.componentProps?.header"
                            :description="field.description"
                          />

                          <FormField
                            v-else
                            v-slot="{ componentField }"
                            :name="`variables.${field.name}`"
                          >
                            <FormItem>
                              <FormLabel>
                                <div class="flex items-center space-x-2">
                                  <span>{{ field.label }}<span v-if="field.isRequired" class="text-error ml-1">*</span></span><span v-if="reRunReasonByField.has(field.name)" class="rerun-field-badge"><span class="sr-only">, </span>{{ $t(`page.createWorkflow.reRunChanges.badge.${reRunReasonByField.get(field.name)}`) }}</span>
                                  <TooltipProvider v-if="field.description">
                                    <Tooltip>
                                      <TooltipTrigger as-child>
                                        <IniaIcon name="circle-info" class="text-md" />
                                      </TooltipTrigger>
                                      <TooltipContent>
                                        <p>{{ field.description }}</p>
                                      </TooltipContent>
                                    </Tooltip>
                                  </TooltipProvider>
                                </div>
                              </FormLabel>
                              <FormControl>
                                <component
                                  v-if="formData.variables"
                                  :is="field.component"
                                  v-bind="{ ...componentField, ...field.componentProps }"
                                />
                              </FormControl>
                              <FormMessage />
                            </FormItem>
                          </FormField>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </section>

              <section v-else class="step-section">
                <header class="step-heading">
                  <h2 class="step-title">{{ $t('page.createWorkflow.reviewStep.title') }}</h2>
                  <p class="step-lead">{{ $t('page.createWorkflow.reviewStep.lead') }}</p>
                </header>

                <div v-if="submitRefusal" class="inline-error" role="alert" data-testid="submit-refusal">
                  <p class="inline-error-text">{{ submitRefusal }}</p>
                </div>

                <dl class="review-list">
                  <div class="review-row">
                    <dt class="review-label">{{ $t('page.createWorkflow.reviewStep.reportType') }}</dt>
                    <dd class="review-value">
                      {{ selectedTypeMeta?.name ?? '—' }}
                      <span v-if="selectedTypeMeta?.category" class="review-sub">{{ selectedTypeMeta.category }}</span>
                    </dd>
                  </div>

                  <div class="review-row">
                    <dt class="review-label">{{ $t('page.createWorkflow.reviewStep.documents') }}</dt>
                    <dd class="review-value">
                      <template v-if="reviewDocuments.length">
                        <ul class="review-doc-list">
                          <li v-for="doc in reviewDocuments" :key="doc.id">
                            <IniaIcon name="file-lines" class="review-doc-icon" />
                            <span class="review-doc-name">{{ doc.name }}</span>
                            <span v-if="doc.className" class="review-doc-class">{{ doc.className }}</span>
                            <span v-else-if="platformClassifies" class="review-doc-class">
                              {{ $t('page.createWorkflow.reviewStep.weDecide') }}
                            </span>
                            <span v-else class="review-doc-class review-doc-class-missing">
                              {{ $t('page.createWorkflow.reviewStep.unclassified') }}
                            </span>
                          </li>
                        </ul>
                      </template>
                      <span v-else class="review-empty">{{ $t('page.createWorkflow.reviewStep.noDocuments') }}</span>
                    </dd>
                  </div>

                  <div class="review-row">
                    <dt class="review-label">{{ $t('page.createWorkflow.form.labels.counterpartyId') }}</dt>
                    <dd class="review-value">{{ selectedCounterpartyName ?? '—' }}</dd>
                  </div>

                  <div v-if="supportsPeerCompanies && selectedPeerNames.length" class="review-row">
                    <dt class="review-label">{{ $t('page.createWorkflow.form.labels.peerCompanies') }}</dt>
                    <dd class="review-value">{{ selectedPeerNames.join(', ') }}</dd>
                  </div>

                  <div v-for="entry in reviewVariables" :key="entry.label" class="review-row">
                    <dt class="review-label">{{ entry.label }}</dt>
                    <dd class="review-value">{{ entry.value }}</dd>
                  </div>

                  <component
                    :is="piece"
                    v-for="(piece, index) in extensionPoints.reviewDetails"
                    :key="index"
                    :workflow="selectedWorkflow"
                    :documents="documentsAsRead"
                    @holds="(reason: string | null) => holdSubmit(index, reason)"
                    @submit-label="(label: string | null) => nameSubmit(index, label)"
                  />
                </dl>

                <p v-if="submitHeldBy" id="submit-held" class="inline-error inline-error-text" data-testid="submit-held">
                  {{ submitHeldBy }}
                </p>
              </section>
            </div>
          </Transition>
        </div>

        <div class="step-footer">
          <Button
            v-if="!isFirstStep"
            type="button"
            variant="outline"
            :disabled="!canLeaveCurrentStep"
            @click="goBack"
          >
            {{ $t('page.createWorkflow.back') }}
          </Button>
          <span class="step-footer-spacer" />
          <Button
            v-if="!isLastStep"
            type="button"
            fontSize="lg"
            class="btn-gradient-primary"
            :disabled="!canContinue"
            @click="goNext"
          >
            {{ $t('page.createWorkflow.continue') }}
            <IniaIcon name="chevron-right" />
          </Button>
          <Button
            v-else
            type="submit"
            fontSize="lg"
            class="btn-gradient-primary"
            :is-loading="isLoading"
            :disabled="!!submitHeldBy"
            :aria-describedby="submitHeldBy ? 'submit-held' : undefined"
          >
            {{ submitLabel ?? $t('page.createWorkflow.form.submitLabel') }}
          </Button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.workflow-form {
  display: flex;
  flex-direction: column;
}

.step-rail {
  display: flex;
  gap: var(--spacing-xs);
  margin-bottom: var(--spacing-md);
  list-style: none;
  padding: 0;
}

.step-rail-item {
  flex: 1;
  position: relative;
}

.step-rail-item::after {
  content: '';
  position: absolute;
  top: 19px;
  left: calc(50% + 20px);
  right: calc(-50% + 20px);
  height: 2px;
  background: var(--stroke);
  transition: background 0.3s ease;
}

.step-rail-item:last-child::after {
  display: none;
}

.step-rail-item.is-complete::after {
  background: var(--primary);
}

.step-rail-entry {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-xs);
  width: 100%;
  background: transparent;
  border: none;
  padding: 0;
  cursor: default;
  text-align: center;
}

button.step-rail-entry:enabled {
  cursor: pointer;
}

.step-rail-marker {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--bg-1);
  border: 2px solid var(--stroke);
  color: var(--text-muted);
  font-size: 14px;
  font-weight: 600;
  transition: all 0.25s ease;
  position: relative;
  z-index: 1;
}

.step-rail-item.is-active .step-rail-marker {
  border-color: transparent;
  background: var(--primary);
  color: var(--primary-foreground);
  box-shadow: 0 4px 14px rgba(26, 56, 80, 0.35);
}

.step-rail-item.is-complete .step-rail-marker {
  border-color: var(--accent-indigo-from);
  background: var(--primary);
  color: var(--primary-foreground);
}

.step-rail-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.step-rail-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-secondary);
  transition: color 0.25s ease;
}

.step-rail-item.is-active .step-rail-title {
  color: var(--text-primary);
}

.step-rail-hint {
  font-size: 11px;
  color: var(--text-muted);
}

.step-panel {
  overflow: hidden;
}

.step-content {
  display: block;
}

.step-content:focus {
  outline: none;
}

.step-section {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.step-heading {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.step-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: -0.01em;
}

.step-lead {
  font-size: 14px;
  color: var(--text-secondary);
  max-width: 60ch;
}

.step-enter-active,
.step-leave-active {
  transition: opacity 0.22s ease, transform 0.22s ease;
}

.step-enter-from {
  opacity: 0;
  transform: translateX(12px);
}

.step-leave-to {
  opacity: 0;
  transform: translateX(-12px);
}

.type-groups {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

.type-group-head {
  margin-bottom: var(--spacing-sm);
}

.type-group-eyebrow {
  display: block;
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-secondary);
}

.type-group-desc {
  margin-top: 2px;
  font-size: 13px;
  color: var(--text-muted);
}

.type-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: var(--spacing-sm);
}

.type-card {
  position: relative;
  display: flex;
  align-items: center;
  min-height: 64px;
  padding: var(--spacing-sm) var(--spacing-md);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-1);
  cursor: pointer;
  transition: all 0.15s ease;
}

.type-card:hover {
  border-color: var(--stroke-hover);
  transform: translateY(-2px);
  box-shadow: var(--shadow-card);
}

.type-card:has(input:focus-visible) {
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 2px var(--accent-indigo-from);
}

.type-card.is-selected {
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 1px var(--accent-indigo-from);
  background: color-mix(in srgb, var(--accent-indigo-from) 6%, var(--bg-1));
}

.type-card-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.type-card-name {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
  padding-right: var(--spacing-md);
}

.type-card-check {
  position: absolute;
  top: 10px;
  right: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: var(--primary);
  color: var(--primary-foreground);
  font-size: 11px;
  opacity: 0;
  transform: scale(0.6);
  transition: all 0.15s ease;
}

.type-card.is-selected .type-card-check {
  opacity: 1;
  transform: scale(1);
}

.doc-requirements {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-2);
}

.doc-req-group {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.doc-req-label {
  align-self: flex-start;
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 2px 8px;
  border-radius: var(--radius-small);
}

.doc-req-required {
  color: var(--accent-error);
  background: var(--accent-error-light);
}

.doc-req-suggested {
  color: var(--accent-warning);
  background: var(--accent-warning-light);
}

.doc-req-optional {
  color: var(--text-secondary);
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
}

.doc-req-toggle {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  align-self: flex-start;
  background: transparent;
  border: none;
  padding: 0;
  cursor: pointer;
}

.doc-req-count {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
}

.doc-req-caret {
  font-size: 11px;
  color: var(--text-muted);
}

.doc-req-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.doc-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  font-size: 12px;
  color: var(--text-primary);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 999px;
}

.doc-gate-note {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--accent-warning-border);
  border-radius: var(--radius-card);
  background: var(--accent-warning-light);
  font-size: 13px;
  color: var(--text-secondary);
}

.doc-gate-note strong {
  color: var(--text-primary);
}

.rerun-changes {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-md);
  padding: var(--spacing-md);
  border: 1px solid var(--accent-info-border);
  border-radius: var(--radius-card);
  background: var(--accent-info-light);
  font-size: 13px;
  color: var(--text-primary);
}

.rerun-changes-icon {
  flex-shrink: 0;
  margin-top: 2px;
  color: var(--accent-info);
}

.rerun-changes-body {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  min-width: 0;
}

.rerun-changes-title {
  font-size: 14px;
  font-weight: 600;
}

.rerun-changes-lead {
  color: var(--text-secondary);
}

.rerun-changes-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-left: 1.1em;
  list-style: disc;
}

.rerun-changes strong {
  font-weight: 600;
  color: var(--text-primary);
}

.rerun-field-badge {
  display: inline-block;
  padding: 0 8px;
  border: 1px solid var(--accent-warning-border);
  border-radius: 999px;
  background: var(--accent-warning-light);
  color: var(--accent-warning);
  font-size: 11px;
  font-weight: 600;
  line-height: 1.6;
}

.doc-chip--rerun {
  border-color: var(--accent-warning-border);
  background: var(--accent-warning-light);
  font-weight: 600;
}

.form-bottom-grid {
  display: grid;
  grid-template-columns: 1fr minmax(320px, 380px);
  gap: var(--spacing-md);
  align-items: start;
}

.details-column {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  max-width: 32rem;
}

.form-right-column {
  position: sticky;
  top: var(--spacing-lg);
  align-self: start;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.right-column-section {
  background: var(--bg-2);
  padding: var(--spacing-md);
  border-radius: var(--radius-card);
  border: 1px solid var(--stroke);
}

.url-section-header {
  margin-bottom: var(--spacing-md);
}

.url-section-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
  margin: 0 0 8px 0;
}

.parent-company-fields {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.review-list {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  overflow: hidden;
}

.review-row {
  display: grid;
  grid-template-columns: 200px 1fr;
  gap: var(--spacing-md);
  padding: var(--spacing-sm) var(--spacing-md);
  border-bottom: 1px solid var(--stroke);
}

.review-row:last-child {
  border-bottom: none;
}

.review-row:nth-child(odd) {
  background: var(--bg-2);
}

.review-label {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-secondary);
}

.review-value {
  min-width: 0;
  font-size: 14px;
  color: var(--text-primary);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.review-sub {
  font-size: 12px;
  color: var(--text-muted);
}

.review-empty {
  color: var(--text-muted);
}

.review-doc-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  list-style: none;
  padding: 0;
  margin: 0;
}

.review-doc-list li {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.review-doc-icon {
  color: var(--text-muted);
}

.review-doc-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.review-doc-class {
  flex-shrink: 0;
  padding: 2px 10px;
  border: 1px solid var(--stroke);
  border-radius: 999px;
  background: var(--bg-2);
  font-size: 12px;
  color: var(--text-secondary);
}

.review-doc-class-missing {
  border-color: var(--accent-warning-border);
  background: var(--accent-warning-light);
  color: var(--accent-warning);
}

.inline-note {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-2);
  font-size: 13px;
  color: var(--text-secondary);
}

.inline-error {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--accent-error-border);
  border-radius: var(--radius-card);
  background: var(--accent-error-light);
}

.inline-error-text {
  font-size: 13px;
  font-weight: 600;
  color: var(--accent-error);
}

.inline-error-list {
  margin: 0;
  padding-left: var(--spacing-sm);
  list-style: disc;
  font-size: 12px;
  color: var(--accent-error);
}

.step-footer {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-md);
}

.step-footer-spacer {
  flex: 1;
}

@media (max-width: 768px) {
  .form-bottom-grid {
    grid-template-columns: 1fr;
  }

  .form-right-column {
    position: static;
  }

  .details-column {
    max-width: none;
  }

  .review-row {
    grid-template-columns: 1fr;
    gap: 4px;
  }
}
</style>
