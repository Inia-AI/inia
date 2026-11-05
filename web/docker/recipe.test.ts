import { describe, expect, it, onTestFinished } from 'vitest'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { randomBytes } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const ROOT = path.join(path.dirname(fileURLToPath(import.meta.url)), '..')
const DOCKERFILE = fs.readFileSync(path.join(ROOT, 'Dockerfile'), 'utf8')
const NGINX = DOCKERFILE.match(/^FROM (nginx\S+)/m)![1]
const HAS_DOCKER = spawnSync('docker', ['info'], { stdio: 'ignore' }).status === 0

describe('the web image recipe', () => {
  it('pins the nginx image it runs on by digest', () => {
    expect(DOCKERFILE).toMatch(/^FROM nginx:alpine@sha256:[0-9a-f]{64}$/m)
  })

  it('takes the Alpine fixes published after its pinned digest, and its build log names the nginx version', () => {
    expect(DOCKERFILE).toMatch(/apk upgrade --no-cache/)
    expect(DOCKERFILE).toMatch(/nginx -v/)
  })

  it('drops the entrypoint script that reaches the network before nginx binds', () => {
    expect(DOCKERFILE).toMatch(/rm -f \/docker-entrypoint\.d\/10-listen-on-ipv6-by-default\.sh/)
  })

  it('checks its health on a loopback address rather than a name with two answers', () => {
    const line = DOCKERFILE.match(/CMD wget[^\n]*/)![0]
    expect(line).toContain('127.0.0.1')
    expect(line).not.toContain('localhost')
  })
})

const docker = (...args: string[]) => {
  const result = spawnSync('docker', args, { encoding: 'utf8', cwd: ROOT })
  expect(result.status, `docker ${args.join(' ')}\n${result.stderr}`).toBe(0)
  return result.stdout.trim()
}

const portOf = (container: string) => docker('port', container, '80/tcp').split('\n')[0].split(':').pop()!

// The image's nginx stage, with everything it adds except the built app bundle.
const nginxStage = () =>
  DOCKERFILE.slice(DOCKERFILE.indexOf('\nFROM nginx'))
    .split('\n')
    .filter((line) => !line.startsWith('COPY --from=build'))
    .join('\n')

// A stand-in for the API container: it answers every request with its name and the URI it got.
const backend = (network: string, name: string, ip: string, aliases: string[]) =>
  docker('run', '-d', '--name', name, '--network', network, '--ip', ip,
    ...aliases.flatMap((alias) => ['--network-alias', alias]), '--entrypoint', 'sh', NGINX, '-c',
    `printf 'server { listen 80; location / { return 200 "%s $request_uri"; } }' ${name} > /etc/nginx/conf.d/default.conf && exec nginx -g 'daemon off;'`)

const answer = async (port: string, uri: string) => {
  try {
    const response = await fetch(`http://127.0.0.1:${port}${uri}`)
    return `${response.status} ${await response.text()}`
  } catch (error) {
    return String(error)
  }
}

const eventually = async (read: () => Promise<string>, expected: string, timeoutMs: number) => {
  const deadline = Date.now() + timeoutMs
  let last = ''
  while (Date.now() < deadline) {
    last = await read()
    if (last === expected) {
      return
    }
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  expect(last).toBe(expected)
}

describe('the web image proxy', () => {
  // CI always has Docker, so there the test runs and a missing daemon fails it.
  it.runIf(HAS_DOCKER || process.env.CI)(
    'follows the API to its new address when the API container is recreated',
    { timeout: 180_000 },
    async () => {
      const id = `web-proxy-test-${randomBytes(4).toString('hex')}`
      const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'web-proxy-test-'))
      onTestFinished(() => {
        spawnSync('docker', ['rm', '-f', `${id}-web`, `${id}-api-1`, `${id}-api-2`, `${id}-admin`], { stdio: 'ignore' })
        spawnSync('docker', ['network', 'rm', id], { stdio: 'ignore' })
        spawnSync('docker', ['image', 'rm', id], { stdio: 'ignore' })
        fs.rmSync(dir, { recursive: true, force: true })
      })

      const dockerfile = path.join(dir, 'Dockerfile')
      fs.writeFileSync(dockerfile, nginxStage())
      docker('build', '-q', '-t', id, '-f', dockerfile, '.')
      // A fixed subnet lets the test give the old API address to another container on purpose, as
      // Docker may do on its own when containers restart in a different order.
      docker('network', 'create', '--subnet', '172.31.252.0/24', id)
      const [OLD, NEW] = ['172.31.252.10', '172.31.252.11']

      backend(id, `${id}-api-1`, OLD, ['api'])
      docker('run', '-d', '--name', `${id}-web`, '--network', id, '-p', '127.0.0.1::80', id)
      const port = portOf(`${id}-web`)
      await eventually(() => answer(port, '/api/v1/meta/version?probe=1'), `200 ${id}-api-1 /api/v1/meta/version?probe=1`, 30_000)

      docker('rm', '-f', `${id}-api-1`)
      backend(id, `${id}-admin`, OLD, [])
      backend(id, `${id}-api-2`, NEW, ['api'])

      await eventually(() => answer(port, '/api/v1/meta/version?probe=2'), `200 ${id}-api-2 /api/v1/meta/version?probe=2`, 30_000)
    },
  )
})

