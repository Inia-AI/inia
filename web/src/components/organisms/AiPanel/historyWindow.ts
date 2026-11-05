import type { RefinementTurn } from '@/api/contentRefinement/schema'

// Backend caps conversationHistory at 20 turns. We slide a window of `maxPairs`
// user+assistant pairs (default 8 → 16 turns) so a long-running refine session
// never hits that cap and so the model never sees an unbounded run of prior
// rejections it can pattern-match.
export const appendPairWithWindow = (
  history: RefinementTurn[],
  userText: string,
  assistantText: string,
  maxPairs: number,
): void => {
  history.push(
    { role: 'user', content: userText },
    { role: 'assistant', content: assistantText },
  )
  const maxTurns = maxPairs * 2
  if (history.length > maxTurns) {
    history.splice(0, history.length - maxTurns)
  }
}
