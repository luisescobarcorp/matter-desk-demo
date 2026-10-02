import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Same-origin in dev: the browser talks to /api and Vite forwards to Kestrel.
    proxy: { '/api': { target: process.env.API_URL ?? 'http://localhost:5080', changeOrigin: true } },
  },
})
