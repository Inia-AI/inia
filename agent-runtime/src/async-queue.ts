export type AsyncQueue<T> = {
  push(value: T): void;
  fail(error: Error): void;
  next(): Promise<T>;
};

export function createAsyncQueue<T>(): AsyncQueue<T> {
  const buffered: T[] = [];
  const waiters: { resolve: (value: T) => void; reject: (error: Error) => void }[] = [];
  let failure: Error | undefined;

  return {
    push(value) {
      const waiter = waiters.shift();
      if (waiter !== undefined) {
        waiter.resolve(value);
        return;
      }
      buffered.push(value);
    },
    fail(error) {
      failure = error;
      for (const waiter of waiters.splice(0)) {
        waiter.reject(error);
      }
    },
    next() {
      const value = buffered.shift();
      if (value !== undefined) {
        return Promise.resolve(value);
      }
      if (failure !== undefined) {
        return Promise.reject(failure);
      }
      return new Promise<T>((resolve, reject) => {
        waiters.push({ resolve, reject });
      });
    },
  };
}
