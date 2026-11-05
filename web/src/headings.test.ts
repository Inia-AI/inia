import { describe, expect, it } from 'vitest'
import {
  flattenHeadings,
  guaranteesH1,
  headingOrderProblems,
  renderTree,
  routedPages,
} from '@/test/helpers/headingOutline'

const PAGES = routedPages()

describe('page headings', () => {
  it('reads every routed page', () => {
    // The open build routes fewer pages, and the floor holds there too.
    expect(PAGES.length).toBeGreaterThan(15)
  })

  it.each(PAGES)('%s shows one h1 in every state and never skips a heading level', page => {
    const tree = renderTree(page)
    const problems = [
      ...(guaranteesH1(tree) ? [] : ['some state of the page renders no h1']),
      ...headingOrderProblems(flattenHeadings(tree)),
    ]

    expect(problems).toEqual([])
  })
})
