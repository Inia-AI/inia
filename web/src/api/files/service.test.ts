import { describe, it, expect } from 'vitest'
import Service from './service'

describe('uploadEndpoint', () => {
  it('resolves a root-relative base against the page origin', () => {
    const service = new Service({ base: '/', withCredentials: false })
    expect(service.uploadEndpoint('http://intranet-host:8080')).toBe(
      'http://intranet-host:8080/api/v1/files',
    )
  })

  it('resolves an absolute base regardless of the page origin', () => {
    const service = new Service({ base: 'https://api.example.com', withCredentials: false })
    expect(service.uploadEndpoint('http://app.example.com')).toBe(
      'https://api.example.com/api/v1/files',
    )
  })

  it('keeps a path prefix, matching how axios joins baseURL and request paths', () => {
    const service = new Service({ base: 'https://example.com/platform', withCredentials: false })
    expect(service.uploadEndpoint('http://app.example.com')).toBe(
      'https://example.com/platform/api/v1/files',
    )
  })
})
