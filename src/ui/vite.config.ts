import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development the API runs on :5080; in Azure the API serves this build from wwwroot (one origin).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5080",
      "/health": "http://localhost:5080",
    },
  },
  build: { outDir: "dist", sourcemap: true },
});
