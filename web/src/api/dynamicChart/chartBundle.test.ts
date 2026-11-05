import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { runInNewContext } from 'node:vm'

const bundle = readFileSync(resolve(process.cwd(), 'chart-builder/charts.global.js'), 'utf8')

describe('the chart bundle the Word export loads', () => {
  it('loads in a page with no build tooling and exposes drawForExport', () => {
    const page: Record<string, unknown> = { navigator: { userAgent: 'Chrome' } }

    runInNewContext(bundle, page)

    expect(typeof (page.IniaCharts as { drawForExport?: unknown } | undefined)?.drawForExport).toBe('function')
  })

  it('opens with the notice that names the code it embeds', () => {
    expect(bundle.startsWith('/*!')).toBe(true)
    expect(bundle).toContain('Apache ECharts')
    expect(bundle).toContain('zrender')
  })
})
