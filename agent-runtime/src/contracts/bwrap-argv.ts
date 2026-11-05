// Normalization for captured bwrap argv: strips the per-run volatility (paths, ports, session
// credentials) so the remainder is a stable fingerprint of the SDK's sandbox translation — the
// undocumented surface an SDK bump can move in silence (docs/adr/0065, spike 8).

export type BwrapArgvContext = {
  scratchRoot: string;
  workspacePath: string;
};

export const WORKSPACE_TOKEN = "<WORKSPACE>";
export const SCRATCH_TOKEN = "<SCRATCH>";

const HTTP_SOCKET_PATTERN = /\/tmp\/claude-http-[0-9a-f]+\.sock/g;
const UID_TMPDIR_PATTERN = /\/tmp\/claude-\d+/g;
const PROXY_CREDENTIAL_PATTERN = /srt\.[A-Za-z0-9%+/_=-]+(:[0-9a-f]+)?/g;
const LOCALHOST_PORT_PATTERN = /localhost:\d+/g;
const LOOPBACK_PORT_PATTERN = /127\.0\.0\.1:\d+/g;
const PROXY_PORT_PATTERN = /proxyport=\d+/g;
const SETTINGS_MASK_PATTERN = /claude-settings-[0-9a-f]+\.json/g;
const SHELL_SNAPSHOT_PATTERN = /snapshot-bash-\d+-[a-z0-9]+\.sh/g;
const CWD_SENTINEL_PATTERN = /cwd-[0-9a-f]+/g;
// Since CLI 2.1.251 the SDK binds a per-project scratch dir under its uid tmpdir, named by the
// cwd path with slashes turned to dashes, and a session-id dir beneath it.
const SESSION_ID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g;
const WORKSPACE_SLUG_TOKEN = "<WORKSPACE-SLUG>";
// Whole-arg only: an ephemeral host proxy port rides as its own argv entry, while stable
// small values ("1", the fixed in-namespace 3128 inside command strings) must survive.
const WHOLE_ARG_PORT_PATTERN = /^\d{4,5}$/;
const WHOLE_ARG_HEX_SECRET_PATTERN = /^[0-9a-f]{32}$/;

export function normalizeBwrapArgv(argv: readonly string[], context: BwrapArgvContext): string[] {
  return argv.map((arg) => {
    if (WHOLE_ARG_HEX_SECRET_PATTERN.test(arg)) {
      return "<SECRET>";
    }
    if (WHOLE_ARG_PORT_PATTERN.test(arg)) {
      return "<PORT>";
    }
    return arg
      .replaceAll(context.workspacePath.replaceAll("/", "-"), WORKSPACE_SLUG_TOKEN)
      .replaceAll(context.workspacePath, WORKSPACE_TOKEN)
      .replaceAll(context.scratchRoot, SCRATCH_TOKEN)
      .replaceAll(HTTP_SOCKET_PATTERN, "/tmp/claude-http-<ID>.sock")
      .replaceAll(SETTINGS_MASK_PATTERN, "claude-settings-<ID>.json")
      .replaceAll(SHELL_SNAPSHOT_PATTERN, "snapshot-bash-<ID>.sh")
      .replaceAll(UID_TMPDIR_PATTERN, "/tmp/claude-<UID>")
      .replaceAll(CWD_SENTINEL_PATTERN, "cwd-<ID>")
      .replaceAll(SESSION_ID_PATTERN, "<SESSION>")
      .replaceAll(PROXY_CREDENTIAL_PATTERN, "srt.<CREDENTIAL>")
      .replaceAll(PROXY_PORT_PATTERN, "proxyport=<PORT>")
      .replaceAll(LOCALHOST_PORT_PATTERN, "localhost:<PORT>")
      .replaceAll(LOOPBACK_PORT_PATTERN, "127.0.0.1:<PORT>");
  });
}

// The shim writes one NUL-terminated arg at a time and ends each invocation with an ASCII
// record separator, so args containing whitespace or newlines survive verbatim.
export const INVOCATION_SEPARATOR = "\x1e";

// CLI 2.1.308 runs `bwrap --version` before it builds a sandbox (2.1.281 did not); that probe runs
// no command, so it is not a sandbox invocation and carries no translation to fingerprint.
const VERSION_PROBE_FLAG = "--version";

export function sandboxInvocations(invocations: readonly string[][]): string[][] {
  return invocations.filter((argv) => !(argv.length === 1 && argv[0] === VERSION_PROBE_FLAG));
}

export function parseNulDelimitedInvocations(log: string): string[][] {
  return log
    .split(INVOCATION_SEPARATOR)
    .filter((chunk) => chunk !== "")
    .map((chunk) => {
      const args = chunk.split("\0");
      // Every arg is NUL-terminated, so a well-formed chunk ends with one empty tail element;
      // interior empties are real empty-string args and must survive.
      if (args.at(-1) === "") {
        args.pop();
      }
      return args;
    });
}
