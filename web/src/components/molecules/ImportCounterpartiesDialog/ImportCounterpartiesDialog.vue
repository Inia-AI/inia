<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import {
  CounterpartyImportDisposition,
  type CounterpartyBundle,
  type CounterpartyImportPlan,
  type CounterpartyImportRow,
} from '@/api/counterparties/schema'
import { useToast } from '@/components/ui/toast/use-toast'
import { formatDateTime } from '@/lib/utils'

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { IniaLoader } from '@/components/atoms'

const props = defineProps<{
  bundle: CounterpartyBundle | null
}>()

const emit = defineEmits<{
  close: []
}>()

const { t: $t } = useI18n()
const { toast } = useToast()

const importBundle = API.Counterparties.useImportBundle()

const plan = shallowRef<CounterpartyImportPlan | null>(null)

const open = computed(() => props.bundle !== null)

// The plan is read against the live instance the moment the file is opened, so what the admin
// reviews is what applying would do now, not what the file says about itself.
watch(
  () => props.bundle,
  async bundle => {
    plan.value = null
    if (!bundle) {
      return
    }
    try {
      const planned = await importBundle.mutateAsync({ bundle, dryRun: true })
      if (props.bundle === bundle) {
        plan.value = planned
      }
    } catch (error) {
      toast({
        description: extractApiErrorMessage(
          error,
          $t('page.counterpartyManagement.importDialog.planError'),
        ),
        variant: 'destructive',
      })
      emit('close')
    }
  },
  { immediate: true },
)

const WRITE_DISPOSITIONS = [
  CounterpartyImportDisposition.Create,
  CounterpartyImportDisposition.UpdateById,
  CounterpartyImportDisposition.Resurrect,
  CounterpartyImportDisposition.UpdateByName,
]

const COUNT_ORDER = [
  ...WRITE_DISPOSITIONS,
  CounterpartyImportDisposition.Unchanged,
  CounterpartyImportDisposition.Skip,
]

// Skipped rows lead: they are the ones the admin has to act on. Unchanged rows are counted only.
const ROW_GROUP_ORDER = [
  CounterpartyImportDisposition.Skip,
  ...WRITE_DISPOSITIONS,
]

const ROWS_SHOWN_PER_GROUP = 100

const UNNAMED = '—'

const rowsOf = (disposition: CounterpartyImportDisposition) =>
  (plan.value?.rows ?? []).filter(row => row.disposition === disposition)

const counts = computed(() =>
  COUNT_ORDER.map(disposition => ({
    disposition,
    value: rowsOf(disposition).length,
  })).filter(count => count.value > 0),
)

const nothingToApply = computed(
  () =>
    plan.value !== null &&
    !plan.value.applied &&
    WRITE_DISPOSITIONS.every(disposition => rowsOf(disposition).length === 0),
)

const stateKey = computed(() => {
  if (plan.value?.applied) {
    return 'applied'
  }
  return nothingToApply.value ? 'nothingToApply' : 'planned'
})

const groups = computed(() =>
  ROW_GROUP_ORDER.map(disposition => {
    const rows = rowsOf(disposition)
    return {
      disposition,
      total: rows.length,
      shown: rows.slice(0, ROWS_SHOWN_PER_GROUP),
    }
  }).filter(group => group.total > 0),
)

const landsElsewhere = (row: CounterpartyImportRow) =>
  row.disposition === CounterpartyImportDisposition.UpdateByName &&
  row.targetId !== null

