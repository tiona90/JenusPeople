// `vitest/config` re-exports Vite's defineConfig with the `test` block typed.
import { defineConfig } from 'vitest/config'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import mkcert from 'vite-plugin-mkcert'
import { VitePWA } from 'vite-plugin-pwa'

// Use the explicit IPv4 loopback address to avoid intermittent `localhost`
// resolution issues on Windows/Node that can surface as Vite proxy ECONNREFUSED logs.
const apiProxyTarget = 'http://127.0.0.1:5000'

/**
 * Dev-only: evict a stale PWA service worker from the dev origin.
 *
 * `npm run dev` intentionally ships no service worker (devOptions.enabled is
 * false), but a worker registered earlier from a production build keeps control
 * of https://localhost:5174 and serves its own precached app shell. That shell
 * can resurrect UI which has since been deleted from the source — while `/api`
 * calls still hit the live backend, producing a confusing half-old page.
 *
 * It never recovers on its own: the SPA history fallback answers `/sw.js` with
 * index.html, so the browser's update check receives `text/html`, fails the
 * script's MIME check, and leaves the old worker installed indefinitely. Serving
 * a real script here lets that update check succeed with a worker whose only job
 * is to drop every cache, unregister itself, and reload open tabs.
 */
function evictStaleServiceWorker(): Plugin {
  const selfDestructingWorker = `
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const keys = await caches.keys()
    await Promise.all(keys.map((key) => caches.delete(key)))
    await self.registration.unregister()
    const windows = await self.clients.matchAll({ type: 'window' })
    windows.forEach((client) => client.navigate(client.url))
  })())
})
`.trimStart()

  return {
    name: 'evict-stale-service-worker',
    apply: 'serve',
    configureServer(server) {
      // Registered outside the returned-callback form so it runs *before* Vite's
      // SPA fallback, which would otherwise answer this path with index.html.
      server.middlewares.use('/sw.js', (_req, res) => {
        res.setHeader('Content-Type', 'application/javascript')
        res.setHeader('Cache-Control', 'no-store')
        res.end(selfDestructingWorker)
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    evictStaleServiceWorker(),
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
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // Only pick up our own specs; without this, Vitest also walks node_modules.
    include: ['src/**/*.test.{ts,tsx}'],
  },
  server: {
    port: 5174,
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
