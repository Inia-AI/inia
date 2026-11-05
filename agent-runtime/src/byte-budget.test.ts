import { describe, expect, it } from "bun:test";
import { createByteBudget, instrumentByteBudget } from "./byte-budget.js";
import type { Logger } from "./logger.js";

describe("createByteBudget", () => {
  it("admits reservations up to the ceiling and refuses the one that would cross it", () => {
    const budget = createByteBudget(100);
    expect(budget.tryReserve(60)).toBeDefined();
    expect(budget.tryReserve(40)).toBeDefined();
    expect(budget.tryReserve(1)).toBeUndefined();
    expect(budget.reservedBytes()).toBe(100);
  });

  it("admits again once a reservation is released", () => {
    const budget = createByteBudget(100);
    const first = budget.tryReserve(100);
    expect(first).toBeDefined();
    expect(budget.tryReserve(100)).toBeUndefined();
    first?.release();
    expect(budget.reservedBytes()).toBe(0);
    expect(budget.tryReserve(100)).toBeDefined();
  });

  it("keeps each handle's charge separate and readable", () => {
    const budget = createByteBudget(100);
    const first = budget.tryReserve(60);
    const second = budget.tryReserve(30);
    expect(first?.bytes).toBe(60);
    expect(second?.bytes).toBe(30);
    expect(budget.reservedBytes()).toBe(90);
  });

  it("settles a charge down, releasing exactly the difference", () => {
    const budget = createByteBudget(100);
    const handle = budget.tryReserve(80);
    handle?.settleTo(30);
    expect(handle?.bytes).toBe(30);
    expect(budget.reservedBytes()).toBe(30);
    expect(budget.tryReserve(70)).toBeDefined();
  });

  it("settles a charge up past the ceiling when the resident bytes demand it", () => {
    const budget = createByteBudget(100);
    const handle = budget.tryReserve(40);
    handle?.settleTo(120);
    expect(handle?.bytes).toBe(120);
    expect(budget.reservedBytes()).toBe(120);
    expect(budget.tryReserve(1)).toBeUndefined();
    handle?.release();
    expect(budget.reservedBytes()).toBe(0);
    expect(budget.tryReserve(100)).toBeDefined();
  });

  it("makes a released handle inert: release and settle are no-ops on it", () => {
    const budget = createByteBudget(100);
    const stale = budget.tryReserve(50);
    stale?.release();
    const successor = budget.tryReserve(100);
    expect(successor).toBeDefined();
    stale?.release();
    stale?.settleTo(0);
    expect(stale?.live).toBe(false);
    expect(successor?.live).toBe(true);
    expect(budget.reservedBytes()).toBe(100);
  });

  it("grows a charge up to the ceiling and refuses the growth that would cross it", () => {
    const budget = createByteBudget(100);
    const neighbour = budget.tryReserve(30);
    const handle = budget.tryReserve(40);
    expect(handle?.tryGrowTo(70)).toBe(true);
    expect(handle?.bytes).toBe(70);
    expect(budget.reservedBytes()).toBe(100);
    expect(handle?.tryGrowTo(71)).toBe(false);
    expect(handle?.bytes).toBe(70);
    expect(budget.reservedBytes()).toBe(100);
    expect(neighbour?.live).toBe(true);
  });

  it("treats growth to at or below the current charge as already granted", () => {
    const budget = createByteBudget(100);
    const handle = budget.tryReserve(40);
    expect(handle?.tryGrowTo(10)).toBe(true);
    expect(handle?.tryGrowTo(40)).toBe(true);
    expect(handle?.bytes).toBe(40);
    expect(budget.reservedBytes()).toBe(40);
  });

  it("refuses growth on a released handle without touching the budget", () => {
    const budget = createByteBudget(100);
    const stale = budget.tryReserve(40);
    stale?.release();
    const successor = budget.tryReserve(60);
    expect(stale?.tryGrowTo(80)).toBe(false);
    expect(successor?.live).toBe(true);
    expect(budget.reservedBytes()).toBe(60);
  });
});

describe("instrumentByteBudget", () => {
  function collectingLogger(): {
    events: { level: string; event: string; fields: Record<string, unknown> }[];
    logger: Logger;
  } {
    const events: { level: string; event: string; fields: Record<string, unknown> }[] = [];
    const boundLogger = (bound: Record<string, unknown>): Logger => ({
      debug: (event, fields = {}) => events.push({ level: "debug", event, fields: { ...bound, ...fields } }),
      info: (event, fields = {}) => events.push({ level: "info", event, fields: { ...bound, ...fields } }),
      warn: (event, fields = {}) => events.push({ level: "warn", event, fields: { ...bound, ...fields } }),
      error: (event, fields = {}) => events.push({ level: "error", event, fields: { ...bound, ...fields } }),
      with: (fields) => boundLogger({ ...bound, ...fields }),
    });
    return { events, logger: boundLogger({}) };
  }

  it("carries the caller's context fields on the reserve and release lines", () => {
    const { events, logger } = collectingLogger();
    const budget = instrumentByteBudget(createByteBudget(100), "inflight_upload", logger);
    const handle = budget.tryReserve(40, { path: "/v1/runs" });
    handle?.release();
    const reserved = events.find((entry) => entry.event === "budget_reserved");
    expect(reserved?.fields["path"]).toBe("/v1/runs");
    const released = events.find((entry) => entry.event === "budget_released");
    expect(released?.fields["path"]).toBe("/v1/runs");
  });

  it("carries fields tagged after the reserve on every later line for that reservation", () => {
    const { events, logger } = collectingLogger();
    const budget = instrumentByteBudget(createByteBudget(100), "inflight_upload", logger);
    const handle = budget.tryReserve(40, { path: "/v1/runs" });
    handle?.tag({ executionId: "e-1" });
    handle?.release();
    const released = events.find((entry) => entry.event === "budget_released");
    expect(released?.fields["executionId"]).toBe("e-1");
    expect(released?.fields["path"]).toBe("/v1/runs");
  });

  it("logs a refused reserve with the caller's context", () => {
    const { events, logger } = collectingLogger();
    const budget = instrumentByteBudget(createByteBudget(100), "inflight_upload", logger);
    expect(budget.tryReserve(101, { executionId: "e-1" })).toBeUndefined();
    const refused = events.find((entry) => entry.event === "budget_refused");
    expect(refused?.level).toBe("warn");
    expect(refused?.fields["executionId"]).toBe("e-1");
    expect(refused?.fields["requestedBytes"]).toBe(101);
  });

  it("logs a refused growth with the reservation's context, and a granted one not at all", () => {
    const { events, logger } = collectingLogger();
    const budget = instrumentByteBudget(createByteBudget(100), "inflight_upload", logger);
    const handle = budget.tryReserve(40, { path: "/v1/runs" });
    expect(handle?.tryGrowTo(100)).toBe(true);
    expect(events.filter((entry) => entry.event === "budget_refused")).toHaveLength(0);
    expect(handle?.tryGrowTo(101)).toBe(false);
    const refused = events.find((entry) => entry.event === "budget_refused");
    expect(refused?.fields["path"]).toBe("/v1/runs");
    expect(refused?.fields["requestedBytes"]).toBe(101);
  });
});
