import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import fs from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    // Pages reference images as "/src/assets/..." URLs, so the build must ship that folder.
    {
      name: 'copy-unbundled-src-assets',
      apply: 'build',
      closeBundle() {
        fs.cpSync(path.resolve('src/assets'), path.resolve('dist/src/assets'), { recursive: true })
      },
    },
  ],
  // Pinned: CORS in Program.cs allows this exact origin.
  server: { port: 5173, strictPort: true },
})
