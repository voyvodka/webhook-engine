import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: "../WebhookEngine.API/wwwroot",
    emptyOutDir: true,
    rolldownOptions: {
      output: {
        manualChunks(id: string) {
          if (id.includes("react-dom") || id.includes("react-router") || id.includes("/react/")) return "vendor";
          if (id.includes("recharts")) return "charts";
          if (id.includes("signalr")) return "signalr";
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
