import { encode } from 'uqr'
import { describe, expect, it } from 'vitest'

import { qrCodeImageSource } from './qrCode'

const authenticatorUri = (secret: string) =>
  `otpauth://totp/Inia%20AI:jane.doe%40example.com?secret=${secret}&issuer=Inia%20AI`

describe('qrCodeImageSource', () => {
  it('draws the code as an inline image, so the secret never leaves the page', async () => {
    const source = await qrCodeImageSource(authenticatorUri('JBSWY3DPEHPK3PXP'))

    expect(source.startsWith('data:image/svg+xml,')).toBe(true)
    expect(decodeURIComponent(source)).toContain('<svg')
    expect(decodeURIComponent(source)).not.toContain('JBSWY3DPEHPK3PXP')
  })

  it('keeps the four-module quiet zone the QR standard asks for, so a phone can read it on the dark dialog', async () => {
    const uri = authenticatorUri('JBSWY3DPEHPK3PXP')
    const modules = encode(uri, { border: 0 }).size
    const [, width] = decodeURIComponent(await qrCodeImageSource(uri)).match(/viewBox="0 0 (\d+) \d+"/) ?? []

    expect(Number(width)).toBe((modules + 8) * 10)
  })

  it('draws a different code for a different secret', async () => {
    expect(await qrCodeImageSource(authenticatorUri('JBSWY3DPEHPK3PXP'))).not.toBe(
      await qrCodeImageSource(authenticatorUri('KRSXG5CTMVRXEZLU')),
    )
  })
})
