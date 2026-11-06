import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

// Dev: everything goes through the Vite origin (http://localhost:5173), so the auth cookie and the
// OAuth callback (http://localhost:5173/auth/callback) live on one origin. changeOrigin=false keeps
// the Host header, so ASP.NET builds the callback URL with the Vite host.
const api = 'http://localhost:5080'
const proxy = { target: api, changeOrigin: false }

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      // Custom service worker (src/sw.ts): precache + Web Push + notification clicks.
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      injectManifest: { globPatterns: ['**/*.{js,css,html,svg,png,woff2}'] },
      devOptions: { enabled: true, type: 'module' }, // push works in `npm run dev` too
      includeAssets: ['icon.svg'],
      manifest: {
        name: 'GoogleSlacking',
        short_name: 'GoogleSlacking',
        description: 'A fast Google Chat client',
        theme_color: '#171b1f',
        background_color: '#171b1f',
        display: 'standalone',
        start_url: '/',
        icons: [{ src: 'icon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any maskable' }],
      },
    }),
  ],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': proxy,
      '/auth': proxy,
      '/healthz': proxy,
      '/hubs': { ...proxy, ws: true },
    },
  },
})
