import { yieldToEventLoop } from "./concurrency.js";

// The finally keeps a throwing `work` from leaving the competitor spinning forever and hanging the suite.
export async function countCompetitorTurns(work: () => Promise<unknown>): Promise<number> {
  let turns = 0;
  let settled = false;
  const competitor = (async () => {
    while (!settled) {
      await yieldToEventLoop();
      turns += 1;
    }
  })();
  try {
    await work();
  } finally {
    settled = true;
    await competitor;
  }
  return turns;
}
