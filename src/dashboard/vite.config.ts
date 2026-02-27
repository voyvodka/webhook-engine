import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: "../WebhookEngine.API/wwwroot",
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks: {
          vendor: ["react", "react-dom", "react-router"],
          charts: ["recharts"],
          signalr: ["@microsoft/signalr"]
        }
      }
    }
  },
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5128",
        changeOrigin: true
      },
      "/health": {
        target: "http://localhost:5128",
        changeOrigin: true
      },
      "/metrics": {
        target: "http://localhost:5128",
        changeOrigin: true
      },
      "/hubs": {
        target: "http://localhost:5128",
        changeOrigin: true,
        ws: true
      }
    }
  }
});
