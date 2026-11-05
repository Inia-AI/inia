// One process multiplexes every run's wire frames, timers, and /healthz onto one event loop.
export function yieldToEventLoop(): Promise<void> {
  return new Promise<void>((resolve) => {
    setImmediate(resolve);
  });
}

export async function mapWithConcurrency<T, R>(
  items: readonly T[],
  limit: number,
  fn: (item: T) => Promise<R>,
): Promise<R[]> {
  const results = new Array<R>(items.length);
  const queue = items.map((item, index) => ({ item, index }));
  let cursor = 0;
  const worker = async (): Promise<void> => {
    while (cursor < queue.length) {
      const next = queue[cursor];
      cursor += 1;
      if (next === undefined) {
        return;
      }
      results[next.index] = await fn(next.item);
    }
  };
  const workerCount = Math.max(1, Math.min(limit, items.length));
  await Promise.all(Array.from({ length: workerCount }, worker));
  return results;
}
