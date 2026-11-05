import { describe, expect, it } from "bun:test";
import {
  ADMISSION_WAIT_TARGET_MS,
  type AdmissionClass,
  type AdmissionQueue,
  COLD_START_RUN_DURATION_MS,
  createAdmissionQueue,
  createDurationEstimator,
  jitteredRetryAfterSeconds,
  MAX_RETRY_AFTER_SECONDS,
  MIN_ESTIMATED_RUN_DURATION_MS,
} from "./admission-queue.js";

const LONG_RUN_MS = 900_000;
const FIFTEEN_MINUTES_MS_FOR_SOJOURN = 15 * 60 * 1000;

function fixedRandom(value: number): () => number {
  return () => value;
}

function recordDurations(queue: AdmissionQueue, durationMs: number, count = 8): void {
  for (let i = 0; i < count; i += 1) {
    queue.recordRunDuration(durationMs);
  }
}

describe("estimating the wait", () => {
  it("estimates from the cold-start prior when no durations are recorded", () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0 });
    expect(queue.estimatedWaitMs(0)).toBe(COLD_START_RUN_DURATION_MS / 32);
  });

  it("estimates from the p75 of recent durations, scaled by queue depth over pool size", () => {
    const queue = createAdmissionQueue({ poolSize: 4 }, { freeSlots: () => 0 });
    for (const durationMs of [8000, 1000, 5000, 3000, 7000, 2000, 6000, 4000]) {
      queue.recordRunDuration(durationMs);
    }
    expect(queue.estimatedWaitMs(0)).toBe(7000 / 4);
    expect(queue.estimatedWaitMs(3)).toBe((7000 * 4) / 4);
  });

  it("floors the estimate so a burst of instant failures cannot zero it", () => {
    const queue = createAdmissionQueue({ poolSize: 1 }, { freeSlots: () => 0 });
    recordDurations(queue, 5);
    expect(queue.estimatedWaitMs(0)).toBe(MIN_ESTIMATED_RUN_DURATION_MS);
  });

  it("keeps only the newest sampleSize durations", () => {
    const queue = createAdmissionQueue({ poolSize: 1, sampleSize: 4 }, { freeSlots: () => 0 });
    recordDurations(queue, LONG_RUN_MS, 4);
    recordDurations(queue, 2000, 4);
    expect(queue.estimatedWaitMs(0)).toBe(2000);
  });

  it("ignores durations that are not a positive finite number", () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0 });
    queue.recordRunDuration(Number.NaN);
    queue.recordRunDuration(-5);
    queue.recordRunDuration(Number.POSITIVE_INFINITY);
    expect(queue.estimatedWaitMs(0)).toBe(COLD_START_RUN_DURATION_MS / 32);
  });
});

describe("jitteredRetryAfterSeconds", () => {
  it("spreads callers upward from the estimate by up to the jitter ratio", () => {
    expect(jitteredRetryAfterSeconds(4000, fixedRandom(0))).toBe(4);
    expect(jitteredRetryAfterSeconds(4000, fixedRandom(1))).toBe(5);
  });

  it("never advises less than one second", () => {
    expect(jitteredRetryAfterSeconds(0, fixedRandom(0))).toBe(1);
    expect(jitteredRetryAfterSeconds(10, fixedRandom(0.5))).toBe(1);
  });

  it("clamps to the ceiling past which the header stops being advice", () => {
    expect(jitteredRetryAfterSeconds(10_000_000, fixedRandom(1))).toBe(MAX_RETRY_AFTER_SECONDS);
  });
});

