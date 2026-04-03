import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import laravel from 'laravel-vite-plugin'

export default defineConfig({
  plugins: [
    laravel({
      input: ['src/app.ts'],
      publicDirectory: '../wwwroot',
      buildDirectory: 'build',
      refresh: true,
    }),
    vue(),
  ],
})
