import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const indexPath = resolve('dist/index.html');
if (!existsSync(indexPath)) throw new Error('Static build did not create dist/index.html.');
const index = readFileSync(indexPath, 'utf8');
if (/\b(?:src|href)="\/(?!\/)/.test(index)) {
  throw new Error('Static build contains root-bound asset URLs and cannot be hosted at a project subpath.');
}
if (!/\.\/assets\//.test(index)) throw new Error('Static build does not reference relative bundled assets.');
if (!readdirSync(resolve('dist/assets')).some((file) => file.endsWith('.wasm'))) {
  throw new Error('Static build does not include the Manifold WASM asset.');
}
console.log('Static GitHub Pages build check passed.');
