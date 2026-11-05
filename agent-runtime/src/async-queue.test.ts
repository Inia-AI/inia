import { describe, expect, it } from "bun:test";
import { createAsyncQueue } from "./async-queue.js";

describe("createAsyncQueue", () => {
  it("delivers already-pushed values in order", async () => {
    const queue = createAsyncQueue<number>();
    queue.push(1);
    queue.push(2);
    expect(await queue.next()).toBe(1);
    expect(await queue.next()).toBe(2);
  });

  it("resolves a consumer that was already waiting when the value arrives", async () => {
    const queue = createAsyncQueue<string>();
    const pending = queue.next();
    queue.push("late");
    expect(await pending).toBe("late");
  });

  it("serves concurrent consumers one value each, in order", async () => {
    const queue = createAsyncQueue<number>();
    const first = queue.next();
    const second = queue.next();
    queue.push(1);
    queue.push(2);
    expect(await first).toBe(1);
    expect(await second).toBe(2);
  });

  it("rejects waiting and future consumers once failed", async () => {
    const queue = createAsyncQueue<number>();
    const pending = queue.next();
    queue.fail(new Error("stream broke"));
    await expect(pending).rejects.toThrow(/stream broke/);
    await expect(queue.next()).rejects.toThrow(/stream broke/);
  });

  it("drains buffered values before reporting a failure", async () => {
    const queue = createAsyncQueue<number>();
    queue.push(1);
    queue.fail(new Error("late failure"));
    expect(await queue.next()).toBe(1);
    await expect(queue.next()).rejects.toThrow(/late failure/);
  });
});