// The script takes the config path and rewrites through a temp file so these tests can run it on a macOS host.
const CSP_SCRIPT = path.join(ROOT, 'docker', '41-content-security-policy.sh')
const POLICY_BEFORE_CONNECT_SRC =
  "default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client; style-src 'self' 'unsafe-inline' https://accounts.google.com/gsi/style; img-src 'self' data: blob:; font-src 'self' data:; connect-src "
const POLICY_AFTER_CONNECT_SRC =
  "; frame-src 'self' blob: https://accounts.google.com/gsi/; worker-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'"
const DEFAULT_CONNECT_SRC = "'self' https:"
const NARROW_CONNECT_SRC = "'self' https://accounts.google.com"
const policyWith = (connectSrc: string) => `${POLICY_BEFORE_CONNECT_SRC}${connectSrc}${POLICY_AFTER_CONNECT_SRC}`
const headerLine = (name: string, connectSrc: string) => `    add_header ${name} "${policyWith(connectSrc)}" always;`
const TODAYS_CSP_LINE = headerLine('Content-Security-Policy', DEFAULT_CONNECT_SRC)

const applyCsp = (env: Record<string, string>) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'web-csp-test-'))
  onTestFinished(() => fs.rmSync(dir, { recursive: true, force: true }))
  const conf = path.join(dir, 'default.conf')
  fs.copyFileSync(path.join(ROOT, 'docker', 'nginx.conf'), conf)
  const result = spawnSync('sh', [CSP_SCRIPT, conf], { encoding: 'utf8', env: { PATH: process.env.PATH, ...env } })
  const cspLines = fs.readFileSync(conf, 'utf8').split('\n')
    .filter((line) => line.includes('add_header Content-Security-Policy') && !line.includes('PLACEHOLDER'))
  return { status: result.status, stderr: result.stderr, cspLines }
}

describe('the content security policy the container sends', () => {
  it('is the same single header, byte for byte, when neither setting is given', () => {
    expect(applyCsp({})).toEqual({ status: 0, stderr: '', cspLines: [TODAYS_CSP_LINE] })
  })

  it('treats an empty setting as an absent one', () => {
    expect(applyCsp({ CSP_CONNECT_SRC: '', CSP_MODE: '' }).cspLines).toEqual([TODAYS_CSP_LINE])
  })

  it('enforces CSP_CONNECT_SRC in place of the default connect-src sources when CSP_MODE is enforce', () => {
    expect(applyCsp({ CSP_MODE: 'enforce', CSP_CONNECT_SRC: NARROW_CONNECT_SRC })).toEqual({
      status: 0,
      stderr: '',
      cspLines: [headerLine('Content-Security-Policy', NARROW_CONNECT_SRC)],
    })
  })

  it('applies CSP_CONNECT_SRC when CSP_MODE is unset', () => {
    expect(applyCsp({ CSP_CONNECT_SRC: NARROW_CONNECT_SRC }).cspLines)
      .toEqual([headerLine('Content-Security-Policy', NARROW_CONNECT_SRC)])
  })

  it('keeps enforcing the default policy and reports breaches of CSP_CONNECT_SRC when CSP_MODE is report-only', () => {
    expect(applyCsp({ CSP_MODE: 'report-only', CSP_CONNECT_SRC: NARROW_CONNECT_SRC })).toEqual({
      status: 0,
      stderr: '',
      cspLines: [TODAYS_CSP_LINE, headerLine('Content-Security-Policy-Report-Only', NARROW_CONNECT_SRC)],
    })
  })

  it.each([
    ['a semicolon', "'self'; script-src *"],
    ['a double quote', '\'self\'" always; add_header X "y'],
    ['a dollar sign', "'self' $host"],
    ['a line break', "'self'\nhttps:"],
    ['a backslash', "'self' https:\\"],
    ['a tab', "'self'\thttps:"],
    ['a quote that is not around a keyword', "'self' 'https://accounts.google.com"],
    ['only spaces', '   '],
    ['an ampersand', "'self' https://a&b"],
    ['a pipe', "'self' https://a|b"],
  ])('refuses CSP_CONNECT_SRC with %s and leaves the config alone', (_, value) => {
    const { status, stderr, cspLines } = applyCsp({ CSP_CONNECT_SRC: value })
    expect(status).not.toBe(0)
    expect(stderr).toContain('which is not a list of sources')
    expect(cspLines).toEqual([])
  })

  it.each(['*', 'http:', 'HTTP:', 'ws:', 'wss:', 'data:', 'blob:', 'http://api.example.com', "'unsafe-inline'", "'unsafe-eval'"])(
    'refuses CSP_CONNECT_SRC with %s, which would allow more than the default',
    (source) => {
      const { status, stderr, cspLines } = applyCsp({ CSP_CONNECT_SRC: `'self' ${source}` })
      expect(status).not.toBe(0)
      expect(stderr).toContain(`and ${source} would allow more connections than ${DEFAULT_CONNECT_SRC}`)
      expect(cspLines).toEqual([])
    },
  )

  it.each(['CSP_CONNECT_SRC_PLACEHOLDER', 'PROXY_BACKEND_URL_PLACEHOLDER', 'RESOLVER_PLACEHOLDER'])(
    'refuses CSP_CONNECT_SRC with the placeholder %s',
    (placeholder) => {
      const { status, stderr, cspLines } = applyCsp({ CSP_CONNECT_SRC: `'self' ${placeholder}` })
      expect(status).not.toBe(0)
      expect(stderr).toContain(`and ${placeholder} is a placeholder`)
      expect(cspLines).toEqual([])
    },
  )

  it.each(['report_only', 'Report-Only', 'enforce;', 'off'])('refuses CSP_MODE %s and leaves the config alone', (mode) => {
    const { status, stderr, cspLines } = applyCsp({ CSP_MODE: mode })
    expect(status).not.toBe(0)
    expect(stderr).toBe(
      `CSP_MODE is "${mode}". Set it to enforce or report-only, or leave it unset to enforce the policy. The web container did not start. Correct the setting and start it again.\n`,
    )
    expect(cspLines).toEqual([])
  })

  it('names the setting, the value and the fix in a refusal, and no file', () => {
    expect(applyCsp({ CSP_CONNECT_SRC: "'self' http:" }).stderr).toBe(
      `CSP_CONNECT_SRC is "'self' http:", and http: would allow more connections than 'self' https:. Give sources separated by spaces, such as 'self' https://accounts.google.com, or leave it unset to allow 'self' https:. The web container did not start. Correct the setting and start it again.\n`,
    )
  })
})

