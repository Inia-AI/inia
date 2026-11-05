import { DEFAULT_CHAT_BORROW_HEADROOM_SLOTS, MS_PER_SECOND } from "./config.js";
import type { Narrower } from "./narrow.js";

export type AdmissionDecision =
  | { outcome: "admitted" }
  | { outcome: "shed"; retryAfterSeconds: number }
  | { outcome: "abandoned" }
  | { outcome: "disabled" };

export const ADMISSION_CLASSES = ["workflow", "chat"] as const;
export type AdmissionClass = (typeof ADMISSION_CLASSES)[number];
export const DEFAULT_ADMISSION_CLASS: AdmissionClass = "workflow";

export function parseAdmissionClass(value: unknown, parse: Narrower, label: string): AdmissionClass {
  const admissionClass = ADMISSION_CLASSES.find((candidate) => candidate === value);
  if (admissionClass === undefined) {
    parse.fail(`${label} must be one of ${ADMISSION_CLASSES.join(", ")}`);
  }
  return admissionClass;
}

export type AdmissionQueueOptions = {
  poolSize: number;
  targetWaitMs?: number;
  maxWaitMs?: number;
  sampleSize?: number;
  chatReservedSlots?: number;
  chatMaxSlots?: number;
  chatBorrowHeadroomSlots?: number;
};

export type AdmissionQueueDeps = {
  freeSlots: () => number;
  random?: () => number;
  onUnbalancedChatRelease?: () => void;
};

export type AdmissionQueue = {
  admit(input?: { abandoned?: AbortSignal; admissionClass?: AdmissionClass }): Promise<AdmissionDecision>;
  recordRunDuration(durationMs: number, admissionClass?: AdmissionClass): void;
  notifySlotFreed(): void;
  releaseChatSlot(): void;
  settleAdmission(input: { becameRun: boolean; admissionClass?: AdmissionClass }): void;
  waitingCount(admissionClass?: AdmissionClass): number;
  chatSlotsHeld(): number;
  workflowSlotsFree(): number;
  drain(): void;
  estimatedWaitMs(queuePosition: number, admissionClass?: AdmissionClass): number;
};

export const ADMISSION_WAIT_TARGET_MS = 10_000;
export const MAX_ADMISSION_WAIT_MS = 2 * ADMISSION_WAIT_TARGET_MS;
export const RUN_DURATION_SAMPLE_SIZE = 64;
export const COLD_START_RUN_DURATION_MS = 30_000;
export const MIN_ESTIMATED_RUN_DURATION_MS = 1_000;
export const RETRY_AFTER_JITTER_RATIO = 0.25;
export const MAX_RETRY_AFTER_SECONDS = 300;

const P75 = 0.75;

export type DurationEstimator = {
  record(durationMs: number): void;
  estimateMs(): number;
};

export type DurationEstimatorOptions = {
  coldStartMs: number;
  sampleSize?: number;
  floorMs?: number;
};

export function createDurationEstimator(options: DurationEstimatorOptions): DurationEstimator {
  const sampleSize = options.sampleSize ?? RUN_DURATION_SAMPLE_SIZE;
  if (!Number.isInteger(sampleSize) || sampleSize < 1) {
    throw new Error(`duration estimator sample size must be a positive integer, got ${sampleSize}`);
  }
  const floorMs = options.floorMs ?? MIN_ESTIMATED_RUN_DURATION_MS;
  const durations: number[] = [];
  let nextSampleIndex = 0;
  return {
    record(durationMs) {
      if (!Number.isFinite(durationMs) || durationMs <= 0) {
        return;
      }
      durations[nextSampleIndex] = durationMs;
      nextSampleIndex = (nextSampleIndex + 1) % sampleSize;
    },
    estimateMs() {
      if (durations.length === 0) {
        return options.coldStartMs;
      }
      const sorted = [...durations].sort((a, b) => a - b);
      const p75 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * P75))];
      return Math.max(p75 ?? options.coldStartMs, floorMs);
    },
  };
}

export function jitteredRetryAfterSeconds(estimateMs: number, random: () => number = Math.random): number {
  const jitteredMs = estimateMs * (1 + random() * RETRY_AFTER_JITTER_RATIO);
  return Math.min(MAX_RETRY_AFTER_SECONDS, Math.max(1, Math.ceil(jitteredMs / MS_PER_SECOND)));
}

type Waiter = {
  admissionClass: AdmissionClass;
  settle: (decision: AdmissionDecision) => void;
};

const ADMITTED: AdmissionDecision = { outcome: "admitted" };

