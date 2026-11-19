import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  // Pin the dev server to port 5173: CORS in Program.cs and
  // frontend/.env (VITE_API_BASE_URL) both assume this exact port.
  server: {
    port: 5173,
    strictPort: true,
  },
})
