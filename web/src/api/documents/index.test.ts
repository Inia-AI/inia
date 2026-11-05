import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import Documents from './index'
import type { DocumentDetail, DocumentSummary } from './schema'

describe('Documents.useProcessDocument', () => {
  it('stops the cached document reading as processed the moment it is queued again', async () => {
    const queryClient = new QueryClient()
    queryClient.setQueryData(['documents', 'detail', 'doc-1'], { id: 'doc-1', processingState: 'Processed', processingError: null } as DocumentDetail)
    const documents = new Documents()
    vi.spyOn(documents.service, 'processDocument').mockResolvedValue({ id: 'doc-1', processingState: 'Queued', processingError: null } as DocumentSummary)
    let process: ((id: string) => Promise<unknown>) | undefined
    mount(
      defineComponent({
        setup() {
          process = documents.useProcessDocument().mutateAsync
          return () => null
        },
      }),
      { global: { plugins: [[VueQueryPlugin, { queryClient }]] } },
    )

    await process!('doc-1')

    expect(queryClient.getQueryData<DocumentDetail>(['documents', 'detail', 'doc-1'])?.processingState).toBe('Queued')
  })
})
