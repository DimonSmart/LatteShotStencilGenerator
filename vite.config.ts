import { defineConfig } from 'vite';

export default defineConfig({
  // Relative assets make the static bundle work at GitHub Pages project subpaths.
  base: './',
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: true,
  },
});
