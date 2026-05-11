import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],

  build: {
    // Output directly into the server's wwwroot so ASP.NET Core
    // static-file middleware picks it up without any manual copy step.
    outDir:      '../VRCGT.Server/wwwroot',
    emptyOutDir: true,
  },

  server: {
    port: 5173,
    // During `npm run dev`, proxy API calls to the running local server.
    // Change the target port if you started VRCGT.Server on a different port.
    proxy: {
      '/api':    { target: 'http://127.0.0.1:57432', changeOrigin: false },
      '/health': { target: 'http://127.0.0.1:57432', changeOrigin: false },
      '/hubs':   { target: 'http://127.0.0.1:57432', changeOrigin: false, ws: true },
    },
  },
})
