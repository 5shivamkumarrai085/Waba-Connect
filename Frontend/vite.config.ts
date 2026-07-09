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
        './Campaign': './src/pages/Campaign.tsx',
      },
      shared: ['react', 'react-dom', 'zustand', 'react-router-dom']
    })
  ],
  build: {
    target: 'esnext',
    minify: false,
    cssCodeSplit: false
  }
})

