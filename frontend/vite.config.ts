/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  resolve: {
    // `@/shared/ui` instead of `../../shared/ui`; mirrored in tsconfig.json "paths"
    alias: { '@': '/src' },
  },
  server: {
    port: 5173,
    // `npm run dev` sends the API to the backend started with `dotnet run` (mock data, see docs/development.md)
    proxy: { '/api': { target: 'http://localhost:5080', ws: true } }, // ws: the VM console
  },
  build: {
    // deploy/www: the frontend of the build hosts get (build.sh, CI); install.sh copies it to /var/www/home for nginx
    outDir: '../deploy/www',
    // noVNC uses top-level await (Safari 15+, Chrome 89+)
    target: 'es2022',
    emptyOutDir: true,
  },
  test: {
    include: ['src/**/*.test.ts'],
  },
})
