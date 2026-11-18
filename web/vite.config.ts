import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import mkcert from 'vite-plugin-mkcert'
import { thirdPartyNotices } from './vite.notices'

export default defineConfig({
  // Tests read no .env file, as in CI, so a developer's local API address cannot change what a test expects.
  envDir: process.env.VITEST ? false : undefined,
  test: {
    environment: 'jsdom',
    pool: 'threads',
    globals: true,
    include: ['src/**/*.{test,spec}.{ts,vue}', 'docker/**/*.test.ts'],
    setupFiles: ['src/test-setup.ts'],
    css: { include: [/\.css\?raw$/] },
  },
  plugins: [
    vue(),
    tailwindcss(),
    // mkcert provisions a local trusted dev cert; skip under Vitest where
    // the trust install step requires sudo and is irrelevant.
    ...(process.env.VITEST ? [] : [mkcert()]),
    thirdPartyNotices('third-party'),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@atoms': fileURLToPath(
        new URL('./src/components/atoms', import.meta.url),
      ),
      '@layouts': fileURLToPath(
        new URL('./src/components/layouts', import.meta.url),
      ),
      '@ui': fileURLToPath(new URL('./src/components/ui', import.meta.url)),
    },
  },
  server: {
    port: 8080,
  },
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'vendor-vue', test: /node_modules\/(vue|vue-router|pinia|@vueuse\/core)\//, priority: 20 },
            { name: 'vendor-ui', test: /node_modules\/(reka-ui|@lucide\/vue)\//, priority: 20 },
            { name: 'vendor-charts', test: /node_modules\/(echarts|zrender)\//, priority: 20 },
            { name: 'vendor-forms', test: /node_modules\/(vee-validate|@vee-validate\/zod|zod)\//, priority: 20 },
            { name: 'vendor-icons', test: /node_modules\/@fortawesome\//, priority: 20 },
            { name: 'vendor-data', test: /node_modules\/(@tanstack\/vue-query|@tanstack\/vue-table|axios)\//, priority: 20 },
            { name: 'vendor-utils', test: /node_modules\/(date-fns|class-variance-authority|tailwind-merge)\//, priority: 20 },
          ],
        },
      },
    },
  },
})
