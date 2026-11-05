import { readFileSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import type { Plugin } from 'vite'

const fromRoot = (path: string) => fileURLToPath(new URL(path, import.meta.url))

const NOTICES: Record<string, string> = {
  'LICENSE-echarts': './node_modules/echarts/LICENSE',
  'NOTICE-echarts': './node_modules/echarts/NOTICE',
  'LICENSE-d3': './node_modules/echarts/licenses/LICENSE-d3',
  'LICENSE-zrender': './node_modules/zrender/LICENSE'
}

export const versionOf = (name: string): string =>
  JSON.parse(readFileSync(fromRoot(`./node_modules/${name}/package.json`), 'utf8')).version

/** Writes the licence and notice files of the third-party code a build embeds into its output, under `folder`. */
export function thirdPartyNotices(folder = ''): Plugin {
  return {
    name: 'third-party-notices',
    generateBundle() {
      for (const [fileName, source] of Object.entries(NOTICES)) {
        this.emitFile({ type: 'asset', fileName: folder ? `${folder}/${fileName}` : fileName, source: readFileSync(fromRoot(source), 'utf8') })
      }
    }
  }
}
