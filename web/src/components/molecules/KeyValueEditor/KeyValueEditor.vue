<script lang="ts" setup>
import { computed, ref, watch } from 'vue'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'

type Row = { id: number; key: string; value: string }

const props = defineProps<{
  modelValue?: Record<string, string> | null
  namePrefix?: string
  keyPlaceholder?: string
  valuePlaceholder?: string
  addLabel?: string
  removeLabel?: string
  duplicateKeyMessage?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: Record<string, string>]
}>()

let nextRowId = 0
const createRow = (key: string, value: string): Row => ({ id: nextRowId++, key, value })

const toRows = (record: Record<string, string> | null | undefined): Row[] =>
  record ? Object.entries(record).map(([key, value]) => createRow(key, value)) : []

const toRecord = (rows: Row[]): Record<string, string> => {
  // Keys are case-insensitive to match the backend; a later row with the same key (ignoring
  // case) wins, and its casing is the one stored.
  const byLowerKey = new Map<string, { key: string; value: string }>()
  for (const { key, value } of rows) {
    const trimmedKey = key.trim()
    if (trimmedKey === '') {
      continue
    }
    byLowerKey.set(trimmedKey.toLowerCase(), { key: trimmedKey, value })
  }

  const record: Record<string, string> = {}
  for (const entry of byLowerKey.values()) {
    record[entry.key] = entry.value
  }
  return record
}

const rows = ref<Row[]>(toRows(props.modelValue))

// Rows are the source of truth. Track our own last emission so the echo of an
// update:modelValue does not rebuild the rows and wipe an in-progress blank one;
// a genuinely external change still re-syncs.
let lastEmitted = JSON.stringify(props.modelValue ?? {})

watch(
  rows,
  () => {
    const record = toRecord(rows.value)
    lastEmitted = JSON.stringify(record)
    emit('update:modelValue', record)
  },
  { deep: true },
)

watch(
  () => props.modelValue,
  value => {
    if (JSON.stringify(value ?? {}) === lastEmitted) {
      return
    }
    rows.value = toRows(value)
  },
)

const addRow = () => {
  rows.value.push(createRow('', ''))
}

const removeRow = (index: number) => {
  rows.value.splice(index, 1)
}

const duplicateKeys = computed(() => {
  const seen = new Set<string>()
  const duplicates = new Set<string>()
  for (const { key } of rows.value) {
    const normalizedKey = key.trim().toLowerCase()
    if (normalizedKey === '') {
      continue
    }
    if (seen.has(normalizedKey)) {
      duplicates.add(normalizedKey)
    }
    seen.add(normalizedKey)
  }
  return duplicates
})

const isDuplicateKey = (key: string): boolean => {
  const normalizedKey = key.trim().toLowerCase()
  return normalizedKey !== '' && duplicateKeys.value.has(normalizedKey)
}

const fieldName = (kind: 'key' | 'value', index: number): string | undefined =>
  props.namePrefix ? `${props.namePrefix}-${kind}-${index}` : undefined
</script>

<template>
  <div class="flex flex-col gap-2">
    <div
      v-for="(row, index) in rows"
      :key="row.id"
      class="flex flex-col gap-1 rounded-md border p-2"
    >
      <Input
        :name="fieldName('key', index)"
        v-model="row.key"
        :placeholder="keyPlaceholder"
        :aria-label="keyPlaceholder"
        :class="{ 'border-destructive': isDuplicateKey(row.key) }"
      />
      <p
        v-if="duplicateKeyMessage && isDuplicateKey(row.key)"
        class="text-xs text-error"
      >
        {{ duplicateKeyMessage }}
      </p>
      <Textarea
        :name="fieldName('value', index)"
        v-model="row.value"
        :placeholder="valuePlaceholder"
        :aria-label="valuePlaceholder"
      />
      <div class="flex justify-end">
        <Button variant="outline" type="button" @click="removeRow(index)">
          {{ removeLabel }}
        </Button>
      </div>
    </div>

    <div>
      <Button variant="outline" type="button" @click="addRow">
        {{ addLabel }}
      </Button>
    </div>
  </div>
</template>
