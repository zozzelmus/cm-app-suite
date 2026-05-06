/// <reference types="vitest/config" />
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

// Aspire injects PORT (set by AddViteApp) and HTTPS env vars; Vite respects them.
// In standalone (`pnpm dev`) we proxy /api and /bff to the BFF on https://localhost:7001.
const BFF_URL = process.env.VITE_BFF_URL ?? 'https://localhost:7001'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    port: Number(process.env.PORT) || 5173,
    proxy: {
      '/api': { target: BFF_URL, changeOrigin: true, secure: false },
      '/bff': { target: BFF_URL, changeOrigin: true, secure: false },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
})
