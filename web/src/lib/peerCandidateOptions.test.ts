import { describe, it, expect } from 'vitest'
import {
  peerCandidateOptions,
  withRememberedPeerOptions,
} from './peerCandidateOptions'
import type { PeerCandidate } from '@/api/peerCandidates/schema'

const translate = (key: string, params?: Record<string, unknown>) =>
  params ? `${key} ${JSON.stringify(params)}` : key

const candidate = (overrides: Partial<PeerCandidate> = {}): PeerCandidate => ({
  id: 'c-1',
  name: 'Acme Holdings',
  nameVariants: '',
  isEligible: true,
  availability: 'Available',
  ...overrides,
})

const optionsFor = (candidates: PeerCandidate[], excludeId?: string) =>
  peerCandidateOptions(candidates, { excludeId, t: translate })

describe('peerCandidateOptions', () => {
  it('offers a usable candidate with no reason against it', () => {
    const [option] = optionsFor([candidate()])

    expect(option).toMatchObject({ label: 'Acme Holdings', value: 'c-1' })
    expect(option!.unavailableReason).toBeUndefined()
  })

  it.each([
    ['NotAnalysed', 'shared.components.peerCompanySelect.notAnalysed'],
    ['Unreadable', 'shared.components.peerCompanySelect.unreadable'],
    ['NoUsablePeriods', 'shared.components.peerCompanySelect.noUsablePeriods'],
  ])('explains %s in the user’s terms', (availability, expectedKey) => {
    const [option] = optionsFor([
      candidate({ isEligible: false, availability }),
    ])

    expect(option!.unavailableReason).toBe(expectedKey)
  })

  it('still explains an availability the client does not recognise', () => {
    const [option] = optionsFor([
      candidate({ isEligible: false, availability: 'SomethingNewFromTheServer' }),
    ])

    expect(option!.unavailableReason).toBe(
      'shared.components.peerCompanySelect.unavailable',
    )
  })

  it('never names the raw availability value in the text it renders', () => {
    const [option] = optionsFor([
      candidate({ isEligible: false, availability: 'NoUsablePeriods' }),
    ])

    expect(option!.unavailableReason).not.toContain('NoUsablePeriods')
  })

  it('treats an ineligible candidate as unusable even when its availability reads Available', () => {
    const [option] = optionsFor([
      candidate({ isEligible: false, availability: 'Available' }),
    ])

    expect(option!.unavailableReason).toBe(
      'shared.components.peerCompanySelect.unavailable',
    )
  })

  it('reports how current a usable candidate’s figures are', () => {
    const [option] = optionsFor([
      candidate({ latestPeriodEnd: '2025-03-31T00:00:00' }),
    ])

    expect(option!.figures).toBe(
      'shared.components.peerCompanySelect.figuresTo {"date":"Mar 2025"}',
    )
  })

  it('names the currency the stored figures are in when it is known', () => {
    const [option] = optionsFor([
      candidate({ latestPeriodEnd: '2025-03-31T00:00:00', currency: 'USD' }),
    ])

    expect(option!.figures).toBe(
      'shared.components.peerCompanySelect.figuresToInCurrency {"date":"Mar 2025","currency":"USD"}',
    )
  })

  it('reports no period for a candidate that has none', () => {
    const [option] = optionsFor([candidate({ latestPeriodEnd: null })])

    expect(option!.figures).toBeUndefined()
  })

  it('reports no period for a candidate that cannot contribute one', () => {
    const [option] = optionsFor([
      candidate({
        isEligible: false,
        availability: 'NotAnalysed',
        latestPeriodEnd: '2025-03-31T00:00:00',
      }),
    ])

    expect(option!.figures).toBeUndefined()
  })

  it('shows the name the report will carry when it differs from the local record', () => {
    const [option] = optionsFor([
      candidate({ reportedName: 'ACME HOLDINGS PLC' }),
    ])

    expect(option!.reportedAs).toBe(
      'shared.components.peerCompanySelect.reportedAs {"name":"ACME HOLDINGS PLC"}',
    )
  })

  it('stays quiet when the reported name matches the local record', () => {
    const [option] = optionsFor([candidate({ reportedName: 'Acme Holdings' })])

    expect(option!.reportedAs).toBeUndefined()
  })

  it('lets the search match name variants and the reported name', () => {
    const [option] = optionsFor([
      candidate({ nameVariants: 'Acme Corp;ACME', reportedName: 'ACME HOLDINGS PLC' }),
    ])

    expect(option!.searchText).toContain('Acme Holdings')
    expect(option!.searchText).toContain('Acme Corp;ACME')
    expect(option!.searchText).toContain('ACME HOLDINGS PLC')
  })

  it('does not offer the target counterparty as its own peer', () => {
    const options = optionsFor(
      [candidate({ id: 'target' }), candidate({ id: 'other' })],
      'target',
    )

    expect(options.map(o => o.value)).toEqual(['other'])
  })

  it('offers nothing when there are no candidates', () => {
    expect(peerCandidateOptions(undefined, { t: translate })).toEqual([])
  })
})

describe('withRememberedPeerOptions', () => {
  const acme = { label: 'Acme', value: 'c-1' }
  const globex = { label: 'Globex', value: 'c-2' }

  it('keeps a selected peer visible after the search moved past it', () => {
    const result = withRememberedPeerOptions([globex], [acme], ['c-1'])

    expect(result.map(o => o.value)).toEqual(['c-2', 'c-1'])
  })

  it('does not re-add a remembered peer that is no longer selected', () => {
    const result = withRememberedPeerOptions([globex], [acme], [])

    expect(result.map(o => o.value)).toEqual(['c-2'])
  })

  it('prefers the fresh candidate over the remembered copy', () => {
    const renamed = { label: 'Acme Renamed', value: 'c-1' }

    const result = withRememberedPeerOptions([renamed], [acme], ['c-1'])

    expect(result).toEqual([renamed])
  })

  it('never re-adds the target counterparty as its own peer', () => {
    const result = withRememberedPeerOptions([globex], [acme], ['c-1'], 'c-1')

    expect(result.map(o => o.value)).toEqual(['c-2'])
  })
})
