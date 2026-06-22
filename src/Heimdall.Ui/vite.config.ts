import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The console is served from the gateway's reserved /_apim namespace, so asset URLs and the router
// basename both live under /_apim. The production build writes straight into the Api's wwwroot so the
// .NET host can serve it (the Docker node stage automates this later in the plan chain).
export default defineConfig({
  base: '/_apim/',
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  build: {
    outDir: path.resolve(import.meta.dirname, '../Heimdall.Api/wwwroot/console'),
    emptyOutDir: true,
  },
  server: {
    // Dev: the SPA runs on Vite under /_apim/ and proxies only the concrete admin endpoints to the
    // running .NET host. Scoped to specific paths (not the whole /_apim tree) so it never shadows the
    // SPA's own document/assets. ws:true carries the SignalR trace hub.
    proxy: {
      // /health has no /_apim prefix; the Overview surface polls it, so proxy it to the host too.
      '/health': 'http://localhost:8080',
      '/_apim/config': 'http://localhost:8080',
      '/_apim/policies': 'http://localhost:8080',
      '/_apim/traces': 'http://localhost:8080',
      // /_apim/playground is both an SPA page (GET) and the replay API (POST). Let GET fall through
      // to Vite so the page renders; proxy only the non-GET (replay) calls to the host.
      '/_apim/playground': {
        target: 'http://localhost:8080',
        bypass: (req) => (req.method === 'GET' ? req.url : undefined),
      },
      '/_apim/hub': { target: 'http://localhost:8080', ws: true },
    },
  },
})
