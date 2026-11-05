<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import API from '@/api'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import type { PreviewKind } from '@/lib/formatMediaType'
import PdfPreview from '../PdfPreview/PdfPreview.vue'

interface Props {
  /** File id — passed through to the `/files/{id}` endpoint. */
  fileId: string
  /** Shown as the dialog title. */
  filename: string
  /**
   * Which renderer to use.
   * - `'pdf'` (default): embed the file via <PdfPreview> (iframe + blob URL).
   * - `'image'`: load the file as a blob and render as an <img>.
   */
  kind?: PreviewKind
  /** Copy shown for unsupported kinds or when an image fails to load. */
  unavailableText?: string
}

const props = withDefaults(defineProps<Props>(), {
  kind: 'pdf',
  unavailableText: 'Preview is not available for this file type.',
})

const open = defineModel<boolean>('open', { default: false })

const { mutateAsync: getFile } = API.Files.useGetFile()

const imageBlobUrl = ref<string | null>(null)
const isLoadingImage = ref(false)

let currentImageRequest = 0

const releaseImage = () => {
  if (imageBlobUrl.value) {
    URL.revokeObjectURL(imageBlobUrl.value)
    imageBlobUrl.value = null
  }
}

// Load the image blob when the dialog opens for image content, and re-fire
// when fileId or kind swap mid-session. PDFs have their own blob lifecycle
// inside <PdfPreview>.
watch(
  [open, () => props.fileId, () => props.kind],
  async ([isOpen, fileId, kind]) => {
    const requestId = ++currentImageRequest
    releaseImage()
    if (!isOpen || kind !== 'image' || !fileId) return
    isLoadingImage.value = true
    try {
      const response = await getFile({ fileId, disposition: 'preview' })
      if (requestId !== currentImageRequest) return
      imageBlobUrl.value = URL.createObjectURL(new Blob([response.data]))
    } catch {
      // fall through to the unavailable message
    } finally {
      if (requestId === currentImageRequest) isLoadingImage.value = false
    }
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  currentImageRequest++
  releaseImage()
})
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-w-[90vw] w-[1200px]">
      <DialogHeader>
        <DialogTitle>{{ filename }}</DialogTitle>
      </DialogHeader>
      <div class="overflow-hidden">
        <PdfPreview
          v-if="kind === 'pdf'"
          :file-id="fileId"
          :height-vh="80"
          :title="filename"
        />
        <div v-else class="image-wrapper">
          <IniaLoader v-if="isLoadingImage" class="w-8" />
          <img
            v-else-if="imageBlobUrl"
            :src="imageBlobUrl"
            :alt="filename"
          />
          <div v-else class="empty-state">{{ unavailableText }}</div>
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.image-wrapper {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 40vh;
  background: var(--bg-0);
  border-radius: var(--radius-card);
  padding: var(--spacing-md);
}

.image-wrapper img {
  max-width: 100%;
  max-height: 80vh;
  object-fit: contain;
  border-radius: var(--radius-button);
}

.empty-state {
  padding: var(--spacing-xl);
  text-align: center;
  color: var(--text-secondary);
  font-size: 14px;
}
</style>
