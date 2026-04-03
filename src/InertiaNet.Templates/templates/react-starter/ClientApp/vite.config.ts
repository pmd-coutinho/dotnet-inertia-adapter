import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import laravel from 'laravel-vite-plugin'

export default defineConfig({
  plugins: [
    laravel({
      input: ['src/app.tsx'],
      publicDirectory: '../wwwroot',
      buildDirectory: 'build',
      refresh: true,
    }),
    react(),
  ],
})
