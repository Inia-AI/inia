const QUIET_ZONE_MODULES = 4

export const qrCodeImageSource = async (text: string): Promise<string> => {
  const { renderSVG } = await import('uqr')
  return `data:image/svg+xml,${encodeURIComponent(renderSVG(text, { border: QUIET_ZONE_MODULES }))}`
}
