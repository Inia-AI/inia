export type OptionType = {
  label: string
  value: string
  /**
   * Optional override for the text the search input matches against. Useful when
   * the visible label is short ("Jane Smith") but you want extra tokens (e.g. an
   * email) to match. Falls back to `label` when omitted.
   */
  searchText?: string
}

export type PeerOptionType = OptionType & {
  unavailableReason?: string
  figures?: string
  reportedAs?: string
}
