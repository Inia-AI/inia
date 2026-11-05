import assert from 'node:assert/strict'
import { test } from 'node:test'

import { gate } from './audit-gate.mjs'

const advisory = (name, id, severity) => ({
  source: 1,
  name,
  dependency: name,
  title: `${name} advisory`,
  url: `https://github.com/advisories/${id}`,
  severity,
  range: '*',
})

const report = (...vulnerabilities) => ({
  vulnerabilities: Object.fromEntries(vulnerabilities.map(v => [v.name, v])),
})

const braces = {
  name: 'braces',
  severity: 'high',
  via: [advisory('braces', 'GHSA-vfj7-8cjw-p6xm', 'high')],
}
const micromatch = { name: 'micromatch', severity: 'high', via: ['braces'] }

test('an ignored advisory and the packages that inherit it pass, and stay listed', () => {
  const result = gate(report(braces, micromatch))
  assert.equal(result.ok, true)
  assert.deepEqual(
    result.ignored.map(a => a.url.split('/').pop()),
    ['GHSA-vfj7-8cjw-p6xm'],
  )
})

test('any other high or critical advisory fails', () => {
  const lodash = {
    name: 'lodash',
    severity: 'critical',
    via: [advisory('lodash', 'GHSA-aaaa-bbbb-cccc', 'critical')],
  }
  const result = gate(report(braces, micromatch, lodash))
  assert.equal(result.ok, false)
  assert.deepEqual(
    result.blocking.map(a => a.name),
    ['lodash'],
  )
})

test('a moderate advisory does not fail', () => {
  const minor = {
    name: 'minor',
    severity: 'moderate',
    via: [advisory('minor', 'GHSA-dddd-eeee-ffff', 'moderate')],
  }
  assert.equal(gate(report(minor)).ok, true)
})

test('an audit that produced no report fails', () => {
  const result = gate({
    error: { code: 'ENOAUDIT', summary: 'audit endpoint returned an error' },
  })
  assert.equal(result.ok, false)
  assert.match(result.error, /audit endpoint returned an error/)
})
