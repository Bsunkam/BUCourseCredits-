import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development the API runs on :5090; in Azure the API serves this build from wwwroot (one origin).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5190,
    strictPort: true, // fail loudly instead of silently moving to another port
    proxy: {
      "/api": "http://localhost:5090",
      "/health": "http://localhost:5090",
    },
  },
  build: { outDir: "dist", sourcemap: true },
});
