export class AmbientCapabilitiesError extends Error {}

const CAP_AMB_LINE = /^CapAmb:\s*([0-9a-fA-F]+)\s*$/m;

export function assertNoAmbientCapabilities(procStatusText: string): void {
  const capAmbHex = procStatusText.match(CAP_AMB_LINE)?.[1];
  if (capAmbHex === undefined) {
    throw new AmbientCapabilitiesError("could not read CapAmb from the process status text");
  }
  if (BigInt(`0x${capAmbHex}`) !== 0n) {
    throw new AmbientCapabilitiesError(
      `this process holds ambient capabilities (CapAmb ${capAmbHex}); bwrap refuses ` +
        "capable-but-not-setuid processes, so run evals through the caps-clearing hop: " +
        "bun run eval (its script wraps setpriv --inh-caps=-all --ambient-caps=-all)",
    );
  }
}
