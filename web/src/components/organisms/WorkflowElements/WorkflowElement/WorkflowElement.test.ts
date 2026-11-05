import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import WorkflowElement from './WorkflowElement.vue'

describe('WorkflowElement actions', () => {
  it('gives the heading its own edit action, apart from the paragraph edit', () => {
    const wrapper = mount(WorkflowElement, {
      props: {
        sectionId: 's1',
        executionId: 'e1',
        element: { id: 'p1', subtitle: 'Revenue', content: 'Revenue grew.' },
      },
      global: { stubs: { WorkflowContent: true, DynamicChartRenderer: true } },
    })

    const [heading, paragraph] = wrapper.findAllComponents({ name: 'WorkflowContent' })
    expect(heading!.props('actions')).toEqual(['editHeading'])
    expect(paragraph!.props('actions')).toContain('edit')
    expect(paragraph!.props('actions')).not.toContain('editHeading')
  })
})
