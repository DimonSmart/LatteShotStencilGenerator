import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

const GOOGLE_FONTS_COMMIT = '23e54b51ddffbc7713c583748e3bd86f62b1fa4a';
const fonts = [
  { source: 'ofl/poiretone/PoiretOne-Regular.ttf', output: 'src/assets/fonts/PoiretOne-Regular.ttf.b64', sha: '928f628f66e4bd32d1a7de297150292723f439cb' },
  { source: 'ofl/russoone/RussoOne-Regular.ttf', output: 'src/assets/fonts/RussoOne-Regular.ttf.b64', sha: 'ba837270804167268bcd395a8a063ac26a77b72c' },
  { source: 'ofl/neucha/Neucha.ttf', output: 'src/assets/fonts/Neucha.ttf.b64', sha: 'd495d8361c36203e6d3b31dad61d2d199ad44d05' },
  { source: 'ofl/ptmono/PTM55FT.ttf', output: 'src/assets/fonts/PTM55FT.ttf.b64', sha: 'f580123617dbf5c57488185920a8184fa64bf780' },
];

function gitBlobSha(bytes) {
  return createHash('sha1').update(Buffer.from('blob ' + bytes.length + '\0')).update(bytes).digest('hex');
}

for (const font of fonts) {
  let bytes;
  try { bytes = Buffer.from((await readFile(resolve(font.output), 'utf8')).trim(), 'base64'); } catch {}
  if (!bytes || gitBlobSha(bytes) !== font.sha) {
    const url = 'https://raw.githubusercontent.com/google/fonts/' + GOOGLE_FONTS_COMMIT + '/' + font.source;
    const response = await fetch(url);
    if (!response.ok) throw new Error('Unable to download bundled font ' + font.source + ': HTTP ' + response.status);
    bytes = Buffer.from(await response.arrayBuffer());
    const actual = gitBlobSha(bytes);
    if (actual !== font.sha) throw new Error('Bundled font hash mismatch for ' + font.source + ': expected ' + font.sha + ', got ' + actual);
    await mkdir(dirname(resolve(font.output)), { recursive: true });
    await writeFile(resolve(font.output), bytes.toString('base64') + '\n', 'utf8');
  }
}
