<script lang="ts" setup>
import { computed, type PropType, ref, watch } from 'vue'
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'
import { useI18n } from 'vue-i18n'

import type { CreateDocumentCategoryRequest } from '@/api/documentCategories/schema'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

interface FormData {
  name: string
  displayOrder: number
}

const { t: $t } = useI18n()

const $emits = defineEmits(['submit', 'cancel'])

const props = defineProps({
  initialValues: {
    type: Object as PropType<FormData | null>,
    required: false,
  },
})

const defaultValues: FormData = {
  name: '',
  displayOrder: 0,
}

const formData = ref<FormData>({
  ...defaultValues,
  ...(props.initialValues ?? {}),
})

const isEditMode = computed(() => !!props.initialValues?.name)

const schema = computed(() =>
  z.object({
    name: z.string().min(1, 'Name is required').max(200, 'Name must not exceed 200 characters'),
    displayOrder: z.number().int().min(0, 'Display order must be a non-negative number'),
  }),
)

const form = useForm<FormData>({
  validationSchema: schema,
  initialValues: formData.value,
})

const handleSubmit = form.handleSubmit(data => {
  const submitData: CreateDocumentCategoryRequest = {
    name: data.name,
    displayOrder: data.displayOrder,
  }
  $emits('submit', submitData)
})

watch(
  () => props.initialValues,
  newValues => {
    Object.assign(formData.value, {
      ...defaultValues,
      ...(newValues ?? {}),
    })
    form.resetForm({ values: formData.value })
  },
  { immediate: true },
)
</script>

<template>
  <form @submit="handleSubmit" class="flex flex-col gap-4">
    <FormField v-slot="{ componentField }" name="name">
      <FormItem>
        <FormLabel>{{ $t('page.documentCategoryManagement.form.nameLabel') }}</FormLabel>
        <FormControl>
          <Input
            v-bind="componentField"
            v-model="formData.name"
            :placeholder="$t('page.documentCategoryManagement.form.namePlaceholder')"
          />
        </FormControl>
        <FormMessage />
      </FormItem>
    </FormField>

    <FormField v-slot="{ componentField }" name="displayOrder">
      <FormItem>
        <FormLabel>{{ $t('page.documentCategoryManagement.form.displayOrderLabel') }}</FormLabel>
        <FormControl>
          <Input
            v-bind="componentField"
            v-model.number="formData.displayOrder"
            type="number"
            min="0"
            :placeholder="$t('page.documentCategoryManagement.form.displayOrderPlaceholder')"
          />
        </FormControl>
        <FormMessage />
      </FormItem>
    </FormField>

    <div class="flex justify-end gap-4 mt-4">
      <Button variant="outline" type="button" @click="$emit('cancel')">
        {{ $t('page.documentCategoryManagement.form.cancelLabel') }}
      </Button>
      <Button type="submit" variant="soft">
        {{ isEditMode ? $t('page.documentCategoryManagement.form.updateLabel') : $t('page.documentCategoryManagement.form.submitLabel') }}
      </Button>
    </div>
  </form>
</template>
