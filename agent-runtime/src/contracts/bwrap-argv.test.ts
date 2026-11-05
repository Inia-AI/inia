import { describe, expect, it } from "bun:test";
import { normalizeBwrapArgv, parseNulDelimitedInvocations, sandboxInvocations } from "./bwrap-argv.js";

const CONTEXT = {
  scratchRoot: "/tmp/scripted-run-abc123",
  workspacePath: "/tmp/scripted-run-abc123/workspaces/run-XYZ",
};

describe("normalizeBwrapArgv", () => {
  it("replaces the workspace path before the scratch root that contains it", () => {
    expect(
      normalizeBwrapArgv(
        [
          "--bind",
          "/tmp/scripted-run-abc123/workspaces/run-XYZ",
          "/tmp/scripted-run-abc123/workspaces/run-XYZ",
        ],
        CONTEXT,
      ),
    ).toEqual(["--bind", "<WORKSPACE>", "<WORKSPACE>"]);
  });

  it("replaces the scratch root wherever it appears inside a value", () => {
    expect(
      normalizeBwrapArgv(["--setenv", "PATH", "/tmp/scripted-run-abc123/shim:/usr/bin:/bin"], CONTEXT),
    ).toEqual(["--setenv", "PATH", "<SCRATCH>/shim:/usr/bin:/bin"]);
  });

  it("normalizes the per-session proxy socket name", () => {
    expect(
      normalizeBwrapArgv(
        ["--bind", "/tmp/claude-http-c8132be2afb1d809.sock", "/tmp/claude-http-c8132be2afb1d809.sock"],
        CONTEXT,
      ),
    ).toEqual(["--bind", "/tmp/claude-http-<ID>.sock", "/tmp/claude-http-<ID>.sock"]);
  });

  it("normalizes the uid-keyed claude tmp dir but not the http socket", () => {
    expect(normalizeBwrapArgv(["--setenv", "TMPDIR", "/tmp/claude-1000"], CONTEXT)).toEqual([
      "--setenv",
      "TMPDIR",
      "/tmp/claude-<UID>",
    ]);
  });

  it("normalizes per-invocation proxy credentials and ports", () => {
    expect(
      normalizeBwrapArgv(
        [
          "--setenv",
          "HTTPS_PROXY",
          "http://srt.dG9vbHVfc3Bpa2VfMQ%3D%3D:63eed1591f8492cad5a245fe0e23499c@localhost:3128",
        ],
        CONTEXT,
      ),
    ).toEqual(["--setenv", "HTTPS_PROXY", "http://srt.<CREDENTIAL>@localhost:<PORT>"]);
  });

  it("normalizes socat proxyauth credentials embedded in a command string", () => {
    const value =
      "ssh -o ProxyCommand='socat - PROXY:localhost:%h:%p,proxyport=3128,proxyauth=srt.dG9vbHU=:63eed1591f8492ca'";
    expect(normalizeBwrapArgv(["--setenv", "GIT_SSH_COMMAND", value], CONTEXT)).toEqual([
      "--setenv",
      "GIT_SSH_COMMAND",
      "ssh -o ProxyCommand='socat - PROXY:localhost:%h:%p,proxyport=<PORT>,proxyauth=srt.<CREDENTIAL>'",
    ]);
  });

  it("normalizes loopback stub ports", () => {
    expect(normalizeBwrapArgv(["--setenv", "ANTHROPIC_BASE_URL", "http://127.0.0.1:33139"], CONTEXT)).toEqual(
      ["--setenv", "ANTHROPIC_BASE_URL", "http://127.0.0.1:<PORT>"],
    );
  });

  it("normalizes a standalone proxy username and a whole-arg hex password", () => {
    expect(
      normalizeBwrapArgv(
        [
          "--setenv",
          "CLOUDSDK_PROXY_USERNAME",
          "srt.bXNnX3NjcmlwdGVkXzFfdG9vbA==",
          "--setenv",
          "CLOUDSDK_PROXY_PASSWORD",
          "9aedbe8458a21b3e18849623fc68f2a6",
        ],
        CONTEXT,
      ),
    ).toEqual([
      "--setenv",
      "CLOUDSDK_PROXY_USERNAME",
      "srt.<CREDENTIAL>",
      "--setenv",
      "CLOUDSDK_PROXY_PASSWORD",
      "<SECRET>",
    ]);
  });

  it("normalizes whole-arg ephemeral port numbers but not small stable values", () => {
    expect(
      normalizeBwrapArgv(
        ["--setenv", "CLAUDE_CODE_HOST_HTTP_PROXY_PORT", "39905", "--setenv", "SANDBOX_RUNTIME", "1"],
        CONTEXT,
      ),
    ).toEqual(["--setenv", "CLAUDE_CODE_HOST_HTTP_PROXY_PORT", "<PORT>", "--setenv", "SANDBOX_RUNTIME", "1"]);
  });

  it("normalizes per-session file names: settings mask, shell snapshot, cwd sentinel", () => {
    expect(
      normalizeBwrapArgv(
        [
          "/tmp/claude-1000/claude-settings-0f19580d9e92da08.json",
          "source /home/x/.claude/shell-snapshots/snapshot-bash-1787149564454-qlar6u.sh && pwd -P >| /tmp/claude-1000/cwd-790c",
        ],
        CONTEXT,
      ),
    ).toEqual([
      "/tmp/claude-<UID>/claude-settings-<ID>.json",
      "source /home/x/.claude/shell-snapshots/snapshot-bash-<ID>.sh && pwd -P >| /tmp/claude-<UID>/cwd-<ID>",
    ]);
  });

  it("normalizes the SDK's per-project scratch dir (the workspace path slugified) and its session id", () => {
    const projectScratch = "/tmp/claude-1000/-tmp-scripted-run-abc123-workspaces-run-XYZ";
    const session = `${projectScratch}/47c411a9-d8ed-4b4f-8b2f-3eb0f1b87bbe`;
    expect(
      normalizeBwrapArgv(
        ["--bind", projectScratch, projectScratch, "--ro-bind", `${session}/tasks`],
        CONTEXT,
      ),
    ).toEqual([
      "--bind",
      "/tmp/claude-<UID>/<WORKSPACE-SLUG>",
      "/tmp/claude-<UID>/<WORKSPACE-SLUG>",
      "--ro-bind",
      "/tmp/claude-<UID>/<WORKSPACE-SLUG>/<SESSION>/tasks",
    ]);
  });

  it("leaves stable flags untouched", () => {
    expect(normalizeBwrapArgv(["--unshare-net", "--tmpfs", "/tmp", "--ro-bind", "/", "/"], CONTEXT)).toEqual([
      "--unshare-net",
      "--tmpfs",
      "/tmp",
      "--ro-bind",
      "/",
      "/",
    ]);
  });
});

