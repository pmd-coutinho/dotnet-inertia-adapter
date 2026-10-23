import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import vue from '@vitejs/plugin-vue'
import laravel from 'laravel-vite-plugin'

export default defineConfig({
  plugins: [
    laravel({ input: ['src/react.tsx', 'src/vue.ts'], publicDirectory: '../wwwroot', buildDirectory: 'build', ssr: 'src/ssr.ts' }),
    react(), vue(),
  ],
  ssr: { noExternal: ['@inertiajs/react', '@inertiajs/vue3', '@inertiajs/core'] },
})