describe("createDurationEstimator", () => {
  it("answers the cold-start prior until a duration is recorded", () => {
    const estimator = createDurationEstimator({ coldStartMs: 5000 });
    expect(estimator.estimateMs()).toBe(5000);
  });

  it("answers the p75 of recorded durations", () => {
    const estimator = createDurationEstimator({ coldStartMs: 5000 });
    for (const durationMs of [8000, 1000, 5000, 3000, 7000, 2000, 6000, 4000]) {
      estimator.record(durationMs);
    }
    expect(estimator.estimateMs()).toBe(7000);
  });

  it("floors the estimate so instant completions cannot zero it", () => {
    const estimator = createDurationEstimator({ coldStartMs: 5000 });
    estimator.record(3);
    expect(estimator.estimateMs()).toBe(MIN_ESTIMATED_RUN_DURATION_MS);
  });

  it("keeps only the newest sampleSize durations", () => {
    const estimator = createDurationEstimator({ coldStartMs: 5000, sampleSize: 4 });
    for (let i = 0; i < 4; i += 1) {
      estimator.record(900_000);
    }
    for (let i = 0; i < 4; i += 1) {
      estimator.record(2000);
    }
    expect(estimator.estimateMs()).toBe(2000);
  });

  it("ignores durations that are not a positive finite number", () => {
    const estimator = createDurationEstimator({ coldStartMs: 5000 });
    estimator.record(Number.NaN);
    estimator.record(-5);
    estimator.record(0);
    estimator.record(Number.POSITIVE_INFINITY);
    expect(estimator.estimateMs()).toBe(5000);
  });

  it("refuses a sample size that is not a positive integer rather than mis-estimating with it", () => {
    for (const bad of [0, -1, 1.5, Number.NaN]) {
      expect(() => createDurationEstimator({ coldStartMs: 5000, sampleSize: bad })).toThrow();
    }
  });
});

describe("admit or shed", () => {
  it("admits immediately when a slot is free and nobody waits", async () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 1 });
    expect(await queue.admit()).toEqual({ outcome: "admitted" });
    expect(queue.waitingCount()).toBe(0);
  });

  it("sheds immediately when the estimated wait exceeds the target, without waiting first", async () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0, random: fixedRandom(0) });
    recordDurations(queue, LONG_RUN_MS);
    const decision = await queue.admit();
    expect(decision).toEqual({ outcome: "shed", retryAfterSeconds: Math.ceil(LONG_RUN_MS / 32 / 1000) });
    expect(queue.waitingCount()).toBe(0);
  });

  it("jitters Retry-After upward from the estimate floor", async () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0, random: fixedRandom(1) });
    recordDurations(queue, LONG_RUN_MS);
    const decision = await queue.admit();
    expect(decision).toEqual({
      outcome: "shed",
      retryAfterSeconds: Math.ceil(((LONG_RUN_MS / 32) * 1.25) / 1000),
    });
  });

  it("never answers a Retry-After below one second", async () => {
    const queue = createAdmissionQueue(
      { poolSize: 1000, targetWaitMs: 0 },
      { freeSlots: () => 0, random: fixedRandom(0) },
    );
    expect(await queue.admit()).toEqual({ outcome: "shed", retryAfterSeconds: 1 });
  });

  it("counts the waiters already queued when estimating for a new arrival", async () => {
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 1500 },
      { freeSlots: () => 0, random: fixedRandom(0) },
    );
    recordDurations(queue, 4000);
    const first = queue.admit();
    expect(queue.waitingCount()).toBe(1);
    const second = await queue.admit();
    expect(second.outcome).toBe("shed");
    queue.drain();
    expect((await first).outcome).toBe("admitted");
  });

  it("sheds at the queue depth the production constants imply, with no long-run special case", async () => {
    const queue = createAdmissionQueue({ poolSize: 32 }, { freeSlots: () => 0, random: fixedRandom(0) });
    const admittedDepth = Math.ceil((ADMISSION_WAIT_TARGET_MS * 32) / COLD_START_RUN_DURATION_MS) - 1;
    const waiting = Array.from({ length: admittedDepth }, () => queue.admit());
    expect(queue.waitingCount()).toBe(admittedDepth);
    const overflow = await queue.admit();
    expect(overflow.outcome).toBe("shed");
    queue.drain();
    await Promise.all(waiting);
  });
});

