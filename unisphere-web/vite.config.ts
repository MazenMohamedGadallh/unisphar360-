import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// The ASP.NET Core backend (UniSphere.Api) listens on http://localhost:5080.
// During development every /api call is proxied to it, so the browser talks
// to one origin and no CORS setup is needed. Override with VITE_API_PROXY.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const target = env.VITE_API_PROXY || 'http://localhost:5080';
  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': { target, changeOrigin: true },
        '/uploads': { target, changeOrigin: true },
        '/health': { target, changeOrigin: true },
      },
    },
  };
});