const cspHeaders = async (port: string) => {
  try {
    const { headers } = await fetch(`http://127.0.0.1:${port}/health`)
    return JSON.stringify([headers.get('content-security-policy'), headers.get('content-security-policy-report-only')])
  } catch (error) {
    return String(error)
  }
}

describe('the web image content security policy', () => {
  it.runIf(HAS_DOCKER || process.env.CI)(
    'is sent by nginx in the mode CSP_MODE names, and a refused setting stops the container',
    { timeout: 180_000 },
    async () => {
      const id = `web-csp-test-${randomBytes(4).toString('hex')}`
      const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'web-csp-test-'))
      onTestFinished(() => {
        spawnSync('docker', ['rm', '-f', `${id}-default`, `${id}-enforce`, `${id}-report-only`, `${id}-refused`], { stdio: 'ignore' })
        spawnSync('docker', ['image', 'rm', id], { stdio: 'ignore' })
        fs.rmSync(dir, { recursive: true, force: true })
      })

      const dockerfile = path.join(dir, 'Dockerfile')
      fs.writeFileSync(dockerfile, nginxStage())
      docker('build', '-q', '-t', id, '-f', dockerfile, '.')
      const run = (name: string, env: string[], detach: boolean) =>
        spawnSync('docker', ['run', '--name', `${id}-${name}`, '-p', '127.0.0.1::80',
          ...env.flatMap((entry) => ['-e', entry]), ...(detach ? ['-d'] : []), id], { encoding: 'utf8', timeout: 60_000 })

      const served = [
        ['default', [], [policyWith(DEFAULT_CONNECT_SRC), null]],
        ['enforce', [`CSP_CONNECT_SRC=${NARROW_CONNECT_SRC}`, 'CSP_MODE=enforce'], [policyWith(NARROW_CONNECT_SRC), null]],
        ['report-only', [`CSP_CONNECT_SRC=${NARROW_CONNECT_SRC}`, 'CSP_MODE=report-only'],
          [policyWith(DEFAULT_CONNECT_SRC), policyWith(NARROW_CONNECT_SRC)]],
      ] as const
      for (const [name, env, expected] of served) {
        expect(run(name, [...env], true).status).toBe(0)
        await eventually(() => cspHeaders(portOf(`${id}-${name}`)), JSON.stringify(expected), 30_000)
      }

      const refused = run('refused', ['CSP_MODE=bogus'], false)
      expect(refused.status).not.toBe(0)
      expect(`${refused.stdout}${refused.stderr}`).toContain('CSP_MODE is "bogus"')
    },
  )
})
