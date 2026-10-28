import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import laravel from "laravel-vite-plugin";

export default defineConfig({
  plugins: [
    laravel({
      input: ["src/style.css", "src/app.tsx"],
      publicDirectory: "../wwwroot",
      buildDirectory: "build",
      ssr: "src/ssr.tsx",
    }),
    react(),
  ],
  server: {
    host: "127.0.0.1",
    port: Number(process.env.PORT ?? 5174),
    strictPort: true,
    cors: { origin: /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/ },
    // InertiaNet sends hot-mode SSR requests to Vite; forward them to the Node renderer.
    proxy: {
      "/__inertia_ssr": {
        target: process.env.SSR_URL ?? "http://127.0.0.1:13715",
        rewrite: () => "/render",
      },
    },
  },
  ssr: { noExternal: ["@inertiajs/react", "@inertiajs/core"] },
});