const apply = async () => {
  const bundle = props.bundle
  if (!bundle) {
    return
  }
  try {
    const applied = await importBundle.mutateAsync({ bundle, dryRun: false })
    if (props.bundle === bundle) {
      plan.value = applied
    }
  } catch (error) {
    toast({
      description: extractApiErrorMessage(
        error,
        $t('page.counterpartyManagement.importDialog.applyError'),
      ),
      variant: 'destructive',
    })
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="!$event && emit('close')">
    <DialogContent
      class="flex max-h-[90dvh] flex-col overflow-hidden sm:max-w-[640px]"
    >
      <DialogHeader>
        <DialogTitle>{{
          $t('page.counterpartyManagement.importDialog.title')
        }}</DialogTitle>
        <DialogDescription v-if="bundle" data-testid="import-source">
          {{
            $t('page.counterpartyManagement.importDialog.source', {
              organization:
                bundle.sourceOrganizationName ??
                bundle.sourceOrganizationId ??
                UNNAMED,
              instance: bundle.sourceInstance ?? UNNAMED,
              date: formatDateTime(bundle.exportedAt),
            })
          }}
          {{
            $t('page.counterpartyManagement.importDialog.contents', {
              counterparties: $t(
                'page.counterpartyManagement.importDialog.contentsCounterparties',
                { count: bundle.counterparties.length },
                bundle.counterparties.length,
              ),
              types: $t(
                'page.counterpartyManagement.importDialog.contentsTypes',
                { count: bundle.counterpartyTypes.length },
                bundle.counterpartyTypes.length,
              ),
            })
          }}
        </DialogDescription>
      </DialogHeader>

      <div v-if="!plan" class="import-planning" data-testid="import-planning">
        <IniaLoader class="w-6" />
        <span>{{
          $t('page.counterpartyManagement.importDialog.planning')
        }}</span>
      </div>

      <template v-else>
        <p
          class="import-state"
          :class="{ 'import-state--done': plan.applied }"
          data-testid="import-state"
        >
          {{ $t(`page.counterpartyManagement.importDialog.${stateKey}`) }}
        </p>

        <ul class="import-counts" data-testid="import-counts">
          <li
            v-for="count in counts"
            :key="count.disposition"
            class="import-count"
            :class="{
              'import-count--skip':
                count.disposition === CounterpartyImportDisposition.Skip,
            }"
            :data-testid="`import-count-${count.disposition}`"
          >
            <span class="import-count-value">{{ count.value }}</span>
            <span class="import-count-label">{{
              $t(
                `page.counterpartyManagement.importDialog.disposition.${count.disposition}`,
              )
            }}</span>
          </li>
        </ul>

        <p
          v-if="plan.counterpartyTypesToCreate.length"
          class="import-types"
          data-testid="import-types"
        >
          {{ $t('page.counterpartyManagement.importDialog.typesToCreate') }}
          <strong>{{ plan.counterpartyTypesToCreate.join(', ') }}</strong>
        </p>

        <section class="import-report">
          <template v-for="group in groups" :key="group.disposition">
            <h3 class="import-heading">
              {{
                $t(
                  `page.counterpartyManagement.importDialog.disposition.${group.disposition}`,
                )
              }}
              · {{ group.total }}
            </h3>
            <ul
              class="import-rows"
              :data-testid="`import-rows-${group.disposition}`"
            >
              <li
                v-for="(row, index) in group.shown"
                :key="index"
                :class="{ 'import-row-skipped': row.skipReason }"
              >
                <span class="import-member">{{ row.name }}</span>
                <template v-if="row.skipReason">
                  <span class="import-reason">{{
                    $t(
                      `page.counterpartyManagement.importDialog.skipReason.${row.skipReason}`,
                    )
                  }}</span>
                  <span v-if="row.detail" class="import-detail">{{
                    row.detail
                  }}</span>
                </template>
                <span v-else-if="landsElsewhere(row)" class="import-reason">{{
                  $t('page.counterpartyManagement.importDialog.matchedByName', {
                    targetId: row.targetId,
                  })
                }}</span>
              </li>
              <li
                v-if="group.total > group.shown.length"
                class="import-more"
                data-testid="import-more"
              >
                {{
                  $t('page.counterpartyManagement.importDialog.moreRows', {
                    n: group.total - group.shown.length,
                  })
                }}
              </li>
            </ul>
          </template>
        </section>
      </template>

      <DialogFooter>
        <Button
          v-if="plan?.applied"
          data-testid="import-close"
          @click="emit('close')"
        >
          {{ $t('common.close') }}
        </Button>
        <template v-else>
          <Button
            variant="outline"
            :disabled="importBundle.isPending.value"
            @click="emit('close')"
          >
            {{ $t('common.cancel') }}
          </Button>
          <Button
            data-testid="import-apply"
            :disabled="!plan || nothingToApply || importBundle.isPending.value"
            @click="apply"
          >
            {{ $t('page.counterpartyManagement.importDialog.apply') }}
          </Button>
        </template>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.import-planning {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-md) 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.import-state {
  font-size: 13px;
  color: var(--text-secondary);
}

.import-state--done {
  color: var(--accent-success);
  font-weight: 500;
}

.import-counts {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
}

.import-count {
  display: flex;
  align-items: baseline;
  gap: 6px;
  padding: 4px 10px;
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
  background: var(--bg-1);
}

.import-count--skip {
  border-color: var(--accent-warning-border);
  background: var(--accent-warning-light);
}

.import-count-value {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
}

.import-count-label {
  font-size: 12px;
  color: var(--text-secondary);
}

.import-types {
  font-size: 13px;
  color: var(--text-primary);
}

.import-report {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-height: 0;
  overflow-y: auto;
  padding-right: var(--spacing-xs);
}

.import-heading {
  margin-top: var(--spacing-xs);
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-secondary);
}

.import-rows {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 13px;
}

.import-row-skipped {
  color: var(--accent-warning);
}

.import-more {
  font-size: 12px;
  color: var(--text-secondary);
}

.import-member {
  font-weight: 500;
}

.import-reason,
.import-detail {
  display: block;
  font-size: 12px;
  color: var(--text-secondary);
}
</style>
