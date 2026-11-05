import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import { thirdPartyNotices, versionOf } from './vite.notices'

const fromRoot = (path: string) => fileURLToPath(new URL(path, import.meta.url))

const BANNER = `/*!
 * Draws Dynamic Chart blocks for the Word export of IniaAi.Platform.
 * Built by the Vue app with \`npm run build:chart-builder\`. Do not edit: copy this folder to
 * the platform's IniaAi.Services/Resources/chartBuilder/ whenever the build changes it.
 * Embeds Apache ECharts ${versionOf('echarts')} (Apache-2.0: LICENSE-echarts, NOTICE-echarts, LICENSE-d3)
 * and zrender ${versionOf('zrender')} (BSD-3-Clause: LICENSE-zrender).
 */`

export default defineConfig({
  plugins: [thirdPartyNotices()],
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    lib: {
      entry: fromRoot('./src/api/dynamicChart/chartExport.global.ts'),
      name: 'IniaCharts',
      formats: ['iife'],
      fileName: () => 'charts.global.js'
    },
    outDir: 'chart-builder',
    emptyOutDir: true,
    copyPublicDir: false,
    minify: true,
    rolldownOptions: {
      output: { postBanner: BANNER }
    }
  }
})