describe("waiting FIFO", () => {
  it("admits waiters in arrival order as slots free", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 60_000 },
      { freeSlots: () => slots.free },
    );
    const order: string[] = [];
    const settled = ["a", "b", "c"].map((name) =>
      queue.admit().then((decision) => {
        order.push(name);
        return decision;
      }),
    );
    expect(queue.waitingCount()).toBe(3);
    slots.free = 1;
    queue.notifySlotFreed();
    await settled[0];
    expect(order).toEqual(["a"]);
    queue.settleAdmission({ becameRun: true });
    queue.notifySlotFreed();
    await settled[1];
    expect(order).toEqual(["a", "b"]);
    queue.settleAdmission({ becameRun: true });
    queue.notifySlotFreed();
    const decisions = await Promise.all(settled);
    expect(order).toEqual(["a", "b", "c"]);
    expect(decisions.map((decision) => decision.outcome)).toEqual(["admitted", "admitted", "admitted"]);
  });

  it("admits at most as many waiters as slots are free at the wake", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 60_000 },
      { freeSlots: () => slots.free },
    );
    const first = queue.admit();
    const second = queue.admit();
    slots.free = 1;
    queue.notifySlotFreed();
    expect((await first).outcome).toBe("admitted");
    expect(queue.waitingCount()).toBe(1);
    queue.drain();
    expect((await second).outcome).toBe("admitted");
  });

  it("never hands the same free slot to two admissions before the first has settled", async () => {
    const queue = createAdmissionQueue({ poolSize: 4, targetWaitMs: 60_000 }, { freeSlots: () => 1 });
    expect((await queue.admit()).outcome).toBe("admitted");
    const second = queue.admit();
    expect(queue.waitingCount()).toBe(1);
    queue.settleAdmission({ becameRun: true });
    expect(queue.waitingCount()).toBe(1);
    queue.notifySlotFreed();
    expect((await second).outcome).toBe("admitted");
  });

  it("counts pending grants at every wake, not only at arrival", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 60_000 },
      { freeSlots: () => slots.free },
    );
    const first = queue.admit();
    const second = queue.admit();
    slots.free = 1;
    queue.notifySlotFreed();
    expect((await first).outcome).toBe("admitted");
    expect(queue.waitingCount()).toBe(1);
    queue.notifySlotFreed();
    expect(queue.waitingCount()).toBe(1);
    queue.settleAdmission({ becameRun: true });
    slots.free = 0;
    queue.drain();
    expect((await second).outcome).toBe("admitted");
  });

  it("returns an unused grant's slot to the next waiter when the admission never became a run", async () => {
    const queue = createAdmissionQueue({ poolSize: 4, targetWaitMs: 60_000 }, { freeSlots: () => 1 });
    expect((await queue.admit()).outcome).toBe("admitted");
    const waiting = queue.admit();
    expect(queue.waitingCount()).toBe(1);
    queue.settleAdmission({ becameRun: false });
    expect((await waiting).outcome).toBe("admitted");
  });

  it("queues a new arrival behind a waiter its own drain just admitted, never past it", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 60_000 },
      { freeSlots: () => slots.free },
    );
    const first = queue.admit();
    slots.free = 1;
    const second = queue.admit();
    expect((await first).outcome).toBe("admitted");
    expect(queue.waitingCount()).toBe(1);
    queue.drain();
    expect((await second).outcome).toBe("admitted");
  });

  it("evicts a waiter whose connection closes and hands its turn to the next", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue(
      { poolSize: 4, targetWaitMs: 60_000 },
      { freeSlots: () => slots.free },
    );
    const abandonedController = new AbortController();
    const first = queue.admit({ abandoned: abandonedController.signal });
    const second = queue.admit();
    expect(queue.waitingCount()).toBe(2);
    abandonedController.abort();
    expect((await first).outcome).toBe("abandoned");
    expect(queue.waitingCount()).toBe(1);
    slots.free = 1;
    queue.notifySlotFreed();
    expect((await second).outcome).toBe("admitted");
  });

  it("never queues a request whose connection is already closed", async () => {
    const queue = createAdmissionQueue({ poolSize: 4 }, { freeSlots: () => 0 });
    const abandonedController = new AbortController();
    abandonedController.abort();
    expect((await queue.admit({ abandoned: abandonedController.signal })).outcome).toBe("abandoned");
    expect(queue.waitingCount()).toBe(0);
  });

  it("sheds a waiter that outlives the sojourn cap, floored at the target so the answer means something", async () => {
    const atFloor = createAdmissionQueue(
      { poolSize: 32, maxWaitMs: 20 },
      { freeSlots: () => 0, random: fixedRandom(0) },
    );
    expect(await atFloor.admit()).toEqual({
      outcome: "shed",
      retryAfterSeconds: ADMISSION_WAIT_TARGET_MS / 1000,
    });
    expect(atFloor.waitingCount()).toBe(0);
    const jittered = createAdmissionQueue(
      { poolSize: 32, maxWaitMs: 20 },
      { freeSlots: () => 0, random: fixedRandom(1) },
    );
    expect(await jittered.admit()).toEqual({
      outcome: "shed",
      retryAfterSeconds: Math.ceil((ADMISSION_WAIT_TARGET_MS * 1.25) / 1000),
    });
  });

  it("advises a workflow waiter past the sojourn cap the target exactly, however long runs have become", async () => {
    const queue = createAdmissionQueue(
      { poolSize: 32, maxWaitMs: 20 },
      { freeSlots: () => 0, random: fixedRandom(0) },
    );
    const waiting = queue.admit();
    recordDurations(queue, FIFTEEN_MINUTES_MS_FOR_SOJOURN);
    expect(await waiting).toEqual({ outcome: "shed", retryAfterSeconds: ADMISSION_WAIT_TARGET_MS / 1000 });
  });

  it("clamps Retry-After so a ring full of long parks cannot advise an hour", async () => {
    const queue = createAdmissionQueue({ poolSize: 1 }, { freeSlots: () => 0, random: fixedRandom(1) });
    recordDurations(queue, 60 * 60 * 1000);
    const decision = await queue.admit();
    expect(decision).toEqual({ outcome: "shed", retryAfterSeconds: MAX_RETRY_AFTER_SECONDS });
  });

  it("drain releases every waiter and latches the queue open, so the registry answers for shutdown", async () => {
    const queue = createAdmissionQueue({ poolSize: 4, targetWaitMs: 60_000 }, { freeSlots: () => 0 });
    const waiters = [queue.admit(), queue.admit()];
    queue.drain();
    const decisions = await Promise.all(waiters);
    expect(decisions.map((decision) => decision.outcome)).toEqual(["admitted", "admitted"]);
    expect(queue.waitingCount()).toBe(0);
    expect((await queue.admit()).outcome).toBe("admitted");
  });

  it("refuses to settle an admission when no grant is outstanding — balance is structural", () => {
    const queue = createAdmissionQueue({ poolSize: 4 }, { freeSlots: () => 1 });
    expect(() => queue.settleAdmission({ becameRun: true })).toThrow();
  });

  it("refuses a pool size that is not a positive integer", () => {
    expect(() => createAdmissionQueue({ poolSize: 0 }, { freeSlots: () => 0 })).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 2.5 }, { freeSlots: () => 0 })).toThrow();
  });

  it("refuses malformed tuning options rather than mis-scheduling with them", () => {
    const deps = { freeSlots: () => 0 };
    expect(() => createAdmissionQueue({ poolSize: 4, targetWaitMs: Number.NaN }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, targetWaitMs: -1 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, maxWaitMs: 0 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, sampleSize: 0 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, sampleSize: 2.5 }, deps)).toThrow();
  });
});

