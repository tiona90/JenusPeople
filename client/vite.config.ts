import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import mkcert from 'vite-plugin-mkcert'
import { VitePWA } from 'vite-plugin-pwa'

// Use the explicit IPv4 loopback address to avoid intermittent `localhost`
// resolution issues on Windows/Node that can surface as Vite proxy ECONNREFUSED logs.
const apiProxyTarget = 'http://127.0.0.1:5000'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    mkcert(),
    react(),
    // PWA: registers a Service Worker (autoUpdate) and emits manifest.json so
    // the app can be installed to a phone home screen. Icons currently reuse
    // /favicon.svg — modern browsers accept SVG, but Android install prompts
    // prefer 192/512 PNGs. Add real PNG assets to client/public/ and append
    // them to manifest.icons[] when you have artwork.
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg'],
      manifest: {
        name: 'Jenus People — Leave & Timesheet',
        short_name: 'Jenus People',
        description: 'Submit leave requests, log timesheets, and track attendance.',
        theme_color: '#0f766e',
        background_color: '#f4f5f2',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        scope: '/',
        icons: [
          {
            src: '/favicon.svg',
            sizes: 'any',
            type: 'image/svg+xml',
            purpose: 'any',
          },
          {
            src: '/favicon.svg',
            sizes: 'any',
            type: 'image/svg+xml',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        // Cache the SPA shell + Vite-emitted JS/CSS. API calls (/api/*) and
        // SignalR (/hubs/*) are NEVER cached so authenticated data stays live;
        // navigation falls back to index.html when offline.
        globPatterns: ['**/*.{js,css,html,svg,png,ico}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/hubs\//],
        runtimeCaching: [],
      },
      devOptions: {
        // Enabling the SW in dev is opt-in: vite-plugin-pwa serves a no-op SW
        // during `npm run dev` so you can verify the install path with HTTPS
        // (via mkcert) without it interfering with HMR.
        enabled: false,
        type: 'module',
      },
    }),
  ],
  server: {
    https: {},
    proxy: {
      '/api': {
        target: apiProxyTarget,
        changeOrigin: true,
      },
      '/hubs': {
        target: apiProxyTarget,
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