describe("parseNulDelimitedInvocations", () => {
  it("splits invocations on the record separator and args on NUL", () => {
    const log = "--flag\0value\0\x1e--other\0";
    expect(parseNulDelimitedInvocations(log)).toEqual([["--flag", "value"], ["--other"]]);
  });

  it("preserves interior empty-string args and drops only the NUL-terminator tail", () => {
    const log = "--setenv\0EMPTY\0\0--end\0\x1e";
    expect(parseNulDelimitedInvocations(log)).toEqual([["--setenv", "EMPTY", "", "--end"]]);
  });

  it("returns no invocations for an empty log", () => {
    expect(parseNulDelimitedInvocations("")).toEqual([]);
  });
});

describe("sandboxInvocations", () => {
  it("drops the CLI's bwrap --version capability probe and keeps the sandbox invocations in order", () => {
    const sandbox = ["--new-session", "--unshare-net", "--", "/bin/bash", "-c", "true"];
    expect(sandboxInvocations([["--version"], sandbox])).toEqual([sandbox]);
  });

  it("keeps an invocation that only carries --version inside its sandboxed command", () => {
    const sandbox = ["--new-session", "--", "/bin/bash", "-c", "bwrap --version"];
    expect(sandboxInvocations([sandbox])).toEqual([sandbox]);
  });
});
