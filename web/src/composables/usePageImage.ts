import axios from 'axios'
import { onUnmounted, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import api from '@/api'

/**
 * Loads the image the platform renders for a page while `active` holds. For a file type with no
 * page image the platform answers with the page's text instead, which `text` carries.
 */
export function usePageImage(pageId: Ref<string | null>, active: Ref<boolean>) {
  const { t: $t } = useI18n()

  const loading = ref(false)
  const error = ref<string | null>(null)
  const imageUrl = ref<string | null>(null)
  const text = ref<string | null>(null)

  const releaseImage = () => {
    if (imageUrl.value) {
      URL.revokeObjectURL(imageUrl.value)
    }
    imageUrl.value = null
  }

  const errorMessageFor = (failure: unknown) => {
    if (axios.isAxiosError(failure) && failure.response?.status === 403) {
      return $t('shared.citations.errors.noAccess')
    }
    if (axios.isAxiosError(failure) && failure.response?.status === 404) {
      return $t('shared.citations.errors.notAvailable')
    }
    return $t('shared.citations.errors.loadPreviewFailed')
  }

  let latestRequest = 0

  const load = async (id: string) => {
    const request = ++latestRequest
    const isCurrent = () => request === latestRequest && active.value

    releaseImage()
    text.value = null
    error.value = null
    loading.value = true

    try {
      const { blob, contentType } = await api.Pages.service.getPageImage(id)
      if (!isCurrent()) {
        return
      }

      if (contentType.startsWith('image/')) {
        imageUrl.value = URL.createObjectURL(blob)
      } else if (contentType.startsWith('text/')) {
        const pageText = await blob.text()
        if (isCurrent()) {
          text.value = pageText
        }
      } else {
        error.value = $t('shared.citations.errors.unsupportedContentType')
      }
    } catch (failure) {
      if (isCurrent()) {
        error.value = errorMessageFor(failure)
      }
    } finally {
      if (request === latestRequest) {
        loading.value = false
      }
    }
  }

  watch(
    () => [active.value, pageId.value] as const,
    ([isActive, id]) => {
      if (isActive && id) {
        void load(id)
      } else if (!isActive) {
        latestRequest++
        releaseImage()
      }
    },
    { immediate: true },
  )

  onUnmounted(releaseImage)

  return { loading, error, imageUrl, text }
}
