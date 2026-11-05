<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useForm, Field as FormField } from 'vee-validate'
import { z } from 'zod'

import { ForgotPasswordRequest } from '@/api/schema'

import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const $emits = defineEmits(['submit'])

const formData = ref<ForgotPasswordRequest>({
  email: '',
})

const schema = computed(() =>
  z.object({
    email: z.string().email(),
  }),
)

const form = useForm<ForgotPasswordRequest>({
  validationSchema: schema,
  initialValues: formData.value,
})

const handleSubmit = form.handleSubmit(async data => {
  $emits('submit', data)
})
</script>

<template>
  <div class="w-full">
    <form @submit="handleSubmit" class="space-y-4">
      <FormField v-slot="{ componentField }" name="email">
        <FormItem>
          <FormLabel>Email</FormLabel>
          <FormControl>
            <Input
              autocomplete
              v-bind="componentField"
              v-model="formData.email"
              placeholder="Email"
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      </FormField>

      <Button type="submit" variant="soft">
        {{ $t('page.forgotPassword.form.submitLabel') }}
      </Button>
    </form>
  </div>
</template>
