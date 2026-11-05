<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import API from '@/api'
import { dailyAllowanceMessage } from '@/api/base/dailyAllowance'
import { useI18n } from 'vue-i18n'
import { MarkdownText } from '@/components/atoms'

const { t: $t } = useI18n()

const props = defineProps<{
  runId: string
  targetTag: string
  isOpen: boolean
  initialQuestion?: string
}>()

const emit = defineEmits<{
  (e: 'update:isOpen', value: boolean): void
}>()

const answer = ref('')
const displayedAnswer = ref('')
const loading = ref(false)
const error = ref('')
const question = ref('')
const loadingStatus = ref('')

// Handle dialog open/close state
watch(() => props.isOpen, (isOpen) => {
  if (isOpen && props.initialQuestion) {
    // Set initial question when opening
    question.value = props.initialQuestion
  } else if (!isOpen) {
    // Clear state when closing
    if (typewriterTimeout) {
      clearTimeout(typewriterTimeout)
      typewriterTimeout = null
    }
    answer.value = ''
    displayedAnswer.value = ''
    error.value = ''
    question.value = ''
    stopLoadingAnimation()
  }
})

const statusMessages = [
  'shared.tracer.statusAnalyzing',
  'shared.tracer.statusSearching',
  'shared.tracer.statusTracing',
  'shared.tracer.statusFollowing',
  'shared.tracer.statusGenerating',
]

let statusInterval: number | null = null
let typewriterTimeout: number | null = null

const startLoadingAnimation = () => {
  let index = 0
  loadingStatus.value = $t(statusMessages[0]!)

  statusInterval = window.setInterval(() => {
    index = (index + 1) % statusMessages.length
    loadingStatus.value = $t(statusMessages[index]!)
  }, 3000)
}

const stopLoadingAnimation = () => {
  if (statusInterval) {
    clearInterval(statusInterval)
    statusInterval = null
  }
  loadingStatus.value = ''
}

const typewriterEffect = (text: string) => {
  // Clear any existing typewriter timeout
  if (typewriterTimeout) {
    clearTimeout(typewriterTimeout)
    typewriterTimeout = null
  }

  displayedAnswer.value = ''
  let index = 0
  const speed = 10 // ms per batch
  const charsPerBatch = 5 // Characters to add per batch

  const type = () => {
    if (index < text.length) {
      const nextChars = text.substring(index, index + charsPerBatch)
      displayedAnswer.value += nextChars
      index += charsPerBatch
      typewriterTimeout = window.setTimeout(type, speed)
    } else {
      typewriterTimeout = null
    }
  }

  type()
}

const { mutateAsync: queryProvenance } = API.GraphRag.useQueryProvenance()

const fetchProvenance = async () => {
  if (!props.runId || !props.targetTag) {
    error.value = $t('shared.tracer.errorMissingInfo')
    return
  }

  if (!question.value.trim()) {
    error.value = $t('shared.tracer.errorEmptyQuestion')
    return
  }

  loading.value = true
  error.value = ''
  answer.value = ''
  startLoadingAnimation()

  try {
    const response = await queryProvenance({
      runId: props.runId,
      request: {
        targetTag: props.targetTag,
        question: question.value,
        startingDepth: 4,
        includePreviews: true,
      },
    })

    // Backend extracts answer from JSON inside <answer> tags and returns clean text
    answer.value = response?.answer || ''
    stopLoadingAnimation()
    typewriterEffect(answer.value)
  } catch (err: any) {
    stopLoadingAnimation()
    console.error('Provenance query failed:', err)

    const status = err.response?.status
    const errorMessage = err.response?.data?.error || err.message

    const allowanceMessage = dailyAllowanceMessage(err, 'explore', $t)

    if (allowanceMessage) {
      error.value = allowanceMessage
    } else if (status === 401) {
      error.value = $t('shared.tracer.errorUnauthorized')
    } else if (status === 403) {
      error.value = $t('shared.tracer.errorForbidden')
    } else if (status === 404) {
      error.value = $t('shared.tracer.errorNotFound')
    } else if (status === 422) {
      error.value = $t('shared.tracer.errorNoPath')
    } else {
      error.value = errorMessage || $t('shared.tracer.error')
    }
  } finally {
    loading.value = false
  }
}

const handleOpenChange = (open: boolean) => {
  emit('update:isOpen', open)
}

// Cleanup timers on component unmount to prevent memory leaks
onUnmounted(() => {
  if (statusInterval) {
    clearInterval(statusInterval)
    statusInterval = null
  }
  if (typewriterTimeout) {
    clearTimeout(typewriterTimeout)
    typewriterTimeout = null
  }
})
</script>

