import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

// In dev, /api and /health are proxied to the Orchestra API so the browser sees one origin.
// Override the target with ORCHESTRA_API, e.g. in a .env.local file or the shell.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, ".", "");
  const target = env.ORCHESTRA_API || "http://localhost:5080";

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        "/api": { target, changeOrigin: true },
        "/health": { target, changeOrigin: true },
      },
    },
  };
});