export function createAdmissionQueue(
  options: AdmissionQueueOptions,
  deps: AdmissionQueueDeps,
): AdmissionQueue {
  if (!Number.isInteger(options.poolSize) || options.poolSize < 1) {
    throw new Error(`admission queue pool size must be a positive integer, got ${options.poolSize}`);
  }
  const random = deps.random ?? Math.random;
  const targetWaitMs = options.targetWaitMs ?? ADMISSION_WAIT_TARGET_MS;
  if (!Number.isFinite(targetWaitMs) || targetWaitMs < 0) {
    throw new Error(`admission target wait must be a non-negative number of ms, got ${targetWaitMs}`);
  }
  const maxWaitMs = options.maxWaitMs ?? MAX_ADMISSION_WAIT_MS;
  if (!Number.isFinite(maxWaitMs) || maxWaitMs <= 0) {
    throw new Error(`admission max wait must be a positive number of ms, got ${maxWaitMs}`);
  }
  const sampleSize = options.sampleSize ?? RUN_DURATION_SAMPLE_SIZE;
  if (!Number.isInteger(sampleSize) || sampleSize < 1) {
    throw new Error(`admission sample size must be a positive integer, got ${sampleSize}`);
  }
  const chatReservedSlots = options.chatReservedSlots ?? 0;
  const chatMaxSlots = options.chatMaxSlots ?? options.poolSize;
  if (
    !Number.isInteger(chatReservedSlots) ||
    chatReservedSlots < 0 ||
    chatReservedSlots >= options.poolSize
  ) {
    throw new Error(
      `admission chat reserve must be a whole number of slots below the pool size ${options.poolSize}, ` +
        `got ${chatReservedSlots}`,
    );
  }
  if (
    !Number.isInteger(chatMaxSlots) ||
    chatMaxSlots < chatReservedSlots ||
    chatMaxSlots > options.poolSize
  ) {
    throw new Error(
      `admission chat cap must be a whole number of slots between the reserve ${chatReservedSlots} ` +
        `and the pool size ${options.poolSize}, got ${chatMaxSlots}`,
    );
  }
  const chatBorrowHeadroomSlots =
    options.chatBorrowHeadroomSlots ?? Math.min(DEFAULT_CHAT_BORROW_HEADROOM_SLOTS, options.poolSize - 1);
  if (
    !Number.isInteger(chatBorrowHeadroomSlots) ||
    chatBorrowHeadroomSlots < 0 ||
    chatBorrowHeadroomSlots >= options.poolSize
  ) {
    throw new Error(
      `admission chat borrow headroom must be a whole number of slots below the pool size ${options.poolSize}, ` +
        `got ${chatBorrowHeadroomSlots}`,
    );
  }
  const runDurations: Record<AdmissionClass, DurationEstimator> = {
    workflow: createDurationEstimator({ coldStartMs: COLD_START_RUN_DURATION_MS, sampleSize }),
    chat: createDurationEstimator({ coldStartMs: COLD_START_RUN_DURATION_MS, sampleSize }),
  };
  // The most chat can hold under the headroom rule on an otherwise idle pool: its reserve always,
  // its cap at most, and never the last headroom slots. At least one while chat is switched on.
  const chatCapacity =
    chatMaxSlots === 0
      ? 0
      : Math.max(1, chatReservedSlots, Math.min(chatMaxSlots, options.poolSize - chatBorrowHeadroomSlots));
  const waiters: Waiter[] = [];
  let pendingAdmissions = 0;
  let chatHeld = 0;
  let draining = false;

  function waitingOf(admissionClass: AdmissionClass): number {
    return waiters.filter((waiter) => waiter.admissionClass === admissionClass).length;
  }

  function unusedChatReserve(): number {
    return Math.max(0, chatReservedSlots - chatHeld);
  }

  function freeNow(): number {
    return deps.freeSlots() - pendingAdmissions;
  }

  function workflowFree(): number {
    return freeNow() - unusedChatReserve();
  }

  function classCapacity(admissionClass: AdmissionClass): number {
    if (admissionClass === "workflow") {
      return options.poolSize - Math.max(chatReservedSlots, chatHeld);
    }
    return chatCapacity;
  }

  function estimatedWaitMs(
    queuePosition: number,
    admissionClass: AdmissionClass = DEFAULT_ADMISSION_CLASS,
  ): number {
    const capacity = classCapacity(admissionClass);
    if (capacity <= 0) {
      return Number.POSITIVE_INFINITY;
    }
    return (runDurations[admissionClass].estimateMs() * (queuePosition + 1)) / capacity;
  }

  // Chat's unused reserve is held back from workflow runs. Chat holds its reserve unconditionally;
  // past it, chat borrows only while at least the headroom stays free for workflow runs after the
  // grant, so a workflow caller coming back after its Retry-After always finds those slots, and
  // never past its cap.
  function hasCapacityFor(admissionClass: AdmissionClass): boolean {
    if (admissionClass === "workflow") {
      return workflowFree() > 0;
    }
    if (freeNow() <= 0 || chatHeld >= chatMaxSlots) {
      return false;
    }
    return chatHeld < chatReservedSlots || freeNow() - 1 >= chatBorrowHeadroomSlots;
  }

  // The headroom rule is what refuses chat: a slot is free and chat is under its cap, but borrowing
  // it would leave fewer than the headroom. Queueing cannot help until a run ends somewhere in the
  // pool, so the caller is shed at once, told how long the pool takes to free one slot.
  function headroomRefusesChat(): boolean {
    return (
      freeNow() > 0 &&
      chatHeld < chatMaxSlots &&
      chatHeld >= chatReservedSlots &&
      freeNow() - 1 < chatBorrowHeadroomSlots
    );
  }

  function slotFreeingEstimateMs(): number {
    return Math.max(targetWaitMs, runDurations.workflow.estimateMs() / options.poolSize);
  }

  function shedDecision(estimateMs: number): AdmissionDecision {
    return { outcome: "shed", retryAfterSeconds: jitteredRetryAfterSeconds(estimateMs, random) };
  }

  function grantAdmission(admissionClass: AdmissionClass): AdmissionDecision {
    pendingAdmissions += 1;
    if (admissionClass === "chat") {
      chatHeld += 1;
    }
    return ADMITTED;
  }

  function releaseChatSlot(): void {
    if (chatHeld < 1) {
      deps.onUnbalancedChatRelease?.();
    } else {
      chatHeld -= 1;
    }
  }

  // Repeats until a pass admits nobody: admitting a workflow waiter can be what frees a chat
  // waiter queued ahead of it to borrow.
  function drainAdmissions(): void {
    let admittedAny = true;
    while (admittedAny) {
      admittedAny = false;
      for (const waiter of [...waiters]) {
        if (hasCapacityFor(waiter.admissionClass)) {
          waiter.settle(grantAdmission(waiter.admissionClass));
          admittedAny = true;
        }
      }
    }
  }

  return {
    admit(input = {}) {
      const abandoned = input.abandoned;
      const admissionClass = input.admissionClass ?? DEFAULT_ADMISSION_CLASS;
      if (abandoned?.aborted === true) {
        return Promise.resolve({ outcome: "abandoned" });
      }
      if (draining) {
        return Promise.resolve(grantAdmission(admissionClass));
      }
      if (admissionClass === "chat" && chatMaxSlots === 0) {
        return Promise.resolve({ outcome: "disabled" });
      }
      drainAdmissions();
      if (waitingOf(admissionClass) === 0 && hasCapacityFor(admissionClass)) {
        const admitted = grantAdmission(admissionClass);
        drainAdmissions();
        return Promise.resolve(admitted);
      }
      if (admissionClass === "chat" && headroomRefusesChat()) {
        return Promise.resolve(shedDecision(slotFreeingEstimateMs()));
      }
      const arrivalEstimateMs = estimatedWaitMs(waitingOf(admissionClass), admissionClass);
      if (arrivalEstimateMs > targetWaitMs) {
        return Promise.resolve(shedDecision(arrivalEstimateMs));
      }
      return new Promise<AdmissionDecision>((resolve) => {
        const waiter: Waiter = {
          admissionClass,
          settle(decision) {
            const position = waiters.indexOf(waiter);
            if (position === -1) {
              return;
            }
            waiters.splice(position, 1);
            clearTimeout(sojournTimer);
            abandoned?.removeEventListener("abort", onAbandoned);
            resolve(decision);
          },
        };
        const onAbandoned = (): void => {
          waiter.settle({ outcome: "abandoned" });
        };
        // A waiter a slot has reached is admitted, not shed. Workflow keeps the target as its
        // sojourn Retry-After, exactly as before classes; chat gets its own estimate, floored at
        // the target.
        const sojournTimer = setTimeout(() => {
          if (hasCapacityFor(admissionClass)) {
            waiter.settle(grantAdmission(admissionClass));
            drainAdmissions();
            return;
          }
          if (admissionClass === "workflow") {
            waiter.settle(shedDecision(targetWaitMs));
            return;
          }
          const position = waiters
            .filter((queued) => queued.admissionClass === admissionClass)
            .indexOf(waiter);
          const estimateMs = estimatedWaitMs(Math.max(0, position), admissionClass);
          waiter.settle(shedDecision(Math.max(targetWaitMs, estimateMs)));
        }, maxWaitMs);
        sojournTimer.unref();
        abandoned?.addEventListener("abort", onAbandoned, { once: true });
        waiters.push(waiter);
      });
    },
    recordRunDuration(durationMs, admissionClass = DEFAULT_ADMISSION_CLASS) {
      runDurations[admissionClass].record(durationMs);
    },
    notifySlotFreed() {
      drainAdmissions();
    },
    releaseChatSlot() {
      releaseChatSlot();
      drainAdmissions();
    },
    settleAdmission(input) {
      if (pendingAdmissions < 1) {
        throw new Error("admission settled with no grant outstanding");
      }
      pendingAdmissions -= 1;
      if (!input.becameRun) {
        if ((input.admissionClass ?? DEFAULT_ADMISSION_CLASS) === "chat") {
          releaseChatSlot();
        }
        drainAdmissions();
      }
    },
    waitingCount: (admissionClass) =>
      admissionClass === undefined ? waiters.length : waitingOf(admissionClass),
    chatSlotsHeld: () => chatHeld,
    workflowSlotsFree: () => Math.max(0, workflowFree()),
    drain() {
      draining = true;
      while (waiters.length > 0) {
        const waiter = waiters[0];
        if (waiter === undefined) {
          break;
        }
        waiter.settle(grantAdmission(waiter.admissionClass));
      }
    },
    estimatedWaitMs,
  };
}
