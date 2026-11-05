<script setup lang="ts">
import { Field as FormField, useFieldArray } from 'vee-validate'
import { computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const { t: $t } = useI18n()

const props = defineProps({
  placeholder: {
    type: String,
    required: false,
    default: '',
  },
  label: {
    type: String,
    required: false,
    default: '',
  },
  header: {
    type: String,
    required: false,
    default: '',
  },
  name: {
    type: String,
    required: false,
    default: '',
  },
  limit: {
    type: Number,
    required: false,
    default: 10,
  },
  description: {
    type: String,
    required: false,
    default: '',
  },
  min: {
    type: Number,
    required: false,
    default: 0,
  },
})

const { fields, push, remove } = useFieldArray<string>(props.name)

onMounted(() => {
  const effectiveMin = Math.max(0, Math.min(props.min, props.limit))
  while (fields.value.length < effectiveMin) {
    push('')
  }
})

const isAtLimit = computed(() => fields.value.length >= props.limit)

const onAdd = () => {
  if (fields.value.length >= props.limit) {
    return
  }

  push('')
}
</script>

<template>
  <div>
    <slot name="header" :onAdd="onAdd" :isAtLimit="isAtLimit">
      <div class="flex space-x-4 items-center mb-4 w-max">
        <div class="flex items-center space-x-2">
          <span class="text-sm">{{ header }}</span>

          <TooltipProvider v-if="description">
            <Tooltip>
              <TooltipTrigger as-child>
                <button
                  type="button"
                  class="button-reset focus-ring hit-target"
                  :aria-label="$t('shared.components.listFormField.about', { list: header })"
                >
                  <IniaIcon name="circle-info" class="text-md" />
                </button>
              </TooltipTrigger>
              <TooltipContent>
                <p>{{ description }}</p>
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        </div>
        <button
          type="button"
          class="hit-target cursor-pointer"
          :class="{ 'opacity-30': isAtLimit }"
          :disabled="isAtLimit"
          @click="onAdd"
          :aria-label="$t('shared.components.listFormField.addItem', { list: header })"
        >
          <IniaIcon name="plus" />
        </button>
      </div>
    </slot>
    <p v-if="isAtLimit" class="text-sm text-muted-foreground mt-1">
      Maximum of {{ props.limit }} URLs allowed.
    </p>

    <div
      v-for="(field, index) in fields"
      :key="index"
      class="flex items-center gap-2 mb-6"
    >
      <FormField v-slot="{ componentField }" :name="`${name}[${index}]`">
        <FormItem class="flex-1">
          <FormLabel v-if="label" class="text-sm"
            >{{ label }} {{ index + 1 }}</FormLabel
          >
          <div class="flex items-center space-x-4">
            <FormControl>
              <Input v-bind="componentField" :placeholder="placeholder" />
            </FormControl>

            <Button
              v-if="fields.length > 1"
              variant="destructiveSoft"
              size="icon"
              :aria-label="$t('shared.components.listFormField.removeItem', { n: index + 1 })"
              @click="remove(index)"
            >
              <IniaIcon name="trash" />
            </Button>
          </div>

          <FormMessage />
        </FormItem>
      </FormField>
    </div>
  </div>
</template>
