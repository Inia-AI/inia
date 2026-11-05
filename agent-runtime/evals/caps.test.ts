import { describe, expect, it } from "bun:test";
import { AmbientCapabilitiesError, assertNoAmbientCapabilities } from "./caps.js";

function statusTextWith(capAmb: string): string {
  return ["Name:\tbun", "Uid:\t1000\t1000\t1000\t1000", `CapAmb:\t${capAmb}`, "Seccomp:\t0"].join("\n");
}

describe("assertNoAmbientCapabilities", () => {
  it("accepts a process whose ambient capability set is empty", () => {
    expect(() => assertNoAmbientCapabilities(statusTextWith("0000000000000000"))).not.toThrow();
  });

  it("refuses the supervisor's ambient CAP_SETUID/CAP_SETGID before any money is spent", () => {
    expect(() => assertNoAmbientCapabilities(statusTextWith("00000000000000c0"))).toThrow(
      AmbientCapabilitiesError,
    );
    expect(() => assertNoAmbientCapabilities(statusTextWith("00000000000000c0"))).toThrow(/setpriv/);
  });

  it("refuses when the ambient set cannot be read at all", () => {
    expect(() => assertNoAmbientCapabilities("Name:\tbun\nUid:\t1000")).toThrow(AmbientCapabilitiesError);
  });
});