describe("admission classes", () => {
  const POOL = 32;
  const RESERVE = 4;
  const CHAT_CAP = 8;
  const HEADROOM = 4;
  const FIFTEEN_MINUTES_MS = 15 * 60 * 1000;

  function classedQueue(
    slots: { free: number },
    shape: {
      reserve?: number;
      cap?: number;
      headroom?: number;
      targetWaitMs?: number;
      maxWaitMs?: number;
    } = {},
    onUnbalancedChatRelease?: () => void,
  ): AdmissionQueue {
    return createAdmissionQueue(
      {
        poolSize: POOL,
        targetWaitMs: shape.targetWaitMs ?? 60_000,
        ...(shape.maxWaitMs === undefined ? {} : { maxWaitMs: shape.maxWaitMs }),
        chatReservedSlots: shape.reserve ?? RESERVE,
        chatMaxSlots: shape.cap ?? CHAT_CAP,
        chatBorrowHeadroomSlots: shape.headroom ?? HEADROOM,
      },
      {
        freeSlots: () => slots.free,
        random: fixedRandom(0),
        ...(onUnbalancedChatRelease === undefined ? {} : { onUnbalancedChatRelease }),
      },
    );
  }

  async function admitAndStart(
    queue: AdmissionQueue,
    slots: { free: number },
    admissionClass: AdmissionClass,
  ): Promise<void> {
    expect((await queue.admit({ admissionClass })).outcome).toBe("admitted");
    queue.settleAdmission({ becameRun: true, admissionClass });
    slots.free -= 1;
  }

  it("treats a request that names no class as a workflow run", async () => {
    const slots = { free: RESERVE };
    const queue = classedQueue(slots);
    const waiting = queue.admit();
    expect(queue.waitingCount("workflow")).toBe(1);
    expect(queue.chatSlotsHeld()).toBe(0);
    queue.drain();
    expect((await waiting).outcome).toBe("admitted");
  });

  it("keeps workflow runs out of the chat reserve while chat is idle", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    for (let i = 0; i < POOL - RESERVE; i += 1) {
      await admitAndStart(queue, slots, "workflow");
    }
    expect(slots.free).toBe(RESERVE);
    expect(queue.workflowSlotsFree()).toBe(0);
    const blocked = queue.admit({ admissionClass: "workflow" });
    expect(queue.waitingCount("workflow")).toBe(1);
    await admitAndStart(queue, slots, "chat");
    expect(queue.waitingCount("workflow")).toBe(1);
    queue.drain();
    expect((await blocked).outcome).toBe("admitted");
  });

  it("lets chat hold its reserve even when fewer than the headroom slots are free", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    for (let i = 0; i < POOL - RESERVE; i += 1) {
      await admitAndStart(queue, slots, "workflow");
    }
    for (let i = 0; i < RESERVE; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    expect(queue.chatSlotsHeld()).toBe(RESERVE);
    expect(slots.free).toBe(0);
  });

  it("lets chat borrow past its reserve while the free slots exceed the headroom", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    for (let i = 0; i < CHAT_CAP; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    expect(queue.chatSlotsHeld()).toBe(CHAT_CAP);
    expect(queue.waitingCount()).toBe(0);
    expect(queue.workflowSlotsFree()).toBe(POOL - CHAT_CAP);
  });

  it("refuses to let chat borrow a slot that would leave fewer than the headroom free", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots, { reserve: 0 });
    for (let i = 0; i < POOL - HEADROOM - 1; i += 1) {
      await admitAndStart(queue, slots, "workflow");
    }
    expect(slots.free).toBe(HEADROOM + 1);
    await admitAndStart(queue, slots, "chat");
    expect((await queue.admit({ admissionClass: "chat" })).outcome).toBe("shed");
    expect(queue.chatSlotsHeld()).toBe(1);
    expect((await queue.admit({ admissionClass: "workflow" })).outcome).toBe("admitted");
  });

  it("leaves at least the headroom free for workflow when chat floods an idle pool", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots, { reserve: 0, cap: POOL - 1 });
    for (let i = 0; i < POOL - HEADROOM; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    expect(slots.free).toBe(HEADROOM);
    expect((await queue.admit({ admissionClass: "chat" })).outcome).toBe("shed");
    expect(queue.chatSlotsHeld()).toBe(POOL - HEADROOM);
    expect((await queue.admit({ admissionClass: "workflow" })).outcome).toBe("admitted");
  });

  it("sheds chat at arrival when the headroom refuses it, advising the pool's time to free a slot", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots, { reserve: 0, targetWaitMs: ADMISSION_WAIT_TARGET_MS });
    recordDurations(queue, FIFTEEN_MINUTES_MS);
    for (let i = 0; i < POOL - HEADROOM; i += 1) {
      await admitAndStart(queue, slots, "workflow");
    }
    expect(await queue.admit({ admissionClass: "chat" })).toEqual({
      outcome: "shed",
      retryAfterSeconds: Math.ceil(FIFTEEN_MINUTES_MS / POOL / 1000),
    });
    expect(queue.waitingCount()).toBe(0);
  });

  it("lets chat borrow down to the last free slot when the headroom is zero", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots, { reserve: 0, headroom: 0 });
    for (let i = 0; i < POOL - 1; i += 1) {
      await admitAndStart(queue, slots, "workflow");
    }
    await admitAndStart(queue, slots, "chat");
    expect(slots.free).toBe(0);
    expect(queue.chatSlotsHeld()).toBe(1);
  });

  it("admits a workflow waiter a slot has reached at its sojourn cap, where develop shed it", async () => {
    const slots = { free: 0 };
    const queue = createAdmissionQueue({ poolSize: POOL, maxWaitMs: 20 }, { freeSlots: () => slots.free });
    const first = queue.admit();
    const second = queue.admit();
    slots.free = 2;
    expect((await first).outcome).toBe("admitted");
    expect((await second).outcome).toBe("admitted");
  });

  it("never lets chat past its cap even with every other slot free and nobody else waiting", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    for (let i = 0; i < CHAT_CAP; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    const overCap = queue.admit({ admissionClass: "chat" });
    expect(queue.waitingCount("chat")).toBe(1);
    queue.notifySlotFreed();
    expect(queue.waitingCount("chat")).toBe(1);
    expect(queue.chatSlotsHeld()).toBe(CHAT_CAP);
    queue.drain();
    expect((await overCap).outcome).toBe("admitted");
  });

  it("does not let a capped chat waiter at the head hold up a workflow arrival behind it", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    for (let i = 0; i < CHAT_CAP; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    const overCap = queue.admit({ admissionClass: "chat" });
    expect((await queue.admit({ admissionClass: "workflow" })).outcome).toBe("admitted");
    expect(queue.waitingCount()).toBe(1);
    queue.drain();
    await overCap;
  });

  it("returns a chat grant's slot to the class when the admission never became a run", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    expect((await queue.admit({ admissionClass: "chat" })).outcome).toBe("admitted");
    expect(queue.chatSlotsHeld()).toBe(1);
    queue.settleAdmission({ becameRun: false, admissionClass: "chat" });
    expect(queue.chatSlotsHeld()).toBe(0);
  });

  it("estimates chat on what it can hold under the headroom rule, finite even with no reserve", () => {
    const queue = createAdmissionQueue(
      { poolSize: 8, chatReservedSlots: 0, chatMaxSlots: 8, chatBorrowHeadroomSlots: 4 },
      { freeSlots: () => 0 },
    );
    for (let i = 0; i < 8; i += 1) {
      queue.recordRunDuration(2000, "chat");
    }
    expect(queue.estimatedWaitMs(0, "chat")).toBe(2000 / 4);
    const capped = classedQueue({ free: 0 });
    for (let i = 0; i < 8; i += 1) {
      capped.recordRunDuration(8000, "chat");
    }
    expect(capped.estimatedWaitMs(0, "chat")).toBe(8000 / CHAT_CAP);
  });

  it("estimates workflow on the share of the pool chat is actually holding", async () => {
    const slots = { free: POOL };
    const queue = classedQueue(slots);
    recordDurations(queue, 4000);
    expect(queue.estimatedWaitMs(0, "workflow")).toBe(4000 / (POOL - RESERVE));
    for (let i = 0; i < CHAT_CAP; i += 1) {
      await admitAndStart(queue, slots, "chat");
    }
    expect(queue.estimatedWaitMs(0, "workflow")).toBe(4000 / (POOL - CHAT_CAP));
  });

  it("sheds a chat waiter past the sojourn cap with chat's own estimate, not the target", async () => {
    const queue = classedQueue({ free: 0 }, { maxWaitMs: 20 });
    const chat = queue.admit({ admissionClass: "chat" });
    expect(queue.waitingCount("chat")).toBe(1);
    for (let i = 0; i < 8; i += 1) {
      queue.recordRunDuration(800_000, "chat");
    }
    expect(await chat).toEqual({ outcome: "shed", retryAfterSeconds: 800_000 / CHAT_CAP / 1000 });
  });

  it("admits a waiter a slot has reached at its sojourn cap, and drains the rest behind it", async () => {
    const slots = { free: 0 };
    const queue = classedQueue(slots, { maxWaitMs: 20 });
    const first = queue.admit({ admissionClass: "chat" });
    const second = queue.admit({ admissionClass: "chat" });
    expect(queue.waitingCount("chat")).toBe(2);
    slots.free = 2;
    expect((await first).outcome).toBe("admitted");
    expect((await second).outcome).toBe("admitted");
  });

  it("refuses chat as disabled, not busy, when its cap is zero", async () => {
    const queue = classedQueue({ free: POOL }, { reserve: 0, cap: 0 });
    expect(await queue.admit({ admissionClass: "chat" })).toEqual({ outcome: "disabled" });
    expect((await queue.admit({ admissionClass: "workflow" })).outcome).toBe("admitted");
  });

  it("reports and clamps a chat slot returned with none held, rather than throwing", () => {
    let reports = 0;
    const queue = classedQueue({ free: POOL }, {}, () => {
      reports += 1;
    });
    expect(() => queue.releaseChatSlot()).not.toThrow();
    expect(reports).toBe(1);
    expect(queue.chatSlotsHeld()).toBe(0);
  });

  it("refuses a chat reserve, cap or headroom that cannot schedule", () => {
    const deps = { freeSlots: () => 0 };
    expect(() => createAdmissionQueue({ poolSize: 4, chatReservedSlots: 4 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatReservedSlots: -1 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatReservedSlots: 1.5 }, deps)).toThrow();
    expect(() =>
      createAdmissionQueue({ poolSize: 4, chatReservedSlots: 2, chatMaxSlots: 1 }, deps),
    ).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatMaxSlots: -1 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatMaxSlots: 5 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatBorrowHeadroomSlots: -1 }, deps)).toThrow();
    expect(() => createAdmissionQueue({ poolSize: 4, chatBorrowHeadroomSlots: 4 }, deps)).toThrow();
  });
});
