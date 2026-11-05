<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import API from '@/api'
import { IniaLoader } from '@/components/atoms'

interface Props {
  fileId: string
  heightVh?: number
  /** Accessibility title for the iframe — consumers should pass a localized value. */
  title?: string
}

const props = withDefaults(defineProps<Props>(), {
  heightVh: 80,
  title: 'Preview',
})

const { mutateAsync: getFile } = API.Files.useGetFile()

const blobUrl = ref<string | null>(null)
const isLoading = ref(false)
const error = ref<string | null>(null)

let currentRequestId = 0

const release = () => {
  if (blobUrl.value) {
    URL.revokeObjectURL(blobUrl.value)
    blobUrl.value = null
  }
}

const load = async (fileId: string) => {
  const requestId = ++currentRequestId
  release()
  if (!fileId) return
  isLoading.value = true
  error.value = null
  try {
    const response = await getFile({ fileId, disposition: 'preview' })
    if (requestId !== currentRequestId) return
    const blob = new Blob([response.data], { type: 'application/pdf' })
    blobUrl.value = URL.createObjectURL(blob)
  } catch (e) {
    if (requestId !== currentRequestId) return
    error.value = e instanceof Error ? e.message : 'Failed to load preview'
  } finally {
    if (requestId === currentRequestId) isLoading.value = false
  }
}

watch(() => props.fileId, load, { immediate: true })

onBeforeUnmount(() => {
  currentRequestId++
  release()
})
</script>

<template>
  <div class="pdf-preview" :style="{ height: `${heightVh}vh` }">
    <div v-if="isLoading" class="pdf-preview-state">
      <IniaLoader class="w-8" />
    </div>
    <div v-else-if="error" class="pdf-preview-state pdf-preview-error">
      {{ error }}
    </div>
    <iframe
      v-else-if="blobUrl"
      :src="blobUrl"
      class="pdf-preview-frame"
      :title="title"
    />
  </div>
</template>

<style scoped>
.pdf-preview {
  position: relative;
  width: 100%;
  background: var(--bg-0);
  border-radius: var(--radius-card);
  overflow: hidden;
}

.pdf-preview-frame {
  width: 100%;
  height: 100%;
  border: none;
  display: block;
}

.pdf-preview-state {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 14px;
}

.pdf-preview-error {
  color: var(--accent-error);
}
</style>
