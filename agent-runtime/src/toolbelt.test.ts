import { describe, expect, it } from "bun:test";
import { execFile, execFileSync, spawnSync } from "node:child_process";
import { X509Certificate } from "node:crypto";
import { chmodSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { getCACertificates } from "node:tls";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";
import { EXPECTED_CLAUDE_CODE_VERSION } from "./claude-code-pin.js";
import { RUN_CHILD_OOM_SCORE_ADJ, UID_POOL_BASE } from "./config.js";
import { sha256Hex } from "./hash.js";
import { createNarrower } from "./narrow.js";
import { biasOomScoreAdj } from "./oom.js";
import { buildQueryOptions, RUN_GIT_IDENTITY_ENV } from "./run-report.js";
import { SANDBOX_PROBE_BWRAP_ARGV, SDK_BWRAP_ARGV_PREFIX, SDK_BWRAP_ARGV_SUFFIX } from "./sandbox-probe.js";
import { privilegeDropArgs, runAsUid } from "./setpriv.js";

const PYTHON_IMPORTS = [
  "docx",
  "markitdown",
  "openpyxl",
  "pandas",
  "pdf2image",
  "pdfminer.high_level",
  "pdfplumber",
  "pypdf",
  "reportlab",
  "xlrd",
];

const REQUIRED_BINARIES: [string, string][] = [
  ["soffice", "--version"],
  ["pandoc", "--version"],
  ["pdftotext", "-v"],
  ["qpdf", "--version"],
  ["git", "--version"],
  ["jq", "--version"],
  ["python3", "--version"],
  ["curl", "--version"],
  ["fc-list", "--version"],
];

const REQUIRED_FONT_FAMILIES = [
  "Carlito",
  "Caladea",
  "DejaVu Sans",
  "Liberation Sans",
  "Liberation Serif",
  "Liberation Mono",
  "Noto Sans CJK",
];

const METRIC_TWINS: [string, string][] = [
  ["Arial", "Liberation Sans"],
  ["Times New Roman", "Liberation Serif"],
  ["Courier New", "Liberation Mono"],
  ["Calibri", "Carlito"],
  ["Cambria", "Caladea"],
];

const GENERIC_FAMILIES: [string, string][] = [
  ["sans-serif", "DejaVu Sans"],
  ["serif", "DejaVu Serif"],
  ["monospace", "DejaVu Sans Mono"],
];

const MICROSOFT_CORE_FAMILIES = [
  "Andale Mono",
  "Arial",
  "Arial Black",
  "Comic Sans MS",
  "Courier New",
  "Georgia",
  "Impact",
  "Times New Roman",
  "Trebuchet MS",
  "Verdana",
  "Webdings",
];

const CACHED_FONT_DIRECTORIES = [
  "/usr/share/fonts/opentype/noto",
  "/usr/share/fonts/truetype/crosextra",
  "/usr/share/fonts/truetype/liberation",
];

const FONT_CACHE_VALID_MARKER = "skipping, existing cache is valid";
const FONT_CACHE_STALE_MARKER = "failed to write cache";
const FONT_CACHE_UNWRITABLE_ERROR = "No writable cache directories";

const ROUNDTRIP_MARKER = "TOOLBELT_ROUNDTRIP_MARKER_7391";

// markitdown backslash-escapes markdown specials (including underscores), so this marker avoids them.
const MARKITDOWN_MARKER = "TOOLBELTMARKITDOWNMARKER7391";

const MARKITDOWN_HEADING_MARKER = "TOOLBELTMARKITDOWNHEADING7391";

const PAGE_RENDER_DPI = 50;

const DARK_PIXEL_CEILING = 128;

const LEGACY_WORKBOOK_MARKER = "TOOLBELTLEGACYXLSMARKER7391";

// Synthetic: openpyxl wrote one marker cell and soffice saved it as BIFF8 (ADR 0139).
const LEGACY_WORKBOOK_FIXTURE = fileURLToPath(
  new URL("toolbelt-legacy-workbook.fixture.xls", import.meta.url),
);

const GETENT_KEY_NOT_FOUND = 2;

const SOFFICE_LAUNCHER_PATH = "/usr/lib/libreoffice/program/soffice";

const SOFFICE_PROFILE_FLAG = "-env:UserInstallation=";

const ROUNDTRIP_TIMEOUT_MS = 120000;

const SANDBOX_BINARIES: [string, string][] = [
  ["bwrap", "--version"],
  ["socat", "-V"],
];

const SANDBOX_BINARY = "bwrap";

const EMULATED_ARM64_TIMEOUT_MS = 60000;

const CAPABILITY_CLEARING_ARGS = ["--inh-caps=-all", "--ambient-caps=-all", "--"];

const RUNS_UNDER_QEMU = process.env["EMULATED_PLATFORM"] === "true";

const QEMU_NAMESPACE_ERROR = "kernel does not support user namespaces";

const COMPROMISED_ROOT_SUBJECT = "CN=emSign ECC Root CA - G3";

const SYSTEM_TRUST_BUNDLE = "/etc/ssl/certs/ca-certificates.crt";

const CERTIFI_BUNDLE_SCRIPT = "import certifi; print(certifi.where())";

const PEM_CERTIFICATE = /-----BEGIN CERTIFICATE-----[^-]+-----END CERTIFICATE-----/g;

const MIN_TRUSTED_ROOTS = 100;

// Mozilla dropped these eMudhra roots after the October 2026 key leak. Bun's store follows Mozilla;
// the system store and certifi still ship the last two.
const ROOTS_MOZILLA_DROPPED = [
  COMPROMISED_ROOT_SUBJECT,
  "CN=emSign Root CA - C1",
  "CN=emSign ECC Root CA - C3",
];

const TRUST_STORES: [string, () => string[]][] = [
  ["the system trust store", () => pemCertificatesIn(SYSTEM_TRUST_BUNDLE)],
  ["certifi's bundle", certifiCertificates],
  ["Bun's default store", () => getCACertificates("default")],
];

function certifiCertificates(): string[] {
  return pemCertificatesIn(
    execFileSync("python3", ["-c", CERTIFI_BUNDLE_SCRIPT], { encoding: "utf8" }).trim(),
  );
}

function installedFontFamilies(): string[] {
  return execFileSync("fc-list", [":", "family"], { encoding: "utf8" })
    .split(/[\n,]/)
    .map((family) => family.trim());
}

function pemCertificatesIn(bundlePath: string): string[] {
  return readFileSync(bundlePath, "utf8").match(PEM_CERTIFICATE) ?? [];
}

function namesCompromisedRoot(subject: string): boolean {
  return subject.split("\n").includes(COMPROMISED_ROOT_SUBJECT);
}

function subjectsOf(certificates: string[]): string[] {
  return certificates.map((pem) => new X509Certificate(pem).subject);
}

function commonNameOf(subject: string): string | undefined {
  return subject.split("\n").find((part) => part.startsWith("CN="));
}

function withWorkDir(prefix: string, run: (workDir: string) => void): void {
  const workDir = mkdtempSync(join(tmpdir(), prefix));
  try {
    run(workDir);
  } finally {
    rmSync(workDir, { recursive: true, force: true });
  }
}

describe("toolbelt", () => {
  it(
    "imports every required python package",
    () => {
      const script = `import ${PYTHON_IMPORTS.join(", ")}; print("ok")`;
      const output = execFileSync("python3", ["-c", script], { encoding: "utf8" });
      expect(output.trim()).toBe("ok");
    },
    EMULATED_ARM64_TIMEOUT_MS,
  );

  for (const [binary, versionFlag] of REQUIRED_BINARIES) {
    it(`has ${binary} available`, () => {
      expect(() => execFileSync(binary, [versionFlag], { stdio: "pipe" })).not.toThrow();
    });
  }

  it("resolves the docx npm package from any working directory", () => {
    const script = "const d = require('docx'); console.log(typeof d.Document);";
    const output = execFileSync("bun", ["-e", script], { encoding: "utf8", cwd: "/tmp" });
    expect(output.trim()).toBe("function");
  });

  it("has every required font family installed", () => {
    const installed = installedFontFamilies();
    for (const family of REQUIRED_FONT_FAMILIES) {
      expect(installed.some((name) => name.startsWith(family))).toBe(true);
    }
  });

  for (const [requested, resolvesTo] of [...METRIC_TWINS, ...GENERIC_FAMILIES]) {
    it(`resolves ${requested} to ${resolvesTo}`, () => {
      const resolved = execFileSync("fc-match", ["-f", "%{family[0]}", requested], { encoding: "utf8" });
      expect(resolved).toBe(resolvesTo);
    });
  }

  it("installs no Microsoft core font", () => {
    const installed = installedFontFamilies();
    expect(MICROSOFT_CORE_FAMILIES.filter((family) => installed.includes(family))).toEqual([]);
  });

  // A stale system cache makes every fontconfig user (each soffice start in every sandbox)
  // rescan the whole font tree and try to write caches it has nowhere to keep.
  it("ships a system fontconfig cache that is valid for every font directory as the run uid", () => {
    const report = spawnSync("fc-cache", ["--verbose"], { encoding: "utf8" });
    const lines = report.stdout.split("\n");
    expect(lines.filter((line) => line.includes(FONT_CACHE_STALE_MARKER))).toEqual([]);
    for (const directory of CACHED_FONT_DIRECTORIES) {
      expect(lines.some((line) => line.startsWith(`${directory}: ${FONT_CACHE_VALID_MARKER}`))).toBe(true);
    }
    expect(report.stderr).not.toContain(FONT_CACHE_UNWRITABLE_ERROR);
  });

  it(
    "renders a docx to pdf and extracts its text back (python-docx -> soffice -> pdftotext)",
    () => {
      withWorkDir("toolbelt-roundtrip-", (workDir) => {
        const docxPath = join(workDir, "roundtrip.docx");
        const pdfPath = join(workDir, "roundtrip.pdf");
        const createDocx = [
          "import docx",
          "d = docx.Document()",
          `d.add_paragraph("${ROUNDTRIP_MARKER}")`,
          `d.save("${docxPath}")`,
        ].join("; ");
        execFileSync("python3", ["-c", createDocx], { stdio: "pipe" });
        execFileSync(
          "soffice",
          [
            "--headless",
            `-env:UserInstallation=file://${workDir}/lo-profile`,
            "--convert-to",
            "pdf",
            "--outdir",
            workDir,
            docxPath,
          ],
          { stdio: "pipe" },
        );
        const extracted = execFileSync("pdftotext", [pdfPath, "-"], { encoding: "utf8" });
        expect(extracted).toContain(ROUNDTRIP_MARKER);
      });
    },
    ROUNDTRIP_TIMEOUT_MS,
  );

  // A legacy .xls must be readable with no LibreOffice at all: inside a run the SDK's own
  // seccomp filter denies the unix socket soffice needs, whatever profile it is given (ADR 0139).
  it(
    "opens a legacy xls with xlrd and with markitdown, and reads its cell back",
    () => {
      const readXls = `import xlrd; print(xlrd.open_workbook("${LEGACY_WORKBOOK_FIXTURE}").sheet_by_index(0).cell_value(0, 0))`;
      expect(execFileSync("python3", ["-c", readXls], { encoding: "utf8" })).toContain(
        LEGACY_WORKBOOK_MARKER,
      );
      expect(execFileSync("markitdown", [LEGACY_WORKBOOK_FIXTURE], { encoding: "utf8" })).toContain(
        LEGACY_WORKBOOK_MARKER,
      );
    },
    ROUNDTRIP_TIMEOUT_MS,
  );

  it("routes the soffice launcher through the profile wrapper", () => {
    expect(readFileSync(SOFFICE_LAUNCHER_PATH, "utf8").includes(SOFFICE_PROFILE_FLAG)).toBe(true);
    expect(existsSync(`${SOFFICE_LAUNCHER_PATH}.real`)).toBe(true);
  });

  it(
    "converts a pdf built by the toolbelt through markitdown, and renders its page through Pillow",
    () => {
      withWorkDir("toolbelt-markitdown-pdf-", (workDir) => {
        const pdfPath = join(workDir, "roundtrip.pdf");
        const createPdf = [
          "from reportlab.pdfgen import canvas",
          `c = canvas.Canvas("${pdfPath}")`,
          `c.drawString(72, 720, "${MARKITDOWN_MARKER}")`,
          "c.save()",
        ].join("; ");
        execFileSync("python3", ["-c", createPdf], { stdio: "pipe" });
        const converted = execFileSync("markitdown", [pdfPath], { encoding: "utf8" });
        expect(converted).toContain(MARKITDOWN_MARKER);
        const renderPage = [
          "from pdf2image import convert_from_path",
          `page = convert_from_path("${pdfPath}", dpi=${PAGE_RENDER_DPI})[0]`,
          'print(page.mode, page.convert("L").getextrema()[0])',
        ].join("; ");
        const [mode, darkestGrey] = execFileSync("python3", ["-c", renderPage], { encoding: "utf8" })
          .trim()
          .split(" ");
        expect(mode).toBe("RGB");
        expect(Number(darkestGrey)).toBeLessThan(DARK_PIXEL_CEILING);
      });
    },
    ROUNDTRIP_TIMEOUT_MS,
  );

  it(
    "converts an xlsx built by the toolbelt through markitdown and gets its text back",
    () => {
      withWorkDir("toolbelt-markitdown-xlsx-", (workDir) => {
        const xlsxPath = join(workDir, "roundtrip.xlsx");
        const createXlsx = [
          "import openpyxl",
          "wb = openpyxl.Workbook()",
          `wb.active["A1"] = "${MARKITDOWN_MARKER}"`,
          `wb.save("${xlsxPath}")`,
        ].join("; ");
        execFileSync("python3", ["-c", createXlsx], { stdio: "pipe" });
        const converted = execFileSync("markitdown", [xlsxPath], { encoding: "utf8" });
        expect(converted).toContain(MARKITDOWN_MARKER);
      });
    },
    ROUNDTRIP_TIMEOUT_MS,
  );

  it(
    "converts a docx built by the toolbelt through markitdown into markdown with its heading and text",
    () => {
      withWorkDir("toolbelt-markitdown-docx-", (workDir) => {
        const docxPath = join(workDir, "roundtrip.docx");
        const createDocx = [
          "import docx",
          "d = docx.Document()",
          `d.add_heading("${MARKITDOWN_HEADING_MARKER}", level=1)`,
          `d.add_paragraph("${MARKITDOWN_MARKER}")`,
          `d.save("${docxPath}")`,
        ].join("; ");
        execFileSync("python3", ["-c", createDocx], { stdio: "pipe" });
        const converted = execFileSync("markitdown", [docxPath], { encoding: "utf8" });
        expect(converted.split("\n")).toContain(`# ${MARKITDOWN_HEADING_MARKER}`);
        expect(converted).toContain(MARKITDOWN_MARKER);
      });
    },
    ROUNDTRIP_TIMEOUT_MS,
  );
});

describe("trusted roots", () => {
  for (const [store, readStore] of TRUST_STORES) {
    it(
      `trusts no root named ${COMPROMISED_ROOT_SUBJECT}, whose eMudhra intermediates leaked their keys, in ${store}`,
      () => {
        const subjects = subjectsOf(readStore());
        expect(subjects.length).toBeGreaterThan(MIN_TRUSTED_ROOTS);
        expect(subjects.filter(namesCompromisedRoot)).toEqual([]);
      },
      EMULATED_ARM64_TIMEOUT_MS,
    );
  }

  it("trusts none of the eMudhra roots Mozilla dropped in Bun's default store", () => {
    const commonNames = subjectsOf(getCACertificates("default")).map(commonNameOf);
    expect(commonNames.filter((name) => name !== undefined && ROOTS_MOZILLA_DROPPED.includes(name))).toEqual(
      [],
    );
  });
});

// bun/uv-installed trees only — dpkg-managed trees ship correct modes by construction
const EXECUTABLE_TREES = ["/opt", "/app/node_modules"];

const GROUP_OR_WORLD_WRITABLE = "/go+w";

describe("image permissions", () => {
  for (const tree of EXECUTABLE_TREES) {
    it(`leaves nothing under ${tree} group- or world-writable, so it cannot be rewritten in place`, () => {
      const writable = execFileSync("find", [tree, "!", "-type", "l", "-perm", GROUP_OR_WORLD_WRITABLE], {
        encoding: "utf8",
      });
      expect(writable.trim()).toBe("");
    });
  }

  it("ships prlimit, without which no run can be spawned under its task cap", () => {
    expect(() => execFileSync("prlimit", ["--version"], { encoding: "utf8" })).not.toThrow();
  });

  it("lets a process bias itself toward the OOM killer, which the run child does at entry", () => {
    expect(biasOomScoreAdj()).toBe(true);
    expect(readFileSync("/proc/self/oom_score_adj", "utf8").trim()).toBe(String(RUN_CHILD_OOM_SCORE_ADJ));
  });

  it("ships no setuid or setgid binaries, so privilege re-gain has nothing to exec", () => {
    // explicit trees, not "/" — a full-root walk as non-root trips unreadable dirs and masks find's exit code
    const suid = execFileSync("find", ["/usr", "/opt", "/app", "-xdev", "-type", "f", "-perm", "/6000"], {
      encoding: "utf8",
    });
    expect(suid.trim()).toBe("");
  });
});

const SANDBOX_CONVERT_MARKER = "ENG97_SANDBOX_CONVERT_MARKER";

const WORLD_WRITABLE_MODE = 0o777;

const PDF_MAGIC = "%PDF-";

const SANDBOX_CAPABILITY_TIMEOUT_MS = 180000;

const BARRIER_POLL_LIMIT = 100;

const execFileAsync = promisify(execFile);

// allowRead and the SDK's own /tmp/claude-<uid> rebind are deliberately not reconstructed here —
// the argv shim covers those (docs/adr/0065-tmp-joins-the-denied-roots-which-is-exactly-what-makes.md)
function sandboxMountArgs(workspacePath: string, alsoWritable: readonly string[]): string[] {
  const options = buildQueryOptions({
    maxBudgetUsd: 1,
    maxTurns: 1,
    abortController: new AbortController(),
    env: {},
    workspacePath,
  });
  const filesystem = options.sandbox?.filesystem;
  const args = [...SDK_BWRAP_ARGV_PREFIX];
  // this test runs without compose's tmpfs mounts, so /workspaces is absent here
  for (const root of (filesystem?.denyRead ?? []).filter((root) => existsSync(root))) {
    args.push("--tmpfs", root);
  }
  for (const writablePath of [...(filesystem?.allowWrite ?? []), ...alsoWritable]) {
    args.push("--bind", writablePath, writablePath);
  }
  args.push(...SDK_BWRAP_ARGV_SUFFIX);
  return args;
}

function runInSandbox(
  workspacePath: string,
  script: string,
  alsoWritable: readonly string[] = [],
  runUid?: number,
  env?: Record<string, string | undefined>,
): Promise<string> {
  return execFileAsync(
    "setpriv",
    [
      ...(runUid === undefined ? CAPABILITY_CLEARING_ARGS : [...privilegeDropArgs(runUid), "--"]),
      SANDBOX_BINARY,
      ...sandboxMountArgs(workspacePath, alsoWritable),
      "--",
      "/bin/sh",
      "-c",
      script,
    ],
    { encoding: "utf8", ...(env === undefined ? {} : { env }) },
  ).then((result) => result.stdout);
}

describe("toolbelt inside the run sandbox", () => {
  it(
    "converts a docx to pdf under the sandbox mount shape a run executes with",
    async () => {
      const workspace = mkdtempSync(join(tmpdir(), "sandbox-convert-"));
      try {
        const script = [
          `cd ${workspace}`,
          `python3 -c "import docx; d=docx.Document(); d.add_paragraph('${SANDBOX_CONVERT_MARKER}'); d.save('in.docx')"`,
          "soffice --headless --convert-to pdf --outdir . in.docx >/dev/null 2>&1",
          "head -c 5 in.pdf",
          "pdftotext in.pdf -",
        ].join(" && ");
        if (RUNS_UNDER_QEMU) {
          await expect(runInSandbox(workspace, script)).rejects.toThrow(QEMU_NAMESPACE_ERROR);
          return;
        }
        const output = await runInSandbox(workspace, script);
        expect(output).toContain(PDF_MAGIC);
        expect(output).toContain(SANDBOX_CONVERT_MARKER);
      } finally {
        rmSync(workspace, { recursive: true, force: true });
      }
    },
    SANDBOX_CAPABILITY_TIMEOUT_MS,
  );

  // A run executes as a leased uid that is a number handed to setpriv, never an image user, so
  // LibreOffice finds no passwd entry and cannot resolve a profile directory by itself.
  it(
    "converts a legacy xls and a docx as a run uid with no passwd entry, under the sandbox mount shape",
    async () => {
      expect(spawnSync("getent", ["passwd", String(UID_POOL_BASE)]).status).toBe(GETENT_KEY_NOT_FOUND);
      const staging = mkdtempSync(join(tmpdir(), "sandbox-leased-"));
      chmodSync(staging, WORLD_WRITABLE_MODE);
      const workspace = join(staging, "workspace");
      try {
        await runAsUid(UID_POOL_BASE, ["mkdir", "-m", "700", workspace]);
        await runAsUid(UID_POOL_BASE, ["cp", LEGACY_WORKBOOK_FIXTURE, join(workspace, "legacy.xls")]);
        const script = [
          `cd ${workspace}`,
          `python3 -c "import docx; d=docx.Document(); d.add_paragraph('${SANDBOX_CONVERT_MARKER}'); d.save('in.docx')"`,
          "soffice --headless --convert-to xlsx --outdir converted legacy.xls",
          "soffice --headless --convert-to pdf --outdir . in.docx",
          `python3 -c "import openpyxl; print(openpyxl.load_workbook('converted/legacy.xlsx').active['A1'].value)"`,
          "pdftotext in.pdf -",
        ].join(" && ");
        if (RUNS_UNDER_QEMU) {
          await expect(runInSandbox(workspace, script, [], UID_POOL_BASE)).rejects.toThrow(
            QEMU_NAMESPACE_ERROR,
          );
          return;
        }
        const output = await runInSandbox(workspace, script, [], UID_POOL_BASE);
        expect(output).toContain(LEGACY_WORKBOOK_MARKER);
        expect(output).toContain(SANDBOX_CONVERT_MARKER);
      } finally {
        await runAsUid(UID_POOL_BASE, ["rm", "-rf", workspace]);
        rmSync(staging, { recursive: true, force: true });
      }
    },
    SANDBOX_CAPABILITY_TIMEOUT_MS,
  );

  it(
    "commits with git as a run uid with no passwd entry and no readable HOME, using the run's identity",
    async () => {
      const staging = mkdtempSync(join(tmpdir(), "sandbox-git-"));
      chmodSync(staging, WORLD_WRITABLE_MODE);
      const workspace = join(staging, "workspace");
      const withoutIdentity = { PATH: process.env["PATH"] ?? "", HOME: "/nonexistent-home" };
      const runEnv = buildQueryOptions({
        maxBudgetUsd: 1,
        maxTurns: 1,
        abortController: new AbortController(),
        env: withoutIdentity,
        workspacePath: workspace,
      }).env;
      const script = [
        `cd ${workspace}`,
        "rm -rf .git",
        "git init -q",
        "echo draft > note.txt",
        "git add note.txt",
        "git commit -q -m first",
        "git log -1 --format=%an",
      ].join(" && ");
      try {
        await runAsUid(UID_POOL_BASE, ["mkdir", "-m", "700", workspace]);
        if (RUNS_UNDER_QEMU) {
          await expect(runInSandbox(workspace, script, [], UID_POOL_BASE, runEnv)).rejects.toThrow(
            QEMU_NAMESPACE_ERROR,
          );
          return;
        }
        await expect(runInSandbox(workspace, script, [], UID_POOL_BASE, withoutIdentity)).rejects.toThrow(
          "Author identity unknown",
        );
        const output = await runInSandbox(workspace, script, [], UID_POOL_BASE, runEnv);
        expect(output.trim()).toBe(RUN_GIT_IDENTITY_ENV.GIT_AUTHOR_NAME);
      } finally {
        await runAsUid(UID_POOL_BASE, ["rm", "-rf", workspace]);
        rmSync(staging, { recursive: true, force: true });
      }
    },
    SANDBOX_CAPABILITY_TIMEOUT_MS,
  );

  it(
    "gives every sandbox a private /tmp, so concurrent runs never see each other's",
    async () => {
      const outsideMarker = join("/tmp", `outside-${process.pid}.marker`);
      writeFileSync(outsideMarker, "outside");
      const first = mkdtempSync(join(tmpdir(), "sandbox-tmp-a-"));
      const second = mkdtempSync(join(tmpdir(), "sandbox-tmp-b-"));
      // barrier: without it a pass could mean "never overlapped" rather than "couldn't see each other"
      const barrier = mkdtempSync(join(tmpdir(), "sandbox-barrier-"));
      try {
        const runSandbox = (workspace: string, own: string, other: string): Promise<string> => {
          const script = [
            `touch /tmp/${own}`,
            `touch ${barrier}/${own}.ready`,
            `i=0; while [ ! -f ${barrier}/${other}.ready ] && [ $i -lt ${BARRIER_POLL_LIMIT} ]; do sleep 0.1; i=$((i+1)); done`,
            `echo "TMP $(ls /tmp | tr '\\n' ' ')"`,
            `echo "BARRIER $(ls ${barrier} | tr '\\n' ' ')"`,
          ].join(" && ");
          return runInSandbox(workspace, script, [barrier]);
        };
        if (RUNS_UNDER_QEMU) {
          await expect(runSandbox(first, "run-a", "run-b")).rejects.toThrow(QEMU_NAMESPACE_ERROR);
          return;
        }
        const [outputA, outputB] = await Promise.all([
          runSandbox(first, "run-a", "run-b"),
          runSandbox(second, "run-b", "run-a"),
        ]);
        const sectionOf = (output: string, label: string): string =>
          output.split("\n").find((line) => line.startsWith(`${label} `)) ?? "";
        const tmpA = sectionOf(outputA, "TMP");
        const tmpB = sectionOf(outputB, "TMP");
        expect(sectionOf(outputA, "BARRIER")).toContain("run-b.ready");
        expect(sectionOf(outputB, "BARRIER")).toContain("run-a.ready");
        expect(tmpA).toContain("run-a");
        expect(tmpA).not.toContain("run-b");
        expect(tmpB).toContain("run-b");
        expect(tmpB).not.toContain("run-a");
        expect(tmpA).not.toContain("outside");
        expect(tmpB).not.toContain("outside");
      } finally {
        rmSync(outsideMarker, { force: true });
        rmSync(first, { recursive: true, force: true });
        rmSync(second, { recursive: true, force: true });
        rmSync(barrier, { recursive: true, force: true });
      }
    },
    SANDBOX_CAPABILITY_TIMEOUT_MS,
  );
});

describe("sandbox dependency", () => {
  it("ships bubblewrap, which the SDK sandbox shells out to", () => {
    const output = execFileSync(SANDBOX_BINARY, ["--version"], { encoding: "utf8" });
    expect(output).toContain("bubblewrap");
  });

  for (const [binary, versionFlag] of SANDBOX_BINARIES) {
    it(`has ${binary}, without which the SDK refuses to start a sandboxed run`, () => {
      expect(() => execFileSync(binary, [versionFlag], { stdio: "pipe" })).not.toThrow();
    });
  }

  it(
    "creates the SDK sandbox namespace set with the run child's own probe argv, which requires the repo seccomp profile on the container",
    () => {
      const createNamespaces = (): void => {
        execFileSync("setpriv", [...CAPABILITY_CLEARING_ARGS, SANDBOX_BINARY, ...SANDBOX_PROBE_BWRAP_ARGV], {
          stdio: "pipe",
        });
      };
      if (RUNS_UNDER_QEMU) {
        expect(createNamespaces).toThrow(QEMU_NAMESPACE_ERROR);
        return;
      }
      expect(createNamespaces).not.toThrow();
    },
    EMULATED_ARM64_TIMEOUT_MS,
  );
});

const SDK_PACKAGE_NAME = "@anthropic-ai/claude-agent-sdk";

describe("Claude Code CLI pin", () => {
  const requireFromSdkConsumer = createRequire(import.meta.url);
  const narrow = createNarrower((message) => new Error(message));

  function installedCliManifest(): { version: string; platforms: Record<string, unknown> } {
    const sdkDir = dirname(requireFromSdkConsumer.resolve(SDK_PACKAGE_NAME));
    const parsed: unknown = JSON.parse(readFileSync(join(sdkDir, "manifest.json"), "utf8"));
    const record = narrow.asRecord(parsed, "manifest");
    return {
      version: narrow.requireString(record, "version", "manifest"),
      platforms: narrow.asRecord(record["platforms"], "manifest.platforms"),
    };
  }

  it("ships exactly the pinned Claude Code CLI version", () => {
    expect(installedCliManifest().version).toBe(EXPECTED_CLAUDE_CODE_VERSION);
  });

  it("resolves a CLI binary whose bytes match the manifest checksum for this platform", () => {
    const platformKey = `${process.platform}-${process.arch}`;
    const entry = narrow.asRecord(
      installedCliManifest().platforms[platformKey],
      `manifest.platforms.${platformKey}`,
    );
    const binaryPath = requireFromSdkConsumer.resolve(`${SDK_PACKAGE_NAME}-${platformKey}/claude`);
    const bytes = readFileSync(binaryPath);
    expect(bytes.byteLength).toBe(narrow.requireNumber(entry, "size", platformKey));
    expect(sha256Hex(bytes)).toBe(narrow.requireString(entry, "checksum", platformKey));
  });
});