<template>
  <Dialog :open="isOpen" @update:open="handleOpenChange">
    <DialogContent class="tracer-dialog">
      <DialogHeader>
        <DialogTitle>{{ $t('shared.tracer.title') }}</DialogTitle>
        <DialogDescription>
          {{ $t('shared.tracer.description') }}
        </DialogDescription>
      </DialogHeader>

      <div class="tracer-content">
        <div class="question-section">
          <textarea
            id="tracer-question"
            v-model="question"
            :disabled="loading"
            :placeholder="$t('shared.tracer.questionPlaceholder')"
            class="question-input"
            rows="2"
          ></textarea>
          <Button
            @click="fetchProvenance"
            :disabled="loading || !question.trim()"
            size="sm"
            class="submit-button"
          >
            {{ loading ? $t('shared.tracer.querying') : $t('shared.tracer.askButton') }}
          </Button>
        </div>

        <div v-if="loading" class="loading-state">
          <div class="status-container">
            <div class="loading-animation">
              <div class="loading-orb"></div>
              <div class="loading-pulse"></div>
            </div>
            <p class="loading-text">{{ loadingStatus }}</p>
          </div>

          <div class="skeleton-preview">
            <div class="skeleton-line skeleton-line-1"></div>
            <div class="skeleton-line skeleton-line-2"></div>
            <div class="skeleton-line skeleton-line-3"></div>
            <div class="skeleton-line skeleton-line-4"></div>
          </div>
        </div>

        <div v-else-if="error" class="error-state">
          <p class="error-text">{{ error }}</p>
        </div>

        <div v-else-if="answer" class="answer-content">
          <MarkdownText :content="displayedAnswer" class="markdown-content" />
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.tracer-dialog {
  max-width: 500px;
  max-height: 70vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.tracer-content {
  padding: 8px 0;
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}

.question-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 12px;
}

.question-input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 13px;
  resize: vertical;
}

.question-input:focus {
  outline: none;
  border-color: #667eea;
}

.submit-button {
  align-self: flex-end;
}

.loading-state {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 0;
  min-height: 100px;
}

.status-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.loading-animation {
  position: relative;
  width: 60px;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.loading-orb {
  width: 34px;
  height: 34px;
  background: var(--primary);
  border-radius: 50%;
  box-shadow: 0 0 16px rgba(26, 56, 80, 0.6);
  animation: pulse-glow 2s ease-in-out infinite;
  z-index: 2;
  position: relative;
}

.loading-pulse {
  position: absolute;
  width: 60px;
  height: 60px;
  border: 2px solid rgba(26, 56, 80, 0.4);
  border-radius: 50%;
  animation: pulse-ring 2s ease-out infinite;
}

@keyframes pulse-glow {
  0%, 100% {
    transform: scale(1);
    box-shadow: 0 0 20px rgba(26, 56, 80, 0.6);
  }
  50% {
    transform: scale(1.1);
    box-shadow: 0 0 30px rgba(26, 56, 80, 0.8), 0 0 60px rgba(26, 56, 80, 0.4);
  }
}

@keyframes pulse-ring {
  0% {
    transform: scale(0.5);
    opacity: 1;
  }
  100% {
    transform: scale(1.2);
    opacity: 0;
  }
}

.loading-text {
  color: var(--text-secondary);
  font-size: 12px;
  font-weight: 500;
  animation: fade-in-out 2s ease-in-out infinite;
}

@keyframes fade-in-out {
  0%, 100% {
    opacity: 0.6;
  }
  50% {
    opacity: 1;
  }
}

.skeleton-preview {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px;
  background: var(--bg-1);
  border-radius: 4px;
}

.skeleton-line {
  height: 12px;
  background: linear-gradient(
    90deg,
    rgba(200, 200, 200, 0.2) 0%,
    rgba(200, 200, 200, 0.3) 50%,
    rgba(200, 200, 200, 0.2) 100%
  );
  background-size: 200% 100%;
  border-radius: 3px;
  animation: skeleton-loading 1.5s ease-in-out infinite;
}

.skeleton-line-1 {
  width: 85%;
}

.skeleton-line-2 {
  width: 95%;
}

.skeleton-line-3 {
  width: 75%;
}

.skeleton-line-4 {
  width: 90%;
}

@keyframes skeleton-loading {
  0% {
    background-position: 200% 0;
  }
  100% {
    background-position: -200% 0;
  }
}

.error-state {
  padding: 8px;
  background: var(--bg-1);
  border: 1px solid var(--accent-error);
  border-radius: 4px;
}

.error-text {
  color: var(--accent-error);
  font-size: 13px;
  line-height: 1.4;
}

.answer-content {
  padding: 10px;
  background: var(--bg-1);
  border-radius: 4px;
  line-height: 1.5;
  position: relative;
  animation: fade-in 0.3s ease-in;
  max-height: calc(70vh - 180px);
  overflow-y: auto;
}

@keyframes fade-in {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.markdown-content {
  font-size: 13px;
}

@media (max-width: 768px) {
  .tracer-dialog {
    max-width: 100%;
  }
}
</style>
