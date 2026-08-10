import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import federation from '@originjs/vite-plugin-federation'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    (federation as any)({
      name: 'omniconnect',
      filename: 'remoteEntry.js',
      exposes: {
        './Dashboard': './src/pages/Dashboard.tsx',
        // './authStore' is added in the auth phase, once the store file exists — it lets a
        // host hydrate identity across the federation boundary. The dead './Campaign' entry
        // was removed here: it pointed at src/pages/Campaign.tsx, a legacy file no route renders.
      },
      // Object form, not the array form. The array form expands to `singleton: false`,
      // which loads a second copy of React in a host (invalid hook calls under React 19)
      // and — more subtly — gives each side its own zustand module instance, so the auth
      // token and permission set would never cross the boundary.
      shared: {
        react: { singleton: true, requiredVersion: '^19.0.0' },
        'react-dom': { singleton: true, requiredVersion: '^19.0.0' },
        'react-router-dom': { singleton: true },
        zustand: { singleton: true },
      }
    })
  ],
  build: {
    target: 'esnext',
    minify: false,
    cssCodeSplit: false
  }
})
