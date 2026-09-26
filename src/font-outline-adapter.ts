import * as opentype from 'opentype.js';

/** Project-owned, public-domain 5-by-7 display font encoded as OpenType glyphs. */
export const bundledFonts = [{ id: 'stencil-block', name: 'Stencil Block (public domain)' }] as const;
export type BundledFontId = typeof bundledFonts[number]['id'];

export interface GlyphOutline {
  readonly contours: ReadonlyArray<ReadonlyArray<readonly [number, number]>>;
  readonly bounds: { readonly minX: number; readonly minY: number; readonly maxX: number; readonly maxY: number };
}

const glyphs: Record<string, readonly string[]> = {
  'A': ['01110','10001','10001','11111','10001','10001','10001'], 'B': ['11110','10001','10001','11110','10001','10001','11110'], 'C': ['01111','10000','10000','10000','10000','10000','01111'], 'D': ['11110','10001','10001','10001','10001','10001','11110'], 'E': ['11111','10000','10000','11110','10000','10000','11111'], 'F': ['11111','10000','10000','11110','10000','10000','10000'], 'G': ['01111','10000','10000','10111','10001','10001','01111'], 'H': ['10001','10001','10001','11111','10001','10001','10001'], 'I': ['11111','00100','00100','00100','00100','00100','11111'], 'J': ['00111','00010','00010','00010','10010','10010','01100'], 'K': ['10001','10010','10100','11000','10100','10010','10001'], 'L': ['10000','10000','10000','10000','10000','10000','11111'], 'M': ['10001','11011','10101','10101','10001','10001','10001'], 'N': ['10001','11001','10101','10011','10001','10001','10001'], 'O': ['01110','10001','10001','10001','10001','10001','01110'], 'P': ['11110','10001','10001','11110','10000','10000','10000'], 'Q': ['01110','10001','10001','10001','10101','10010','01101'], 'R': ['11110','10001','10001','11110','10100','10010','10001'], 'S': ['01111','10000','10000','01110','00001','00001','11110'], 'T': ['11111','00100','00100','00100','00100','00100','00100'], 'U': ['10001','10001','10001','10001','10001','10001','01110'], 'V': ['10001','10001','10001','10001','10001','01010','00100'], 'W': ['10001','10001','10001','10101','10101','10101','01010'], 'X': ['10001','10001','01010','00100','01010','10001','10001'], 'Y': ['10001','10001','01010','00100','00100','00100','00100'], 'Z': ['11111','00001','00010','00100','01000','10000','11111'],
  '0': ['01110','10001','10011','10101','11001','10001','01110'], '1': ['00100','01100','00100','00100','00100','00100','01110'], '2': ['01110','10001','00001','00010','00100','01000','11111'], '3': ['11110','00001','00001','01110','00001','00001','11110'], '4': ['00010','00110','01010','10010','11111','00010','00010'], '5': ['11111','10000','10000','11110','00001','00001','11110'], '6': ['01110','10000','10000','11110','10001','10001','01110'], '7': ['11111','00001','00010','00100','01000','01000','01000'], '8': ['01110','10001','10001','01110','10001','10001','01110'], '9': ['01110','10001','10001','01111','00001','00001','01110'],
  '?': ['01110','10001','00001','00010','00100','00000','00100'], '-': ['00000','00000','00000','11111','00000','00000','00000'], ' ': ['00000','00000','00000','00000','00000','00000','00000'], '.': ['00000','00000','00000','00000','00000','00110','00110'], '!': ['00100','00100','00100','00100','00100','00000','00100'],
};

function makeFont(): any {
  const api = opentype as any;
  const fontGlyphs = Object.entries(glyphs).map(([character, rows]) => {
    const path = new api.Path();
    rows.forEach((row, y) => [...row].forEach((cell, x) => {
      if (cell !== '1') return;
      path.moveTo(x, 7 - y); path.lineTo(x + 1, 7 - y); path.lineTo(x + 1, 6 - y); path.lineTo(x, 6 - y); path.close();
    }));
    return new api.Glyph({ name: character, unicode: character.codePointAt(0), advanceWidth: 6, path });
  });
  const font = new api.Font({ familyName: 'Stencil Block', styleName: 'Regular', unitsPerEm: 7, ascender: 7, descender: 0, glyphs: fontGlyphs });
  font.kerningPairs = {};
  return font;
}

const font = makeFont();

/** Converts bundled OpenType glyph paths into closed, template-local millimetre outlines. */
export function captionOutline(text: string, fontId: BundledFontId, size: number): GlyphOutline {
  if (fontId !== 'stencil-block') throw new Error('Choose a bundled caption font.');
  if (!Number.isFinite(size) || size <= 0) throw new Error('Caption size must be a positive number of millimetres.');
  if (!text) return { contours: [], bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 } };
  const path = font.getPath(text.toUpperCase(), 0, 0, size);
  const contours: Array<Array<readonly [number, number]>> = [];
  let contour: Array<readonly [number, number]> | undefined;
  for (const command of path.commands as Array<{ type: string; x?: number; y?: number }>) {
    if (command.type === 'M') { contour = [[command.x!, command.y!]]; contours.push(contour); }
    else if (command.type === 'L' && contour) contour.push([command.x!, command.y!]);
    else if (command.type === 'Z') contour = undefined;
  }
  const points = contours.flat();
  if (!points.length) return { contours: [], bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 } };
  const minX = Math.min(...points.map(([x]) => x)); const minY = Math.min(...points.map(([, y]) => y));
  const normalized = contours.map((line) => line.map(([x, y]) => [x - minX, y - minY] as const));
  const normalizedPoints = normalized.flat();
  return { contours: normalized, bounds: { minX: 0, minY: 0, maxX: Math.max(...normalizedPoints.map(([x]) => x)), maxY: Math.max(...normalizedPoints.map(([, y]) => y)) } };
}
